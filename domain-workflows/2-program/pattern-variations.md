---
icon: lambda
---

# Addendum: Pattern Variations

{% hint style="success" %}
This page is dedicated to [**John Azariah**](https://johnazariah.github.io/), whose excellent blog posts mentioned below deeply inspired this work. Thank you, John, for sharing these ideas with the community!
{% endhint %}

The Free Monad and Tagless Final patterns originate in Haskell, where they can be expressed directly thanks to language features that F# and C# lack:

* **Type classes** — the foundation of Tagless Final in Haskell (`class Monad m => MyDSL m where ...`). F# and C# use interfaces as a substitute, but interfaces cannot abstract over type constructors.
* **Higher-Kinded Types (HKTs)** — Haskell can abstract over monadic contexts (`Monad m => m a`), which is essential for both patterns. F#/C# generics are first-order: you can write `Program<'a>` but not `Program<M<'a>>` where `M` itself is a parameter.
* **GADTs** (Generalized Algebraic Data Types) — Haskell can give each constructor of a data type its own return type (`CheckStock :: [Item] -> OrderStep StockResult`). F# discriminated unions share a single type parameter across all cases; C# approximates GADTs via record inheritance (`record CheckStock(...) : OrderStep<StockResult>`).

Without these building blocks, F# and C# must **emulate** these patterns rather than implement them directly — each emulation making its own trade-offs in type safety, verbosity, and flexibility. This page explores these variations along several design axes — instruction typing, parallelism, combinator placement, and the trade-off between **generic** (versatile, decoupled) and **turnkey** (integrated, opinionated) designs.

These variations are inspired by [John Azariah's blog](https://johnazariah.github.io/) and the gitbook's own program V3bis experiment.

## John Azariah's Resources

* Series [Tagless Final in F#](https://johnazariah.github.io/2025/12/12/tagless-final-01-froggy-tree-house.html)
* Series [Intent vs Process](https://johnazariah.github.io/2026/03/05/01-your-clean-architecture-has-a-dirty-secret.html) — explores the same Intent-vs-Process separation through an Order Processing domain, comparing Free Monad and Tagless Final in C# and F#
* Article [Choosing Both Sides of the Coin](https://johnazariah.github.io/2026/03/19/choosing-both-sides-of-the-coin.html) — parallelism via `Both` in Free Monad (C#)
* Code repository: [johnazariah.github.io/code/intent-vs-process](https://github.com/johnazariah/johnazariah.github.io/tree/main/code/intent-vs-process) — code in C#, F#, and Haskell

## The Order Domain

John's core domain types are shared across all encodings — they belong to the domain, not the pattern:

```fsharp
type Item = { Sku: string; Name: string; Quantity: int }
// ...
type OrderRequest = { Items: Item list (*...*) ... }

[<Struct>] type StockResult = { IsAvailable: bool }
[<Struct>] type PriceResult = { Total: decimal; Subtotal: decimal; Discount: decimal }
// ...

type OrderResult =
    | Success of transactionId: string
    | Failure of reason: string
```

## Free Monad Variations

### John's Free Monad (F#)

John's Free Monad uses a **flat discriminated union** with no generic type parameter — each case holds only the instruction's input data:

```fsharp
type OrderStep =
    | CheckStock     of Item list
    | CalculatePrice of Item list * Coupon option
    // ...
```

The `Program` type uses `obj` boxing at the continuation boundary:

```fsharp
type Program<'a> =
    | Done   of 'a
    | Failed of string
    | Step   of OrderStep * (obj -> Program<'a>)
```

Two notable design choices:

1. **`Failed of string`** — the program handles failure directly with a string reason, making it independent of any `Result` type. The `guard` function below leverages this to short-circuit the program on a boolean condition.
2. **`Step of OrderStep * ...`** — the step case references the domain-specific `OrderStep` type directly. This means the `Program` type is **tied to the Order domain** — to use it in another domain, you would need to duplicate or generalize it.

In comparison, the gitbook's V3bis addresses both points differently:

1. **Failure handling:** V3bis has no `Failed` case. Instead, it relies on the `Result` type — the CE's `Bind` overload for `Program<Result<_, _>>` short-circuits on `Error`, threading the error through `End(Error e)`. This makes failure handling type-safe and composable (error aggregation via `Result.zip`, typed error lifting), but couples the program to the `Result` and `Error` types.
2. **Domain agnosticism:** V3bis makes `Program<'a>` **domain-agnostic** by using an `IStep` interface instead of a concrete union type. A sub-interface `IInterpretableStep<'union>` then bridges back to the domain's instruction DU, restoring exhaustive pattern matching in the interpreter — at the cost of a type test (`match step with :? IInterpretableStep<ProductInstruction> as s -> ...`), which is less safe and less performant than a direct match on the DU.

Smart constructors—that I called Program helpers in the GitBook—restore type safety at the call site via `unbox`:

```fsharp
module Program =
    ...

    let lift (step: OrderStep) : Program<'a> =
        Step(step, fun result -> Done(unbox<'a> result))

    let guard condition reason =
        if condition then Done() else Failed reason


// ─── Smart constructors ──────────────────────────────────────────────

module Order =
    let checkStock items : Program<StockResult> = Program.lift (CheckStock items)
    let calculatePrice items coupon : Program<PriceResult> = Program.lift (CalculatePrice(items, coupon))
    ...
    let guard cond reason : Program<unit> = Program.guard cond reason
```

They are used in the `placeOrder` program—what is called _domain workflow_ in the GitBook—making use of the `program` computation expression:

```fsharp
module FreeMonad =
    let placeOrder (req: OrderRequest) : Program<OrderResult> =
        order {
            let! stock = Order.checkStock req.Items
            do! Order.guard stock.IsAvailable "Out of stock"

            let! price = Order.calculatePrice req.Items req.Coupon
            ...

            return ...
        }
```

The key advantage: the flat DU makes **structural analysis** trivial. Each step can be pattern-matched without unwrapping generic interfaces:

```fsharp
let rec flatten (program: Program<'a>) : OrderStep list =
    match program with
    | Done _ | Failed _ -> []
    | Step(step, k) ->
        let next = k (defaultExecutor step)
        step :: flatten next

let analyze (program: Program<OrderResult>) : ExecutionPlan =
    let steps = flatten program
    // Count DB calls, payment calls, estimate latency, cost...
```

{% hint style="info" %}
John trades **type safety** at the continuation boundary (`obj` boxing/unboxing) for **inspectability** — the flat DU enables structural analysis that would be much harder with generic interfaces.
{% endhint %}

### John's Free Monad (C#)

In C#, John uses a **GADT-like** pattern that F# discriminated unions cannot express: each step record carries its **result type** as a generic parameter:

```csharp
public abstract record OrderStep<T> : OrderStepBase;

public record CheckStock(List<Item> Items) : OrderStep<StockResult>;
public record CalculatePrice(List<Item> Items, Coupon? Coupon) : OrderStep<PriceResult>;
...
```

Here, `CheckStock` _is_ an `OrderStep<StockResult>` — the return type is encoded in the type itself. This is possible in C# because each step is a separate record inheriting from a generic base, whereas F# discriminated unions share a single type parameter across all cases (no GADTs).

The `OrderProgram<T>` type mirrors the F# version — `Done`, `Failed`, `Bind` — plus a `Both<T>` case for parallelism (added in his latest article):

```csharp
public abstract record OrderProgram<T>;
public record Done<T>(T Value) : OrderProgram<T>;
public record Failed<T>(string Reason) : OrderProgram<T>;
public record Bind<T>(OrderStepBase Step, Func<object, OrderProgram<T>> Continue) : OrderProgram<T>;
public record Both<T>(OrderProgram<object> Left, OrderProgram<object> Right, Func<object, object, OrderProgram<T>> Combine) : OrderProgram<T>;
```

John then provides **LINQ extensions** that make `OrderProgram<T>` composable using C#'s query syntax — the equivalent of F#'s computation expression:

```csharp
// Functor
OrderProgram<TResult> Select<T, TResult>(this OrderProgram<T> source, Func<T, TResult> selector)
// Monad bind
OrderProgram<TResult> SelectMany<T, TResult>(this OrderProgram<T> source, Func<T, OrderProgram<TResult>> selector)
// Guard (short-circuits to Failed)
OrderProgram<T> Where<T>(this OrderProgram<T> source, Func<T, bool> predicate)
// Applicative (creates Both nodes)
OrderProgram<(TA, TB)> Parallel<TA, TB>(OrderProgram<TA> left, OrderProgram<TB> right)
```

The `PlaceOrder` program reads like a declarative workflow thanks to LINQ:

```csharp
public static OrderProgram<OrderResult> PlaceOrder(OrderRequest request) =>
    from stock in Lift(new CheckStock(request.Items))
    where stock.IsAvailable
    from price in Lift(new CalculatePrice(request.Items, request.Coupon))
    ...
    select ...;
```

The interpreter then decides whether to run `Both` branches sequentially or concurrently — the program structure declares _intent_, the interpreter chooses _strategy_.

### GitBook V3: Emulating Algebraic Effects

The gitbook's V3 was an attempt to emulate **algebraic effects** in F# using a free monad and interfaces. The core idea: effects are represented as a functor interface (`IProgramEffect<'a>`) that the `Program` type wraps recursively:

```fsharp
[<Interface>]
type IProgramEffect<'a> =
    abstract member Map: f: ('a -> 'b) -> IProgramEffect<'b>

type Program<'ret> =
    | Stop of 'ret
    | Effect of IProgramEffect<Program<'ret>>
```

Each domain then defines its instructions following a **5-step recipe** (see [Algebraic Effects — V3 Design](../1-introduction/4-algebraic-effects.md#v3-design)). The key building block is `Instruction<'arg, 'ret, 'a>` — a full typed class capturing the argument, the return type, and a continuation:

```fsharp
type Instruction<'arg, 'ret, 'a>(name: string, arg: 'arg, cont: 'ret -> 'a) =
    member _.Map(f: 'a -> 'b) = Instruction(name, arg, cont >> f)
    member _.Run(runner) = runner arg |> cont
    // ...

type Command<'arg, 'a> = Instruction<'arg, Result<unit, Error>, 'a>
type Query<'arg, 'ret, 'a> = Instruction<'arg, 'ret option, 'a>
```

Here is John's Order domain adapted to the V3 pattern (showing only `CheckStock` and `CalculatePrice` for brevity):

```fsharp
// ─── 1: Instructions ────────────────────────────────────────────────
type CheckStockCommand<'a> = Command<Item list, 'a>  // Ok() if available, Error "OutOfStock" otherwise
type CalculatePriceQuery<'a> = Query<Item list * Coupon option, PriceResult, 'a>
// ...

// ─── 2: Instruction union ───────────────────────────────────────────
type OrderInstruction<'a> =
    | CheckStock of CheckStockCommand<'a>
    | CalculatePrice of CalculatePriceQuery<'a>
    // ...

// ─── 3: Effect interface ─────────────────────────────────────────────
type IOrderEffect<'a> =
    inherit IProgramEffect<'a>
    inherit IInterpretableEffect<OrderInstruction<'a>>

// ─── 4: Effect classes (1 per instruction) ───────────────────────────
type CheckStockEffect<'a>(command: CheckStockCommand<'a>) =
    interface IOrderEffect<'a> with
        override _.Map(f) = CheckStockEffect(command.Map f)
        override val Instruction = CheckStock command

type CalculatePriceEffect<'a>(query: CalculatePriceQuery<'a>) =
    interface IOrderEffect<'a> with
        override _.Map(f) = CalculatePriceEffect(query.Map f)
        override val Instruction = CalculatePrice query
// ...

// ─── 5: Smart constructors (Program helpers) ─────────────────────────
module Order =
    let checkStock = Program.effect CheckStockEffect CheckStockCommand
    let calculatePrice = Program.effect CalculatePriceEffect CalculatePriceQuery
    // ...
```

The workflow can use the `program` computation expression:

```fsharp
let placeOrder (req: OrderRequest) = program {
    do! Order.checkStock req.Items       // short-circuits on Error (e.g. "OutOfStock")
    let! price = Order.calculatePrice (req.Items, req.Coupon)
    ...
}
```

### GitBook V3bis

The **V3bis** is a hybrid that combines V3's typed instruction approach with John's `obj`-boxing flexibility, adding a `Parallel` case to the Program ADT. The full implementation is available on the [`program-v3`](https://github.com/rdeneau/shopfoo/tree/program-v3) branch.

#### Program ADT

The `Program<'a>` type has three cases — including `Parallel` for applicative composition:

```fsharp
type Program<'a> =
    | End of 'a
    | Step of step: IStep * cont: (obj -> Program<'a>)
    | Parallel of left: Program<obj> * right: Program<obj> * merge: (obj * obj -> Program<'a>)
```

Like John's approach, the continuation in `Step` uses `obj` boxing. Unlike V3, there is no functor interface (`IProgramEffect.Map`) — this is what makes `Parallel` possible.

#### Fully Typed Instructions

Instructions carry both request and response types:

```fsharp
type Instruction<'req, 'res>(request: 'req) =
    member val Request = request

type Command<'req> = Instruction<'req, Result<unit, Error>>
type Query<'req, 'res> = Instruction<'req, 'res option>
```

Unlike John's flat `OrderStep` DU (where the return type is only known at the smart constructor level), `Instruction<'req, 'res>` makes the response type explicit at the instruction definition. Compare:

```fsharp
// John's F# Free Monad — return type not visible on the DU case:
type OrderStep =
    | CheckStock     of Item list
    | CalculatePrice of Item list * Coupon option
    // ...

// V3bis — return type explicit on the instruction:
type CheckStockCommand = Command<Item list>  // Ok() if available, Error "OutOfStock" otherwise
type CalculatePriceQuery = Query<Item list * Coupon option, PriceResult>
```

```csharp
// John's C# Free Monad — return type baked in the step definitions:
public abstract record OrderStep<T> : OrderStepBase;

public record CheckStock(List<Item> Items) : OrderStep<StockResult>;
public record CalculatePrice(List<Item> Items, Coupon? Coupon) : OrderStep<PriceResult>;
...
```

#### Domain Instructions

Each domain defines its instruction set as a DU wrapping typed instructions:

```fsharp
type OrderInstruction =
    | CheckStock of CheckStockCommand
    | CalculatePrice of CalculatePriceQuery
    // ...
```

Smart constructors use `StepImplBuilder` to lift instructions into programs:

```fsharp
module Program =
    type private Step = StepImplBuilder<OrderInstruction>

    let checkStock = Step.lift CheckStock CheckStockCommand
    let calculatePrice = Step.lift CalculatePrice CalculatePriceQuery
    // ...
```

#### Parallel Execution

The `map2` combinator creates `Parallel` nodes by boxing both sub-programs:

```fsharp
let map2 (f: 'a -> 'b -> 'c) (progA: Program<'a>) (progB: Program<'b>) : Program<'c> =
    Parallel(map box progA, map box progB, fun (a, b) -> End(f (unbox<'a> a) (unbox<'b> b)))
```

This enables the `let! ... and! ...` syntax in the `program` CE — exactly like John's `Both<T>` in C#:

```fsharp
// (Shopfoo instructions)
program {
    let! resA = Program.addProduct product
    and! resB = Program.addPrices (Prices.Initial(sku, currency))
    return Result.zip resA resB |> Result.map ignore
}
```

The interpreter transforms the program AST into an `Async` value, executing each step asynchronously. For the `Parallel` case, it launches both branches concurrently via `Async.StartChild`:

```fsharp
member this.Run<'r>(program: Program<'r>) : Async<'r> =
    match program with
    | End res -> async { return res }
    | Step(As(step: 'step), cont) ->
        async {
            let! res = runStep step
            return! this.Run(cont res)
        }
    | Parallel(left, right, merge) ->
        async {
            let! childLeft = Async.StartChild(this.Run left)
            let! rightResult = this.Run right
            let! leftResult = childLeft
            return! this.Run(merge (leftResult, rightResult))
        }
```

#### Structural Testing

Because the program is an AST, it can be introspected without execution — including parallel structure:

```fsharp
type ProgramEntry =
    | Instruction of string
    | ParallelInstructions of string list

let describe (prog: Program<'a>) : ProgramEntry list
```

Usage in tests:

```fsharp
type AddProductShould() =
    [<Test>]
    member _.``run addProduct and addPrices in parallel``() =
        let prog = AddProductWorkflow.Instance.Run(createValidBookProduct (), Currency.EUR)
        let entries = Program.describe prog
        entries =! [ Program.ParallelInstructions [ "AddProduct"; "AddPrices" ] ]
```

### Free Monad Comparison

| Aspect              | John C#                         | John F#                     | Gitbook V3bis                            | Gitbook V3                             |
| ------------------- | ------------------------------- | --------------------------- | ---------------------------------------- | -------------------------------------- |
| Instruction type    | GADT-like `OrderStep<T>`        | Flat DU (no `'a`)           | DU wrapping `Instruction<'req, 'res>`    | Generic DU with typed continuations    |
| Return type visible | On `OrderStep<T>` type          | In smart constructors only  | On instructions (`Query<SKU, Prices>`)   | On `Instruction<'arg, 'ret, 'a>` type  |
| Continuation        | `Func<object, OrderProgram<T>>` | `obj -> Program<'a>`        | `obj -> Program<'a>`                     | Typed: `'ret -> 'a`                    |
| Structural analysis | Easy (flat records)             | Easy (flat DU)              | Easy (`IStep.Name` + `Program.describe`) | Hard (wrapped in `IProgramEffect<'a>`) |
| Parallelism         | `Both<T>` record                | Not implemented             | `Parallel` case + `map2`                 | Blocked (functor Map requirement)      |
| Functor (Map)       | Not needed                      | Not needed                  | Not needed                               | Required on every effect class         |
| Boilerplate         | Low                             | Low                         | Medium (\~20 lines for 7 instructions)   | High (\~45 lines for 5 instructions)   |
| New domain cost     | High (duplicate everything)     | High (duplicate everything) | Low (domain-specific only)               | Low (domain-specific only)             |

{% hint style="info" %}
### Notes

**New domain cost:** John's approaches tie `Program<'a>`, the CE/LINQ extensions, and the interpreter loop to the Order domain — adding a new domain means duplicating all of them. The gitbook's V3/V3bis make `Program<'a>`, the CE builder, and the interpreter loop **domain-agnostic** — only the instruction DU and smart constructors (+ effect classes for V3) are domain-specific.

**Inspectability vs type safety:** John trades type safety for inspectability. V3 trades inspectability for type safety. V3bis and John C# find middle grounds — V3bis recovers inspectability and parallelism by adopting `obj` boxing, while John C# uses GADT-like records to keep return types explicit.
{% endhint %}

## Tagless Final Variations

### John's Tagless Final (C#)

John's C# version uses an `IOrderAlgebra<TResult>` interface — the generic `TResult` parameter represents the **output type of the algebra**, not the output of individual instructions:

```csharp
public interface IOrderAlgebra<TResult>
{
    TResult CheckStock(List<Item> items);
    TResult CalculatePrice(List<Item> items, Coupon? coupon);
    TResult ChargePayment(PaymentMethod method, decimal amount);
    // ...

    TResult Then<T>(TResult first, Func<T, TResult> next);
    TResult Done(OrderResult result);
    TResult Guard(TResult value, Func<bool> predicate, string failureReason);
}
```

The same approach appears in John's F# Tagless Final series with `FrogInterpreter<'a>`.

This design opens **many interpretation possibilities**: `TResult` can be `OrderResult` (actual execution), `string` (narrative), `AuditEntry list` (dry-run), `Task<OrderResult>` (async execution), etc. However, instruction signatures like `TResult CheckStock(List<Item> items)` don't reveal the **nominal return type** of each instruction (e.g., `StockResult`). That information only appears at the call site, via `alg.Then<StockResult>(...)`, which specifies what type to cast the result to in the continuation.

### Gitbook V4

The gitbook's V4 restores the shared workflow by using an **interface** as the algebra:

```fsharp
[<Interface>]
type IOrderInstructions =
    inherit IProgramInstructions
    abstract member CheckStock: (Item list -> Async<StockResult>)
    abstract member CalculatePrice: (Item list * Coupon option -> Async<PriceResult>)
    // ...
```

Helpers/smart constructors via `DefineProgram` type alias that fixes the domain instructions type and enables IntelliSense:

```fsharp
type private DefineProgram = DefineProgram<IOrderInstructions>

let checkStock items       = DefineProgram.instruction _.CheckStock(items)
let calculatePrice items c = DefineProgram.instruction _.CalculatePrice(items, c)
// ...
```

The **sequential workflow** is identical to V3's CE syntax:

```fsharp
let placeOrder (req: OrderRequest) = program {
    do! = checkStock req.Items
    let! price = calculatePrice req.Items req.Coupon
    // ...
    return ...
}
```

The **V4 superpower** — parallel execution via `let! ... and! ...`:

```fsharp
let placeOrderParallel (req: OrderRequest) = program {
    // Stock check and price calculation can run in parallel
    let! _ = checkStock req.Items
    and! price = calculatePrice req.Items req.Coupon
    // ...
}
```

### Tagless Final Comparison

| Aspect                | John F# (Frog series)                      | John C#                                   | Gitbook V4                       |
| --------------------- | ------------------------------------------ | ----------------------------------------- | -------------------------------- |
| Abstraction           | `FrogInterpreter<'a>` record               | `IOrderAlgebra<TResult>` interface        | `IOrderInstructions` interface   |
| Program type          | `FrogInterpreter<'a> -> 'a`                | `OrderProgram<T>` abstract record         | `'ins -> Async<'ret>` (ReaderT)  |
| Result type           | Generic `'a`                               | Generic `TResult`                         | Fixed: `Async<'ret>`             |
| Return type visible   | At interpreter instantiation               | At call site (`alg.Then<StockResult>`)    | On interface member              |
| Composition           | CE (`frog { ... }`) + `product` combinator | `alg.Then<T>` / LINQ query                | CE (`program { ... }`)           |
| Multiple interpreters | One program, swap interpreter record       | One program, swap `IOrderAlgebra` impl    | One program, swap interface impl |
| Parallelism           | Idem F#                                    | Only after `ToFreeMonad` + interpretation | `let! ... and! ...` via `map2`   |

{% hint style="info" %}
All three Tagless Final approaches share the workflow once — the key difference is how: John F# uses a generic record whose `'a` parameter determines the interpretation mode (string, state, graph…); John C# uses an OO interface with a generic `TResult`; V4 uses an instruction interface with a fixed `Async` result type.
{% endhint %}

## Cross-Cutting Analysis

### Where Instruction Return Types Are Defined

A key design difference: where does the **nominal return type** of an instruction (e.g., `StockResult` for `CheckStock`) become visible?

| Variant            | Where `'res` is defined    | Example                                                         |
| ------------------ | -------------------------- | --------------------------------------------------------------- |
| John F# Free Monad | At smart constructor level | `let checkStock items : Program<StockResult> = ...`             |
| John C# Free Monad | On step type (GADT-like)   | `record CheckStock(...) : OrderStep<StockResult>`               |
| Gitbook V2         | In typed continuation      | `CheckStock of Item list * (StockResult -> 'a)`                 |
| Gitbook V3         | On instruction type        | `type GetPricesQuery<'a> = Query<SKU, Prices, 'a>`              |
| Gitbook V3bis      | On instruction type        | `type GetPricesQuery = Query<SKU, Prices>`                      |
| John Tagless Final | At use site in combinator  | `alg.Then<StockResult>(alg.CheckStock(items), ...)`             |
| Gitbook V4         | On interface member        | `abstract member CheckStock: (Item list -> Async<StockResult>)` |

### Combinators: Mixed vs Separated

John's `IOrderAlgebra<TResult>` **mixes** domain instructions (`CheckStock`, `CalculatePrice`) with combinators (`Then`, `Done`, `Guard`) in a single interface. This gives the algebra full control over composition — each interpreter can define its own sequencing semantics.

The gitbook's approach **separates** them: domain operations live in the instruction interface (`IOrderInstructions`), while combinators (`bind`, `map`, `map2`) live in the `Program` module and CE builder. This yields a reusable CE that works with any instruction set, but fixes the monadic structure.

| Approach                | Pros                                                             | Cons                                                                                                 |
| ----------------------- | ---------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| **Mixed** (John)        | Full control per interpreter; can optimize sequencing            | Logic must be re-implemented per interpreter (F#); workflow shape coupled to algebra                 |
| **Separated** (Gitbook) | One CE for all domains; workflow syntax is fixed and predictable | Less flexible; monadic structure is fixed (always `Async` in V4, always free monad tree in V3/V3bis) |

### Parallelism Approaches

Three different approaches to expressing and executing parallel instructions:

| Approach          | Pattern                  | How it works                                 | Who decides?                           |
| ----------------- | ------------------------ | -------------------------------------------- | -------------------------------------- |
| John C# `Both<T>` | Free Monad (AST)         | `Parallel()` creates `Both` nodes in the AST | Interpreter (sequential or concurrent) |
| V3bis `Parallel`  | Free Monad (AST)         | `map2` creates `Parallel` nodes in the AST   | Interpreter (`Async.StartChild`)       |
| V4 `map2`         | Tagless Final (function) | `map2` directly starts child async           | Built into the combinator              |

The Free Monad approaches (John C# and V3bis) encode parallelism as **data** — the AST declares the intent, and the interpreter chooses the strategy. The Tagless Final approach (V4) encodes parallelism as **behavior** — `map2` directly calls `Async.StartChild`, so parallelism is baked into the combinator.

### Turnkey vs Generic

The gitbook's implementations (V3, V3bis, V4) are **turnkey**: the `Program` type and CE are pre-wired with `Result`, `Async`, and the domain `Error` type. The CE automatically handles error short-circuiting (binding `Result` values), async threading, and error type lifting. This is convenient for the nominal use case — a domain workflow that performs async I/O and may fail — but it constrains the design. You cannot easily produce a `string` (narrative) or an `AuditEntry list` (dry-run) from the same workflow.

John's implementations are **generic**: the algebra and program types are independent of `Result`, `Async`, and any specific error type. `IOrderAlgebra<TResult>` can be instantiated with any `TResult` — making test, narrative, dry-run, and async interpreters all possible from the same workflow definition. The trade-off is more wiring for the nominal use case: the caller must handle async, errors, and composition explicitly.

| Approach              | Convenience                          | Flexibility                                 | Interpretation modes                                         |
| --------------------- | ------------------------------------ | ------------------------------------------- | ------------------------------------------------------------ |
| **Turnkey** (Gitbook) | High — CE handles Result/Async/Error | Low — fixed to `Async<Result<'ret, Error>>` | Nominal execution + tests (mock interface)                   |
| **Generic** (John)    | Lower — manual wiring                | High — `TResult` can be anything            | Nominal, narrative, dry-run, audit, structural analysis, ... |

## Choosing a Pattern: Tagless Final, Free Monad, or Both?

In his article [Choosing Both Sides of the Coin](https://johnazariah.github.io/2026/03/19/choosing-both-sides-of-the-coin.html), John suggests **starting with Tagless Final**. The pattern is simpler to adopt: programs are written against a familiar algebra (an interface in C#, a record in F#), composition uses standard language features (LINQ, CEs), and the approach fits naturally with dependency injection — idiomatic in OO languages like C#.

When inspectability becomes necessary — structural analysis, dry-run visualization, pre-execution optimization — the program can be **converted to a Free Monad AST** via a dedicated interpreter (`ToFreeMonadInterpreter`). This interpreter implements the algebra but, instead of executing operations, produces AST nodes. The resulting data structure can then be walked, analyzed, or optimized by a second-pass interpreter.

The key insight: **you don't have to choose upfront**. Start with the ergonomics of Tagless Final, and add the inspectability of a Free Monad only when and where you need it — as just another interpreter.

## Conclusion

The fundamental trade-off: **initial encoding** (Free Monad) gives inspectability and pre-execution optimization; **final encoding** (Tagless Final) gives simplicity and ergonomics. Neither is universally superior.

V3bis demonstrates a **middle ground** for the Free Monad: by adopting `obj` boxing (from John's approach) and adding a `Parallel` case, it recovers both inspectability and parallel execution while keeping fully typed instructions — something that was impossible with V3's functor-based design.

The gitbook's V4 takes a **turnkey Tagless Final** path: a domain-agnostic `Program` type and CE, parameterized by an instruction interface, with built-in `Result`/`Async` handling and parallel execution via `let! ... and! ...` — optimized for the nominal use case at the cost of interpretation flexibility.

John's hybrid approach shows that this is not necessarily an either/or choice: write Tagless Final programs for ergonomics, then convert to a Free Monad AST when you need structural analysis. The design space is rich — each choice reflects a different balance of type safety, flexibility, simplicity, and inspectability.

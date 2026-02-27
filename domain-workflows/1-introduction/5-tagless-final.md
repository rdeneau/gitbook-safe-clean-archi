---
icon: tag
---

# Tagless Final

The **Tagless Final** pattern (sometimes called *Finally Tagless*) is a well-known approach in the functional programming community for defining domain-specific languages (DSLs). It replaces the free monad's data structure encoding with a direct encoding using interfaces (type classes in Haskell). This is version 4 of our `program`, and it represents a radical simplification over V3.

## The Core Idea

In the **initial** (or *tagged*) encoding (V2 and V3), we represent programs as **data structures** — discriminated unions, instruction classes — that are later interpreted. In the **final** (or *tagless*) encoding, we represent programs as **functions** parameterized by an interface (the "algebra") that describes the available operations.

| Aspect                | Initial Encoding (V2/V3)             | Final Encoding (V4)                           |
| --------------------- | ------------------------------------ | --------------------------------------------- |
| **Program type**      | ADT: `Stop \| Effect of ...`         | Function: `'ins -> Async<'ret>`               |
| **Instructions**      | Union cases + Effect classes         | Interface methods                             |
| **Interpretation**    | Recursive pattern match              | Direct invocation of interface implementation |
| **Adding operations** | Add union case, effect class, helper | Add interface method                          |
| **Type parameters**   | `Program<'ret>`                      | `Program<'ins, 'ret>`                         |

In Haskell, this is achieved with type classes; in F#, we use **interfaces** — which serve the same role of abstracting the algebra of operations.

## Why Move from V3 to V4?

### The Variance Problem

V3's `IProgramEffect<'a>` interface required a `Map` method returning `IProgramEffect<'b>`:

```fsharp
// V3 — problematic
[<Interface>]
type IProgramEffect<'a> =
    abstract member Map: f: ('a -> 'b) -> IProgramEffect<'b>
```

This design made the type a functor — sufficient for sequential `let! ... let!` composition. However, to support **parallel execution** via the applicative syntax `let! ... and! ...`, we need a `map2` function:

```fsharp
// Would need something like:
let map2 f (effA: IProgramEffect<'a>) (effB: IProgramEffect<'b>) : IProgramEffect<'c> = ...
```

This is effectively impossible in F# because:

1. The `Map` method's return type `IProgramEffect<'b>` is covariant in `'b`, but the effect implementations wrapping domain-specific instruction types are invariant.
2. F#'s .NET generics enforce **strict variance checking** — unlike Haskell's type classes, which are structurally flexible.
3. Implementing `MergeSources` (the CE method backing `and!`) on `Program<'ret> = Stop of 'ret | Effect of IProgramEffect<Program<'ret>>` would require composing two effects that know nothing about each other.

This made parallel instruction execution essentially impossible with the V3 architecture.

### The Boilerplate Problem

V3 required a 5-step recipe per instruction:

1. Type alias (`GetPricesQuery<'a>`)
2. Union case (`GetPrices of GetPricesQuery<'a>`)
3. Effect interface (`IProductEffect<'a>`)
4. Effect class (4 lines each: `GetPricesEffect<'a>`)
5. Helper function (`let getPrices = Program.effect GetPricesEffect GetPricesQuery`)

For 5 instructions, that's ~50 lines of boilerplate before writing any workflow logic.

## V4: The Tagless Final Solution

### Program = Async Reader

The entire `Program` type collapses to a single type alias:

```fsharp
type Program<'ins, 'ret when 'ins :> IProgramInstructions> = 'ins -> Async<'ret>
```

A program is simply a **function** that:

- Takes `'ins` — an instruction set (an interface inheriting `IProgramInstructions`)
- Returns `Async<'ret>` — an asynchronous result

This is the **ReaderT monad** pattern: the "environment" being read is the set of instructions.

### Instructions = Interface Methods

Each domain defines its instructions as a plain interface:

```fsharp
[<Interface>]
type IProductInstructions =
    inherit IProgramInstructions
    abstract member GetPrices: (SKU -> Async<Prices option>)
    abstract member SavePrices: (Prices -> Async<Result<PreviousValue<Prices>, Error>>)
    // ...
```

{% hint style="info" %}
Each member's type is a function `'arg -> Async<'ret>`, making each member essentially a single instruction. This is the Tagless Final algebra — no union type, no effect class, just interface methods.
{% endhint %}

### Defining Programs from Instructions

A helper type makes defining programs from instructions ergonomic:

```fsharp
type private DefineProgram = DefineProgram<IProductInstructions>

let getPrices sku = DefineProgram.instruction _.GetPrices(sku)
let savePrices prices = DefineProgram.instruction _.SavePrices(prices)
```

Under the hood, `DefineProgram.instruction` is just the identity function — it's a **DevExp** aid that triggers IntelliSense on the instruction interface, using F#'s shorthand lambda syntax (`_.Method(args)`).

## Benefits

### Simplicity

- **1 type alias** instead of the free monad ADT
- **1 interface** instead of union + effect classes + aliases
- **1 line per instruction** in the helper module
- No interpreter loop — the CE builder directly composes async functions

### Parallel Execution

Since `Program<'ins, 'ret>` is just `'ins -> Async<'ret>`, implementing `map2` is trivial:

```fsharp
let map2 (f: 'a -> 'b -> 'c) (progA: Program<'ins, 'a>) (progB: Program<'ins, 'b>) : Program<'ins, 'c> =
    fun ins ->
        async {
            let! childTaskA = Async.StartChild(progA ins)
            let! b = progB ins
            let! a = childTaskA
            return f a b
        }
```

This enables the applicative `let! ... and! ...` syntax in the `program` CE:

```fsharp
program {
    let! resA = Program.addProduct product
    and! resB = Program.addPrices (Prices.Initial(sku, currency))
    return Result.zip resA resB |> Result.ignore
}
```

Both instructions run concurrently — something impossible in V3.

### Extensibility

The reader-based design naturally supports:

- **Undo / Saga pattern:** The instruction preparer wraps each instruction with tracking and undo capabilities, without modifying the program type.
- **Observability:** Logging, metrics, and timing are injected at the instruction preparation level.
- **Testing:** Mock the instruction interface — no interpreter to stub.

## Complementary Resource

- Series [Tagless Final in F#](https://johnazariah.github.io/2025/12/12/tagless-final-01-froggy-tree-house.html) by John Azariah 🐸

## What's Next

The following sections detail the [Program](../2-program/) project implementation, then show how [domain workflows](../3-domain-workflow/) use this pattern in practice.

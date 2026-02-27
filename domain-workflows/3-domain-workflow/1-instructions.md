---
icon: rectangle-terminal
---

# Instructions

With V4's Tagless Final approach, defining instructions is dramatically simpler than V3's 5-step recipe. Each domain defines a **single interface** and a small **helper module**.

🔗 [Source code: Prelude.fs](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/Prelude.fs)

## 1. Define the instruction interface

```fsharp
[<Interface>]
type IProductInstructions =
    inherit IProgramInstructions

    // === Query Instructions ===
    abstract member GetPrices: (SKU -> Async<Prices option>)
    abstract member GetSales: (SKU -> Async<Sale list option>)
    abstract member GetStockEvents: (SKU -> Async<StockEvent list option>)

    // === Command Instructions ===
    abstract member SavePrices: (Prices -> Async<Result<PreviousValue<Prices>, Error>>)
    abstract member SaveProduct: (Product -> Async<Result<PreviousValue<Product>, Error>>)
    abstract member AddPrices: (Prices -> Async<Result<unit, Error>>)
    abstract member AddProduct: (Product -> Async<Result<unit, Error>>)
```

This single interface replaces the entire V3 machinery: type aliases, union type, effect interface, effect classes.

**Conventions:**

- The interface inherits `IProgramInstructions`, the marker interface from `Shopfoo.Program`.
- Each member is a **function type** `'arg -> Async<'ret>` wrapped in parentheses (making it a property returning a function, which enables partial application and assignment via `member val`).
- **Queries** return `'ret option` (wrapped in `Async`), where `None` means not found.
- **Commands** return `Result<'ret, Error>` (wrapped in `Async`). Commands that modify data return a `PreviousValue<'a>` to support undo (reverting to the prior state). Commands that create data return `unit`.

{% hint style="info" %}
Naming convention: `I{Domain}Instructions`. The interface serves as the Tagless Final **algebra** — the complete set of operations available to workflows in this domain.
{% endhint %}

{% hint style="success" %}
The implementation of these instructions — wiring each one to its data-layer call and undo strategy — is covered in the [Api](4-api.md) page under [Instruction Wiring with Undo Support](4-api.md#instruction-wiring-with-undo-support).
{% endhint %}

## 2. Define helper functions

```fsharp
[<AutoOpen>]
module internal Internals =
    [<RequireQualifiedAccess>]
    module Program =
        type private DefineProgram = DefineProgram<IProductInstructions>

        let getPrices sku       = DefineProgram.instruction _.GetPrices(sku)
        let getSales sku        = DefineProgram.instruction _.GetSales(sku)
        let getStockEvents sku  = DefineProgram.instruction _.GetStockEvents(sku)
        let savePrices prices   = DefineProgram.instruction _.SavePrices(prices)
        let saveProduct product = DefineProgram.instruction _.SaveProduct(product)
        let addPrices prices    = DefineProgram.instruction _.AddPrices(prices)
        let addProduct product  = DefineProgram.instruction _.AddProduct(product)
```

Each helper function wraps one instruction into a `Program<IProductInstructions, 'ret>`. The `DefineProgram.instruction` call is an identity function that triggers IntelliSense on the instruction interface via the shorthand lambda `_.Method(args)`.

**Key points:**

- The private type alias `DefineProgram = DefineProgram<IProductInstructions>` fixes the instruction set once.
- Each helper is a one-liner, following the pattern `let {name} {args} = DefineProgram.instruction _.{Name}({args})`. The parentheses after the method name are **required**: the `_.Method(args)` shorthand is syntactic sugar for a lambda calling an interface member, and F# only recognizes it as such when the member invocation includes an explicit argument list — even for a one-parameter member that we can write `x.Method arg` usually.
- The `Internals` module is `[<AutoOpen>]` and `internal` — it is automatically opened but only within the `Shopfoo.Product` project. The nested `Program` module carries `[<RequireQualifiedAccess>]`, so callers must write `Program.getPrices`, `Program.savePrices`, etc. The qualifier makes instruction calls immediately identifiable in workflow code and enables precise IDE auto-completion.

## 3. Define the workflow interface alias

```fsharp
[<Interface>]
type IProductWorkflow<'arg, 'ret> =
    inherit IProgramWorkflow<IProductInstructions, 'arg, 'ret>
```

This optional but convenient alias fixes the instruction set type parameter. Each workflow class implements `IProductWorkflow<'arg, 'ret>` instead of the longer `IProgramWorkflow<IProductInstructions, 'arg, 'ret>`.

## Comparison with V3

| Aspect                       | V3 (5-step recipe)                                            | V4 (Tagless Final)            |
| ---------------------------- | ------------------------------------------------------------- | ----------------------------- |
| **Steps per instruction**    | 5 (alias, union case, effect interface, effect class, helper) | 2 (interface member + helper) |
| **Lines per instruction**    | ~10                                                           | 2                             |
| **Union type**               | Required for exhaustive matching                              | Not needed                    |
| **Effect classes**           | One per instruction (4 lines each)                            | Not needed                    |
| **Functor Map**              | Must be implemented per effect class                          | Not needed                    |
| **Total for 7 instructions** | ~70 lines                                                     | ~15 lines                     |

The drastic reduction in boilerplate is the most visible benefit of V4. But the more important gain is **conceptual simplicity**: instructions are just interface methods, programs are just functions consuming those methods.

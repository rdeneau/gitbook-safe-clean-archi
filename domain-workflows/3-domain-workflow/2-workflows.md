---
icon: arrow-progress
---

# Workflows

## Domain Types

`Types.fs` defines two types:

* `ProductDomain`: A single-case union implementing the `IDomain` interface. This marker type identifies the domain and distinguishes it from other domains in the solution.
* `ProductWorkflow`: The base class for workflows in the _Product_ domain. This design choice prioritizes convenience of use. The code is straightforward enough to justify this exception to the inheritance avoidance rule.

```fsharp
namespace Shopfoo.Product.Workflows

open Shopfoo.Domain.Types.Errors
open Shopfoo.Effects

type ProductDomain =
    | ProductDomain

    interface IDomain with
        member _.Name = "Product"

[<AbstractClass>]
type ProductWorkflow<'arg, 'ret>() =
    abstract member Run: 'arg -> Program<Result<'ret, Error>>

    interface IDomainWorkflow<ProductDomain> with
        member val Domain = ProductDomain

    interface IProgramWorkflow<'arg, 'ret> with
        member this.Run arg = this.Run arg
```

## Domain Workflow Design Choice

Which features warrant a workflow implementation? Two approaches lead to different designs.

### Favor Simplicity

_This is the approach chosen in the Shopfoo solution._

Evaluate each feature to determine whether it would benefit from workflow implementation.

Generally, commands are most suitable candidates. They typically contain business complexity and/or orchestrate multiple Data layer calls. In contrast, queries usually lack sufficient complexity and can be delegated directly to the Data layer.

However, exceptions exist, as seen in [Api.fs](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Api.fs#L43-L56):

* The `AdjustStock` command is delegated directly to the _Warehouse_ access client.
* The `DetermineStock` query is implemented as a workflow.

### Favor Domain Expressiveness in File Structure

This design mandates that each feature has its own workflow, making them visible in the file tree within the `Workflows` folder. This results in numerous pass-through workflows that simply invoke a single instruction—typically used only once (not shared across workflows)—which connects to the Data layer during program interpretation.

In my opinion, this violates the [KISS principle](https://en.wikipedia.org/wiki/KISS_principle) and leads to over-engineering. Features remain easily accessible through the [API contract](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Api.fs#L14-L28):

```fsharp
[<Interface>]
type IProductApi =
    abstract member GetProducts: (unit -> Async<Product list>)
    abstract member GetProduct: (SKU -> Async<Product option>)
    abstract member SaveProduct: (Product -> Async<Result<unit, Error>>)
    // ...
```

## Domain Workflow Examples

Let's examine characteristic workflows in order of increasing complexity.

### RemoveListPrice

This feature requires a workflow to orchestrate multiple instructions: `getPrices` and `savePrices`. It's one of the simplest workflows.

🔗 [Code source](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/RemoveListPrice.fs)

```fsharp
[<Sealed>]
type internal RemoveListPriceWorkflow private () =
    inherit ProductWorkflow<SKU, unit>()

    override _.Run sku =
        program {
            let! prices = Program.getPrices sku |> Program.requireSome $"SKU #%s{sku.Value}" |> Program.mapDataRelatedError
            do! Program.savePrices { prices with ListPrice = None }
            return Ok()
        }

    static member val Instance = RemoveListPriceWorkflow()
```

The `RemoveListPriceWorkflow` class, like all workflow classes, explicitly implements the _Singleton_ pattern without relying on the IoC container. Indeed, the `Api` class that we'll see later is the only place in production code where workflow instances are used.

As a reminder, the `Run` method has the signature `'arg -> Program<Result<'ret, Error>>`, coming from the `IProgramWorkflow<'arg, 'ret>` interface. However, the `getPrices` instruction returns a `Program<Prices option>`. Therefore, it must be adapted to the type expected as the return of `Run`. For this, we successively use two helpers from the `Program` module:

* First `requireSome` ([source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Program.fs#L113-L114)) which converts an `Option<'a>` to a `Result<'a, DataRelatedError>`
* Then `mapDataRelatedError` ([source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Program.fs#L107-L107)) which transforms a `Result<'a, DataRelatedError>` into the expected `Result<'a, Error>`

The `savePrices` instruction already has the correct return type, so no adaptation is needed.

{% hint style="warning" %}
These adaptations are among the most delicate aspects when writing a `program`. When forgotten, the compilation error appears after the "faulty" line and indicates that no overload can be found for the `Bind` method. This cryptic error message, located in the wrong place, doesn't help understand how to fix the problem. If you don't remember the need to adapt the return type, you can always annotate the values with the expected types. The error is then located in the right place and its message is more precise, which helps somewhat, though it still requires careful analysis to properly understand and resolve the issue.
{% endhint %}

### SavePrices

This feature requires a workflow to handle validation:

* If `ListPrice` is defined, it must be positive.
* If `RetailPrice` is of type `Regular` (not `SoldOut`), it must be positive as well.

🔗 [Source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/SavePrices.fs):

```fsharp
[<Sealed>]
type internal SavePricesWorkflow private () =
    inherit ProductWorkflow<Prices, unit>()

    let guardListPrice (prices: Prices) =
        match prices.ListPrice with
        | Some price -> Guard(nameof prices.ListPrice).IsPositive(price.Value)
        | None -> Ok 0m

    let guardRetailPrice (prices: Prices) =
        match prices.RetailPrice with
        | RetailPrice.Regular price -> Guard(nameof prices.RetailPrice).IsPositive(price.Value)
        | RetailPrice.SoldOut -> Ok 0m

    let validate (prices: Prices) =
        validation {
            let! _ = guardListPrice(prices).ToValidation()
            and! _ = guardRetailPrice(prices).ToValidation()
            return ()
        }

    override _.Run prices =
        program {
            do! validate prices |> liftGuardClauses
            do! Program.savePrices prices
            return Ok()
        }

    static member val Instance = SavePricesWorkflow()
```

In the Shopfoo codebase, validation occurs in two stages: guard clauses returning `Result<'a, GuardClauseError>` are transformed into `Validation<'a, GuardClauseError>` (an alias for `Result<'a, GuardClauseError list>`). These guard clauses are then aggregated using the `validation` computation expression with the `let! ... and! ...` syntax, revealing applicative behavior.

The result of `validate prices` needs to be adapted regarding the error track. We use the `liftGuardClauses` ([source code](https://github.com/rdeneau/shopfoo/blob/96d8eb77072ec60ab2989fec96a2fa86b1867b34/src/Shopfoo.Domain.Types/Errors.fs#L158-L159)) to obtain the required `Result<unit, Error>` type.

Then, the workflow uses the fact that the `program` CE provides two overloads for the `Bind` method to handle the `Result` type ([source code](https://github.com/rdeneau/shopfoo/blob/96d8eb77072ec60ab2989fec96a2fa86b1867b34/src/Shopfoo.Effects/Program.fs#L78-L80)):

```fsharp
[<AutoOpen>]
module ProgramBuilder =
    // [...]
    /// Bind operator
    let private (>>=) program f = bind f program

    let private bindResult (f: 'v -> Program<_>) (result: Result<'v, _>) =
        match result with
        | Ok v -> f v
        | Error e -> Stop(Error e)

    type ProgramBuilder() =
        // [...]
        member _.Bind(program: Program<_>, f) = program >>= f
        member _.Bind(program: Program<Result<_, _>>, f) = program >>= (bindResult f)
        member _.Bind(result: Result<_, _>, f) = result |> bindResult f
```

* The regular `Bind` uses the `>>=` bind operator directly.
* The two other overloads rely on the `bindResult` function that operates on a `Result` but returns it wrapped in a `Program`.
* The first one `Bind(result: Result<_, _>, f)` supports binding a `Result` directly and elevating it to a `Program`. This is the one used in this workflow to bind `validate prices |> liftGuardClauses`.
* The second one `Bind(program: Program<Result<_, _>>, f)` supports binding a `Program` containing a `Result`. In practice, it is this `Bind` that is most commonly used in workflows.

This design simplifies `program` composition, as error track management is already sufficiently delicate on its own.

### DetermineStock

This query feature benefits from workflow implementation. It handles both orchestration of multiple instructions—`getSales` and `getStockEvents`—and a business rule to determine current stock based on stock events and sales, similar to event sourcing.

🔗 [Source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/DetermineStock.fs)

```fsharp
[<Sealed>]
type internal DetermineStockWorkflow private () =
    inherit ProductWorkflow<SKU, Stock>()

    override _.Run sku =
        program {
            let! (sales: Sale list) =
                Program.getSales sku // ↩
                |> Program.requireSome $"SKU #%s{sku.Value}"
                |> Program.mapDataRelatedError

            let! stockEvents =
                Program.getStockEvents sku
                |> Program.requireSome $"SKU #%s{sku.Value}"
                |> Program.mapDataRelatedError

            let allEvents =
                [
                    for sale in sales do
                        StockEventType.Shipped, sale.Date, sale.Quantity

                    for stockEvent in stockEvents do
                        match stockEvent.Type with
                        | ProductSupplyReceived _ -> StockEventType.SupplyReceived, stockEvent.Date, stockEvent.Quantity
                        | StockAdjusted -> StockEventType.StockAdjusted, stockEvent.Date, stockEvent.Quantity
                ]
                |> List.sortBy (fun (_, date, _) -> date)

            let quantity =
                (0, allEvents)
                ||> Seq.fold (fun acc (eventType, _, quantity) ->
                    match eventType with
                    | StockEventType.Shipped -> acc - quantity
                    | StockEventType.SupplyReceived -> acc + quantity
                    | StockEventType.StockAdjusted -> quantity
                )

            return Ok { SKU = sku; Quantity = quantity }
        }

    static member val Instance = DetermineStockWorkflow()
```

{% hint style="info" %}
**💡 Formatting Tip**

Throughout the codebase, you'll occasionally find `// ↩` comments, like the one after `Program.getSales sku` here. These ensure consistent automatic formatting by Fantomas. Without it, the expression `let! (sales: Sale list) = ...` would be formatted on a single line (like with `let! prices` in `RemoveListPriceWorkflow`), while `let! stockEvents = ...` spans 4 lines, creating asymmetry that hinders code readability.

This represents a compromise allowing reasonably long lines (up to 150 characters, see [.editorconfig](https://github.com/rdeneau/shopfoo/blob/main/.editorconfig#L14-L19)) while locally overriding formatting rules via these `// ↩` comments.
{% endhint %}

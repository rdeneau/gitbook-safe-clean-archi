---
icon: arrow-progress
---

# Domain Workflow

This page demonstrates how to write domain workflows using the V3 program framework described in the [Effectful Program](effectful-program "mention") page.

## Shopfoo Code

Here's a simplified view of the solution structure, focused on the [`Shopfoo.Product` project](https://github.com/rdeneau/shopfoo/tree/main/src/Shopfoo.Product), which implements domain workflows:

```txt
📂 src/
├──📂 Core/
│  ├──🗃️ Shopfoo.Common
│  ├──🗃️ Shopfoo.Domain.Types
│  └──🗃️ Shopfoo.Effects
├──📂 Feat/
│  ├──🗃️ Shopfoo.Home
│  └──🗃️ Shopfoo.Product 👈👈
│     ├──📂 Workflows/
│     │  ├──📄 Types.fs
│     │  ├──📄 Instructions.fs
│     │  ├──📄 AdjustStock.fs
│     │  ├──📄 MarkAsSoldOut.fs
│     │  └──📄 ...
│     ├──📂 Data/
│     └──📄 Api.fs
└──📂 UI/
   ├──🗃️ Shopfoo.Client
   ├──🗃️ Shopfoo.Server
   └──🗃️ Shopfoo.Shared
```

## Domain Types

`Types.fs` defines two types:

- `ProductDomain`: A single-case union implementing the `IDomain` interface. This marker type identifies the domain and distinguishes it from other domains in the solution.
- `ProductWorkflow`: The base class for workflows in the *Product* domain. This design choice prioritizes convenience of use. The code is straightforward enough to justify this exception to the inheritance avoidance rule.

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

## Domain Instructions

Each domain defines its instructions following a 5-step pattern (maintaining top-down declaration order).

While not all steps are strictly mandatory, they are highly recommended as they make the code more concise and readable.

All steps should follow a naming convention to organize the code effectively. You may adopt the convention presented here or devise your own that better suits your needs—feel free to share alternatives in the comments.

### 1. Define all query and command type aliases

```fsharp
type GetPricesQuery<'a> = Query<SKU, Prices, 'a>
type GetSalesQuery<'a> = Query<SKU, Sale list, 'a>
type GetStockEventsQuery<'a> = Query<SKU, StockEvent list, 'a>
type SavePricesCommand<'a> = Command<Prices, 'a>
type SaveProductCommand<'a> = Command<Product, 'a>
```

These aliases are convenient for steps 2, 4, and 5. They follow this naming convention:

- A *Query* alias ends with `Query`, such as `GetPricesQuery`.
- A *Command* alias ends with `Command`, such as `SavePricesCommand`.

All that remains is to specify the generic type parameters. As a reminder, `Query` and `Command` are aliases of the `Instruction` class that fix the return type:

- A `Command` returns a `Result<unit, Error>`. Only the input type parameter needs to be specified, for example `Prices` for `SavePricesCommand`.
- A `Query` returns a `'ret option`. It therefore requires two type parameters: input and output, for example `SKU` and `Prices` respectively for `GetPricesQuery`.

### 2. Define the union type gathering all instructions

```fsharp
type ProductInstruction<'a> =
    | GetPrices of GetPricesQuery<'a>
    | GetSales of GetSalesQuery<'a>
    | GetStockEvents of GetStockEventsQuery<'a>
    | SavePrices of SavePricesCommand<'a>
    | SaveProduct of SaveProductCommand<'a>
```

This union type is only used in step 3, but it is essential for recovering exhaustiveness in the interpretation of domain effects.

The naming convention applied here:

- The union name follows the format `{Domain}Instruction`.
- The union cases use instruction names as-is, without prefix or suffix.

### 3. Define the effect interface for this union

```fsharp
[<Interface>]
type IProductEffect<'a> =
    inherit IProgramEffect<'a>
    inherit IInterpretableEffect<ProductInstruction<'a>>
```

This interface is convenient for step 4. It brings together the two interfaces defining an effect, with the second fixing the domain: `IInterpretableEffect<ProductInstruction<'a>>`. It follows the naming convention `I{Domain}Effect`.

### 4. Define the effect class corresponding to each instruction

```fsharp
type GetPricesEffect<'a>(query: GetPricesQuery<'a>) =
    interface IProductEffect<'a> with
        override _.Map(f) = GetPricesEffect(query.Map f)
        override val Instruction = GetPrices query

type GetSalesEffect<'a>(query: GetSalesQuery<'a>) =
    interface IProductEffect<'a> with
        override _.Map(f) = GetSalesEffect(query.Map f)
        override val Instruction = GetSales query

type GetStockEventsEffect<'a>(query: GetStockEventsQuery<'a>) =
    interface IProductEffect<'a> with
        override _.Map(f) = GetStockEventsEffect(query.Map f)
        override val Instruction = GetStockEvents query

type SavePricesEffect<'a>(command: SavePricesCommand<'a>) =
    interface IProductEffect<'a> with
        override _.Map(f) = SavePricesEffect(command.Map f)
        override val Instruction = SavePrices command

type SaveProductEffect<'a>(command: SaveProductCommand<'a>) =
    interface IProductEffect<'a> with
        override _.Map(f) = SaveProductEffect(command.Map f)
        override val Instruction = SaveProduct command
```

This is the most verbose step, requiring four lines per instruction to write the class defining the effect containing the instruction.

We could seal the classes with the `[<Sealed>]` attribute to strengthen type safety, but this would be even more verbose, and it's actually safe to omit it since the classes are only used in step 5 and don't appear elsewhere in the codebase.

Each class simply implements the interface defined in step 3:

- The `Map` method comes from the `IProgramEffect<'a>` interface and must follow the signature `Map: f: ('a -> 'b) -> IProgramEffect<'b>`. As mentioned previously, this signature is not precise enough to correspond to a functor. In fact, it must return the current type implementing the interface, for example `GetPricesEffect`. The mapping occurs on the object's content, namely the instruction. Since the `Instruction` class is equipped with a `Map` method, we simply delegate the mapping to it by transferring the "mapper"—the function `f` as input parameter.
- The `Instruction` property comes from the `IInterpretableEffect<ProductInstruction<'a>>` interface. It returns the union case corresponding to the instruction. For example, for `GetPricesEffect`, it's the `GetPrices of GetPricesQuery<'a>` case, where the `GetPricesQuery<'a>` corresponds to the class's input parameter, here named `query` since it's a *Query*.

The following naming convention can serve as a template:

```fsharp
type {Instruction}Effect<'a>({instructionType}: {Instruction}{InstructionType}<'a>) =
    interface I{Domain}Effect<'a> with
        override _.Map(f) = {Instruction}Effect({instructionType}.Map f)
        override val Instruction = {Instruction} {instructionType}
```

### 5. Define helper functions for each effect

```fsharp
[<RequireQualifiedAccess>]
module Program =
    let getPrices = Program.effect GetPricesEffect GetPricesQuery
    let getSales = Program.effect GetSalesEffect GetSalesQuery
    let getStockEvents = Program.effect GetStockEventsEffect GetStockEventsQuery
    let savePrices = Program.effect SavePricesEffect SavePricesCommand
    let saveProduct = Program.effect SaveProductEffect SaveProductCommand
```

This final step defines the helper functions that will be used to write the `program` for workflows. It relies on a lower-level helper—`Program.effect`—whose [source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Program.fs#L85-L89) is:

```fsharp
module Program =
    let inline effect (buildEffect: _ -> 'eff) (buildInstruction: _ -> Instruction<'arg, 'ret, _>) (args: 'arg) =
        let instructionName = typeof<'eff>.Name.Replace("Effect`1", "")
        Effect(buildEffect (buildInstruction (instructionName, args, Stop)))
```

This helper simplifies code that would otherwise occupy several lines per instruction. It takes the effect, the instruction, and the instruction's argument as input. The effect type allows deducing the instruction name. Finally, the helper assembles all the pieces to construct the mini `Program` that only runs the given instruction and terminates, as indicated by the use of the final `Stop`.

In usage, we don't need to specify the argument thanks to partial application of the first two of three parameters. The template defining the naming convention is `let {instruction} = Program.effect {Instruction}Effect {Instruction}{InstructionType}`.

### Remark

While instruction declaration involves multiple steps, it's mostly boilerplate. You can work bottom-up (fixing compiler errors as you proceed) or top-down (leveraging AI code generation tools).

## Domain Workflow Design Choice

Which features warrant a workflow implementation? Two approaches lead to different designs.

### Favor Simplicity

*This is the approach chosen in the Shopfoo solution.*

Evaluate each feature to determine whether it would benefit from workflow implementation.

Generally, commands are most suitable candidates. They typically contain business complexity and/or orchestrate multiple Data layer calls. In contrast, queries usually lack sufficient complexity and can be delegated directly to the Data layer.

However, exceptions exist, as seen in [Api.fs](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Api.fs#L43-L56):

- The `AdjustStock` command is delegated directly to the *Warehouse* access client.
- The `DetermineStock` query is implemented as a workflow.

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

The `RemoveListPriceWorkflow` class, like all workflow classes, explicitly implements the *Singleton* pattern without relying on the IoC container. Indeed, the `Api` class that we'll see later is the only place in production code where workflow instances are used.

As a reminder, the `Run` method has the signature `'arg -> Program<Result<'ret, Error>>`, coming from the `IProgramWorkflow<'arg, 'ret>` interface. However, the `getPrices` instruction returns a `Program<Prices option>`. Therefore, it must be adapted to the type expected as the return of `Run`. For this, we successively use two helpers from the `Program` module:

- First `requireSome` ([source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Program.fs#L113-L114)) which converts an `Option<'a>` to a `Result<'a, DataRelatedError>`
- Then `mapDataRelatedError` ([source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Program.fs#L107-L107)) which transforms a `Result<'a, DataRelatedError>` into the expected `Result<'a, Error>`

The `savePrices` instruction already has the correct return type, so no adaptation is needed.

{% hint style="warning" %}
These adaptations are among the most delicate aspects when writing a `program`. When forgotten, the compilation error appears after the "faulty" line and indicates that no overload can be found for the `Bind` method. This cryptic error message, located in the wrong place, doesn't help understand how to fix the problem. If you don't remember the need to adapt the return type, you can always annotate the values with the expected types. The error is then located in the right place and its message is more precise, which helps somewhat, though it still requires careful analysis to properly understand and resolve the issue.
{% endhint %}

### SavePrices

This feature requires a workflow to handle validation:

- If `ListPrice` is defined, it must be positive.
- If `RetailPrice` is of type `Regular` (not `SoldOut`), it must be positive as well.

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

- The regular `Bind` uses the `>>=` bind operator directly.
- The two other overloads rely on the `bindResult` function that operates on a `Result` but returns it wrapped in a `Program`.
- The first one `Bind(result: Result<_, _>, f)` supports binding a `Result` directly and elevating it to a `Program`. This is the one used in this workflow to bind `validate prices |> liftGuardClauses`.
- The second one `Bind(program: Program<Result<_, _>>, f)` supports binding a `Program` containing a `Result`. In practice, it is this `Bind` that is most commonly used in workflows.

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
#### 💡 Formatting Tip

Throughout the codebase, you'll occasionally find `// ↩` comments, like the one after `Program.getSales sku` here. These ensure consistent automatic formatting by Fantomas. Without it, the expression `let! (sales: Sale list) = ...` would be formatted on a single line (like with `let! prices` in `RemoveListPriceWorkflow`), while `let! stockEvents = ...` spans 4 lines, creating asymmetry that hinders code readability.

This represents a compromise allowing reasonably long lines (up to 150 characters, see [.editorconfig](https://github.com/rdeneau/shopfoo/blob/main/.editorconfig#L14-L19)) while locally overriding formatting rules via these `// ↩` comments.
{% endhint %}

## API

The `Api` class serves as the actual domain entry point, residing in the *Application* layer in *Clean Architecture* terminology.

```fsharp
type internal Api(interpreterFactory: IInterpreterFactory) =
    let interpret = interpreterFactory.Create(ProductDomain)
    // ...
```

This internal class is accessible to the `Shopfoo.Server` project via dependency injection in conjunction with the `IProductApi` interface, which defines the API contract.

The class depends on `IInterpreterFactory`, which creates the interpreter for the current domain: `ProductDomain`. The interpreter is named `interpret` so method calls read like English: `interpret.Command`, `interpret.Workflow`.

```fsharp
    // ...
    let runEffect (productEffect: IProductEffect<_>) =
        match productEffect.Instruction with
        | GetPrices query -> interpret.Query(query, Prices.Client.getPrices)
        | GetSales query -> interpret.Query(query, Sales.Client.getSales)
        | GetStockEvents query -> interpret.Query(query, Warehouse.Client.getStockEvents)
        | SavePrices command -> interpret.Command(command, Prices.Client.savePrices)
        | SaveProduct command -> interpret.Command(command, Catalog.Client.saveProduct)

    let interpretWorkflow (workflow: ProductWorkflow<'arg, 'ret>) args =
        interpret.Workflow runEffect workflow args
    // ...
```

The interpreter enables definition of two key functions:

- `runEffect`:
  - Pattern matches on the union type defining domain instructions
  - Interpreting each instruction involves calling the method corresponding to the instruction type and passing the underlying Data layer function
- `interpretWorkflow`: Though it appears to be a simple pass-through, it subtly serves to:
  - Enforce accepted workflow types: only those from the current *Product* domain via the `ProductWorkflow` type
  - Name generic type parameters for convenience: `ProductWorkflow<'arg, 'ret>`
  - Apply the `runEffect` parameter to the `interpret.Workflow` method

```fsharp
    // ...
    interface IProductApi with
        member val GetProducts = Catalog.Client.getProducts // (1)
        member val GetProduct = Catalog.Client.getProduct // (1)
        member val SaveProduct = interpretWorkflow SaveProductWorkflow.Instance // (2)

        member val GetPrices = Prices.Client.getPrices // (1)
        member val SavePrices = interpretWorkflow SavePricesWorkflow.Instance // (2)
        member val MarkAsSoldOut = interpretWorkflow MarkAsSoldOutWorkflow.Instance // (2)
        member val RemoveListPrice = interpretWorkflow RemoveListPriceWorkflow.Instance // (2)

        member val AdjustStock = Warehouse.Client.adjustStock // (1)
        member val DetermineStock = interpretWorkflow DetermineStockWorkflow.Instance // (2)
        member val GetSales = Sales.Client.getSales // (1)
```

Finally, the class implements the API contract. Each endpoint is defined based on the underlying feature type:

1. Direct Data layer call
2. Workflow interpretation

## Domain Isolation

Three rules ensure proper domain isolation:

1. **No cross-domain workflow calls**: A workflow cannot invoke workflows from other domains.
2. **No cross-domain instruction calls**: A workflow cannot use instructions from other domains. When multiple domains need the same data source, declare separate instructions for each domain.
3. **Project structure enforcement**: Separate domain projects prevent cross-domain dependencies
   - Projects cannot reference each other
   - Compiler enforces isolation
   - Architecture tests complete the safety net

**Benefits:**

- Each domain is an independent F# project
- Compiler-enforced dependency boundaries
- **Screaming architecture** or **vertical slice architecture**
- Enhanced discoverability with one workflow per file

## Best Practices

### Naming Conventions

- **Domain types**: `{Domain}Domain` (e.g., `ProductDomain`)
- **Workflow classes**: `{Feature}Workflow` (e.g., `SaveProductWorkflow`)
- **Instructions**: `{Action}({Entity})` 
  - With explicit entity: `GetProducts` (entity: `Product`), `AdjustStock` (entity: `Stock`)
  - With implicit entity: `MarkAsSoldOut` (entity implied: `Prices`)

### File Organization

- General types definition in `Types.fs`
- Group instructions in `Instructions.fs`
- One workflow per file
- File name matches workflow name

### Type Safety

- Use type aliases for commands and queries
- Leverage smart constructors for domain types
- Use `Result` for commands, `option` for queries
- Make invalid states unrepresentable

## Conclusion

This page demonstrated practical domain workflow implementation using the V3 program framework.

**Key Takeaways:**

1. **Workflow Structure**: Each domain defines marker types (`ProductDomain`) and base workflow classes (`ProductWorkflow`) ensuring type safety, consistency, and convenient usage patterns.

2. **Instruction Patterns**: The systematic 5-step pattern for instruction declaration, while primarily boilerplate, ensures consistency across domains and maintains clear separation of concerns.

3. **Design Flexibility**: Choose between simplicity (workflows only for genuinely complex features) and file structure expressiveness (one workflow per feature) based on project requirements and team preferences.

4. **Practical Examples**: Real-world workflows demonstrate progression from simple orchestration (`RemoveListPrice`) through validation (`SavePrices`) to complex business logic (`DetermineStock`), illustrating the pattern's versatility.

5. **API Encapsulation**: The `Api` class abstracts implementation details—whether features use direct Data layer calls or workflow interpretation—maintaining clean architectural boundaries.

6. **Domain Isolation**: Compiler-enforced separation through project structure achieves screaming architecture while preventing cross-domain coupling, with architecture tests providing an additional safety net.

The Shopfoo repository provides working examples of these patterns in an almost production-ready codebase, demonstrating that the V3 program framework successfully balances functional purity with pragmatic application development.

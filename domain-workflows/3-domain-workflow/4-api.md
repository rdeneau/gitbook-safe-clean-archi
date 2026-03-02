---
icon: webhook
---

# Api

The `Api.fs` file defines the domain project entry point and corresponds to the _Application_ layer in _Clean Architecture_ terminology. It contains two main elements:

* The public interface `IProductApi`
* The internal class `Api`

## IProductApi Contract

`IProductApi` is a public interface defining the API contract of the domain project:

```fsharp
[<Interface>]
type IProductApi =
    abstract member GetProducts: (Provider -> Async<Product list>)
    abstract member GetProduct: (SKU -> Async<Product option>)
    abstract member AddProduct: (Product * Currency -> Async<Result<unit, Error>>)
    abstract member SaveProduct: (Product -> Async<Result<unit, Error>>)

    abstract member GetPrices: (SKU -> Async<Prices option>)
    abstract member SavePrices: (Prices -> Async<Result<unit, Error>>)
    abstract member MarkAsSoldOut: (SKU -> Async<Result<unit, Error>>)
    abstract member RemoveListPrice: (SKU -> Async<Result<unit, Error>>)

    abstract member AdjustStock: (Stock -> Async<Result<unit, Error>>)
    abstract member DetermineStock: (SKU -> Async<Result<Stock, Error>>)
    abstract member GetPurchasePrices: (SKU -> Async<PurchasePrices>)
    abstract member GetSales: (SKU -> Async<Sale list option>)
```

{% hint style="info" %}
Members are defined using parentheses, making them properties that return a function rather than methods. This reveals an implementation detail: we define these members by partial application of functions. This remains a minor leak, acceptable where we use this interface from F# code.
{% endhint %}

## Api Implementation

The `Api` class serves as the concrete domain project entry point:

```fsharp
[<Sealed>]
type internal Api(
    workflowRunnerFactory: IWorkflowRunnerFactory,
    catalogPipeline: CatalogPipeline,
    pricesPipeline: PricesPipeline,
    salesPipeline: SalesPipeline,
    warehousePipeline: WarehousePipeline,
    openLibraryPipeline: OpenLibraryPipeline
) = // ...
```

This internal class depends on `IWorkflowRunnerFactory` (from `Shopfoo.Program`), which creates a workflow runner for the current domain.

### Instruction Wiring with Undo Support

The most important part is the `prepareInstructions` function, which wires each instruction to its data-layer implementation and undo strategy.

**Product domain example** (from [`Shopfoo.Product`](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Api.fs)) — only uses queries and reversible commands:

```fsharp
let prepareInstructions (preparer: IInstructionPreparer<'ins>) =
    { new IProductInstructions with
        member _.GetPrices = preparer.Query(pricesPipeline.GetPrices, "GetPrices")
        member _.GetSales = preparer.Query(salesPipeline.GetSales, "GetSales")
        member _.GetStockEvents = preparer.Query(warehousePipeline.GetStockEvents, "GetStockEvents")

        member _.SavePrices =
            preparer
                .Command(pricesPipeline.SavePrices, "SavePrices")
                .Reversible(fun _ (PreviousValue initialPrices) ->
                    async {
                        let! res = pricesPipeline.SavePrices initialPrices
                        return res |> Result.ignore
                    })

        member _.SaveProduct =
            preparer
                .Command(catalogPipeline.SaveProduct, "SaveProduct")
                .Reversible(fun _ (PreviousValue initialProduct) ->
                    async {
                        let! res = catalogPipeline.SaveProduct initialProduct
                        return res |> Result.ignore
                    })

        member _.AddPrices =
            preparer
                .Command(pricesPipeline.AddPrices, "AddPrices")
                .Reversible(fun prices _ -> pricesPipeline.DeletePrices prices.SKU)

        member _.AddProduct =
            preparer
                .Command(catalogPipeline.AddProduct, "AddProduct")
                .Reversible(fun product _ -> catalogPipeline.DeleteProduct product.SKU)
    }
```

**Order domain example** (from [`Shopfoo.Program.Tests`](https://github.com/rdeneau/shopfoo/blob/main/tests/Shopfoo.Program.Tests/OrderContext/)) — showcases all three undo strategies (Reversible, Compensatable, NotUndoable):

```fsharp
let prepareInstructions (preparer: IInstructionPreparer<'ins>) =
    { new IOrderInstructions with
        member _.CreateOrder =
            preparer
                .Command(orderRepository.CreateOrder, "CreateOrder")
                .Reversible(fun cmd _ -> orderRepository.DeleteOrder cmd.OrderId)

        member _.IssueInvoice =
            preparer
                .Command(invoiceRepository.IssueInvoice, "IssueInvoice")
                .Compensatable(fun _ invoiceId ->
                    invoiceRepository.CompensateInvoice { InvoiceId = invoiceId })

        member _.ProcessPayment =
            preparer
                .Command(paymentRepository.ProcessPayment, "ProcessPayment")
                .Compensatable(fun _ paymentId ->
                    paymentRepository.RefundPayment { PaymentId = paymentId })

        member _.SendNotification =
            preparer
                .Command(notificationClient.SendNotification,
                    getName = (fun cmd -> $"SendNotificationOrder%s{cmd.NewStatus.Name}"))
                .NotUndoable()

        member _.ShipOrder =
            preparer
                .Command(warehouseClient.ShipOrder, "ShipOrder")
                .NotUndoable()

        member _.TransitionOrder =
            preparer
                .Command(orderRepository.TransitionOrder,
                    fun (FromTo(from, to')) -> $"TransitionOrderFrom%s{from}To%s{to'}")
                .Reversible(fun cmd _ -> orderRepository.TransitionOrder(cmd.Revert()))
    }
```

**Key patterns:**

* **Queries** are wrapped with `preparer.Query(work, name)` — this adds logging, timing, and step tracking.
* **Commands** are wrapped with `preparer.Command(work, name)` followed by the undo strategy:
  * `.Reversible(undoFun)` — the command can be exactly reversed, restoring the previous state. Two flavours are shown: using the `PreviousValue` returned by the command (e.g. `SavePrices` restores the initial prices) or using the original arguments (e.g. `CreateOrder` deletes by `OrderId`, `TransitionOrder` calls `cmd.Revert()`).
  * `.Compensatable(undoFun)` — the command cannot be literally reversed but can be logically compensated. For example, `IssueInvoice` is compensated (not deleted) and `ProcessPayment` is refunded (not reversed). The undo function uses the **return value** (e.g. the `invoiceId` or `paymentId`) to identify what to compensate.
  * `.NotUndoable()` — the command is fire-and-forget; no undo is attempted. Suitable for side effects that cannot be taken back, such as sending a notification or shipping an order.
* The undo function receives the **original arguments** and the **return value** — enabling it to use the `PreviousValue` to restore the prior state or the returned ID to identify what to undo.
* The `name` parameter can be a static string or a **dynamic function** (e.g. `TransitionOrder` uses `fun (FromTo(from, to')) -> ...` and `SendNotification` uses `getName = (fun cmd -> ...)`) to produce descriptive step names in the saga history.

### Running Workflows

The `IWorkflowRunner` interface offers two methods:

```fsharp
[<Interface>]
type IWorkflowRunner<'ins when Instructions<'ins>> =
    abstract member Run:
        workflow: #IProgramWorkflow<'ins, 'arg, 'ret> ->
        arg: 'arg ->
        prepareInstructions: (IInstructionPreparer<'ins> -> 'ins) ->
            Async<Result<'ret, Error>>

    abstract member RunInSaga:
        workflow: #IProgramWorkflow<'ins, 'arg, 'ret> ->
        arg: 'arg ->
        prepareInstructions: (IInstructionPreparer<'ins> -> 'ins) ->
        undoPredicate: CanUndo ->
            Async<Result<'ret, Error> * SagaState>
```

* **`Run`** — executes the workflow without saga capability. Internally uses `CanUndo.never`, so no undo is ever attempted. Returns only the result.
* **`RunInSaga`** — executes the workflow with saga support. Takes an `undoPredicate` of type `CanUndo` to control when undo is triggered. Returns both the result and the `SagaState` (useful for debugging or reporting).

#### Undo strategy: `CanUndo` type

The undo decision is controlled by `CanUndo`, a function type alias defined in `Shopfoo.Program`:

```fsharp
type UndoCriteria = { WorkflowError: Error; History: ProgramStep list }
type CanUndo = UndoCriteria -> bool

[<RequireQualifiedAccess>]
module CanUndo =
    let never: CanUndo = fun _ -> false
    let always: CanUndo = fun _ -> true
```

When a workflow step fails, the saga evaluates the `CanUndo` predicate with the `UndoCriteria` (containing the error and the history of completed steps). If it returns `true`, all previously completed commands are undone; if `false`, the saga stops without undo.

Two built-in helpers cover the most common cases:

* `CanUndo.always` — any failure triggers a full undo.
* `CanUndo.never` — no undo is ever attempted (used internally by `Run`).

#### Product domain example (from `Shopfoo.Product`)

```fsharp
let runWorkflow (workflow: IProductWorkflow<'arg, 'ret>) (arg: 'arg) =
    async {
        let workflowRunner = workflowRunnerFactory.Create(Manifest.DomainName)
        let! result, _state = workflowRunner.RunInSaga workflow arg prepareInstructions CanUndo.always
        return result
    }
```

Here every workflow uses `CanUndo.always`: any failure triggers undo of all previously completed commands.

#### Order domain example with custom undo predicate (from `Shopfoo.Program.Tests`)

A more sophisticated example from the test project shows a **custom `CanUndo` predicate** that conditionally prevents undo:

```fsharp
member private this.VerifyCancel(cancelAfterStep, expectedStatus, expectedHistory, ?expectedError) =
    async {
        let canUndoExceptAfterShipOrder undoCriteria =
            match undoCriteria with
            | { WorkflowError = BusinessError(As OrderCannotBeCancelledAfterShipping) } -> false
            | _ -> true

        let! result, sagaState =
            workflowRunner.RunInSaga
                (OrderWorkflow(cancelAfterStep))
                cmdCreateOrder
                (this.PrepareInstructions())
                canUndoExceptAfterShipOrder
        // ... verification logic
    }
```

The `canUndoExceptAfterShipOrder` predicate pattern-matches on `UndoCriteria`: it returns `false` when the error is `OrderCannotBeCancelledAfterShipping` (meaning the order has already been shipped and cannot be reversed), and `true` for all other errors (allowing full undo). This demonstrates how to implement **domain-specific undo policies** by inspecting the workflow error.

{% hint style="info" %}
See [The `As` active pattern](../../tips-and-tricks/as-active-pattern.md) for details on this pattern matching technique.
{% endhint %}

### Interface Implementation

```fsharp
interface IProductApi with
    member val GetProducts = catalogPipeline.GetProducts
    member val GetProduct = catalogPipeline.GetProduct
    member val SaveProduct = fun product -> runWorkflow SaveProductWorkflow.Instance product
    member val AddProduct = fun (product, currency) -> runWorkflow AddProductWorkflow.Instance (product, currency)

    member val GetPrices = pricesPipeline.GetPrices
    member val SavePrices = fun prices -> runWorkflow SavePricesWorkflow.Instance prices
    member val MarkAsSoldOut = fun sku -> runWorkflow MarkAsSoldOutWorkflow.Instance sku
    member val RemoveListPrice = fun sku -> runWorkflow RemoveListPriceWorkflow.Instance sku

    member val AdjustStock = warehousePipeline.AdjustStock
    member val DetermineStock = fun sku -> runWorkflow DetermineStockWorkflow.Instance sku

    member val GetSales = salesPipeline.GetSales
```

Each endpoint falls into one of two implementation styles:

* **Direct** — delegates to a data-layer pipeline call, with no workflow orchestration.
* **Workflow** — goes through `runWorkflow`, which handles business logic, validation, and multi-instruction orchestration with saga support.

| Entity  | Feature           | Type    | Implementation |
| ------- | ----------------- | ------- | -------------- |
| Product | `GetProducts`     | Query   | Direct         |
|         | `GetProduct`      | Query   | Direct         |
|         | `SaveProduct`     | Command | Workflow       |
|         | `AddProduct`      | Command | Workflow       |
| Prices  | `GetPrices`       | Query   | Direct         |
|         | `SavePrices`      | Command | Workflow       |
|         | `MarkAsSoldOut`   | Command | Workflow       |
|         | `RemoveListPrice` | Command | Workflow       |
| Stock   | `AdjustStock`     | Command | Direct 👈      |
|         | `DetermineStock`  | Query   | Workflow 👈    |
| Sales   | `GetSales`        | Query   | Direct         |

**Notes:**

* As a rule of thumb, **queries** use Direct calls while **commands** go through a Workflow.
* Two members — marked with 👈 — deviate from this default: `AdjustStock` and `DetermineStock`.
* The system is flexible enough to allow either style for any member, based on the actual needs of each feature.

## Dependency Injection

Registration happens at two levels.

### Program level — `AddProgram()`

The `Shopfoo.Program` project exposes a single extension method that registers the shared infrastructure:

```fsharp
// Shopfoo.Program / Dependencies.fs
type IServiceCollection with
    member services.AddProgram() =
        services
            .AddSingleton<IMetricsSender, MetricsLogger>()
            .AddSingleton<IWorkMonitors, WorkMonitors>()
            .AddSingleton<IWorkflowRunnerFactory, WorkflowRunnerFactory>()
```

This registers `IWorkflowRunnerFactory` — the factory injected into every domain `Api` class — along with the monitoring infrastructure (`IWorkMonitors`, `IMetricsSender`). The default `MetricsLogger` implementation simply logs metrics via `ILogger`; in a real-world application, it would be replaced by a concrete sender targeting an actual metrics backend.

### Domain level — `Add{Domain}Api()`

Each domain project encapsulates its own registrations:

```fsharp
// Shopfoo.Product / DependencyInjection.fs
type IServiceCollection with
    member services.AddProductApi() =
        services
            .AddSingleton<IProductApi, Api>()
            // ... data layer registrations
```

This keeps the `Api` class internal while exposing only the `IProductApi` interface. All data-layer types (pipelines, repositories) are registered here as well, invisible to the rest of the application.

### Composition root — `AddRemotingApi()`

Both methods are called from the presentation layer (`Shopfoo.Server`), which acts as the composition root:

```fsharp
// Shopfoo.Server / DependencyInjection.fs
type IServiceCollection with
    member services.AddRemotingApi(configuration: IConfiguration) =
        services.AddProgram() |> ignore

        services
            // ... configure settings
            .AddHttp()
            .AddProductApi()
            .AddHomeApi()
            // ... remoting API builders
```

`AddProgram()` is called once for the shared infrastructure, then each domain's `Add{Domain}Api()` method is called to wire up its own types. This layered approach keeps each project in control of its own registrations while the server project simply composes them together.

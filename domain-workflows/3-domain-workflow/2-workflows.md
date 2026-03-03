---
icon: arrow-progress
---

# Workflows

## Workflow Interface

Each workflow implements `IProgramWorkflow<'ins, 'arg, 'ret>`, which defines a `Run` method producing a `Program`:

```fsharp
[<Interface>]
type IProgramWorkflow<'ins, 'arg, 'ret when 'ins :> IProgramInstructions> =
    abstract member Run: 'arg -> Program<'ins, Res<'ret>>
```

In the Product domain, a convenience alias fixes the instruction set:

```fsharp
[<Interface>]
type IProductWorkflow<'arg, 'ret> =
    inherit IProgramWorkflow<IProductInstructions, 'arg, 'ret>
```

## Design Choice: Which Features Get Workflows?

_This is the approach chosen in the Shopfoo solution:_ evaluate each feature to determine whether it benefits from a workflow implementation.

Generally, **commands** are the best candidates — they typically contain business complexity and/or orchestrate multiple Data layer calls. **Queries** usually lack sufficient complexity and can be delegated directly to the Data layer.

However, exceptions exist, as seen in [Api.fs](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Api.fs):

* The `AdjustStock` command is delegated directly to the _Warehouse_ access client.
* The `DetermineStock` query is implemented as a workflow.

## Product Domain Workflow Examples

Let's examine characteristic workflows in order of increasing complexity.

### RemoveListPrice — Basic orchestration

This simple workflow orchestrates two instructions: `getPrices` then `savePrices`.

🔗 [Source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/RemoveListPrice.fs)

```fsharp
[<Sealed>]
type internal RemoveListPriceWorkflow private () =
    static member val Instance = RemoveListPriceWorkflow()

    interface IProductWorkflow<SKU, unit> with
        override _.Run sku =
            program {
                let! prices =
                    Program.getPrices sku
                    |> Program.requireSomeData ($"SKU #%s{sku.Value}", TypeName.Custom "Prices")

                let! (PreviousValue _) = Program.savePrices { prices with ListPrice = None }
                return Ok()
            }
```

**Key points:**

* The workflow class implements the **Singleton** pattern without relying on the IoC container.
* `Program.getPrices` returns `Program<_, Prices option>`. The `Program.requireSomeData` helper converts the inner `None` to a `DataRelatedError`, and the `program` CE's `Bind` overload automatically lifts it to `Error`.
* `savePrices` returns `Program<_, Res<PreviousValue<Prices>>>`. Writing `let! (PreviousValue _) = ...` rather than `let! _ = ...` is required: without the destructuring pattern the compiler cannot determine which `Bind` overload to use — the plain `Program` bind (which would hand `Res<PreviousValue<Prices>>` to the continuation) or the result-unwrapping bind (which extracts `PreviousValue<Prices>`). The pattern pins the expected type to `PreviousValue<Prices>`, resolving the ambiguity and selecting the unwrapping overload. The value itself is then discarded; the saga runner is the one that uses it for undo.

### SavePrices — Validation

This workflow demonstrates validation using guard clauses and the `validation` applicative CE:

🔗 [Source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/SavePrices.fs)

```fsharp
[<Sealed>]
type internal SavePricesWorkflow private () =
    static member val Instance = SavePricesWorkflow()

    interface IProductWorkflow<Prices, unit> with
        override _.Run prices =
            program {
                do! Prices.validate prices
                let! (PreviousValue _) = Program.savePrices prices
                return Ok()
            }
```

The `Prices.validate` function returns a `Validation<unit, GuardClauseError>` (an alias for `Result<unit, GuardClauseError list>`). It is built with the `validation` applicative CE, which collects all errors rather than stopping at the first one:

```fsharp
let validate (prices: Prices) =
    validation {
        let! _ = guardListPrice(prices).ToValidation()
        and! _ = guardRetailPrice(prices).ToValidation()
        return ()
    }
```

The `let! ... and! ...` applicative syntax inside `validation { }` runs both guards independently and merges any errors into a list. The `program` CE then provides a `Bind` overload that lifts `Validation<_, GuardClauseError>` directly to the common `Error` type.

### AddProduct — Parallel execution

This is the most interesting Product workflow — it demonstrates the `let! ... and! ...` applicative syntax for parallel execution:

🔗 [Source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/AddProduct.fs)

```fsharp
[<Sealed>]
type internal AddProductWorkflow private () =
    static member val Instance = AddProductWorkflow()

    interface IProductWorkflow<Product * Currency, unit> with
        override _.Run((product, currency)) =
            program {
                let sku =
                    match product.SKU.Type, product.Category with
                    | SKUType.OLID _, Category.Books book -> book.ISBN.AsSKU
                    | _ -> product.SKU

                let product = { product with SKU = sku }

                do! Product.validate product |> liftValidation

                // addProduct and addPrices run in PARALLEL
                let! resA = Program.addProduct product
                and! resB = Program.addPrices (Prices.Initial(sku, currency))

                return Result.zip resA resB |> Result.ignore
            }
```

The `let! ... and! ...` syntax compiles into a call to `MergeSources`, which uses `Program.map2` to start both instructions concurrently via `Async.StartChild`. This pattern is **impossible** with V3's free monad approach due to F#'s strict variance checking on generics.

`Result.zip` combines two `Result` values — if both succeed, it combines them; if either fails, it returns the error(s). `Result.ignore` discards the success value — a useless pair of unit values.

### DetermineStock — Query workflow

This query workflow orchestrates multiple instructions and applies a domain rule inspired by the **Decider** pattern (\*):

🔗 [Source code](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Product/Workflows/DetermineStock.fs)

```fsharp
[<Sealed>]
type internal DetermineStockWorkflow private () =
    static member val Instance = DetermineStockWorkflow()

    interface IProductWorkflow<SKU, Stock> with
        override _.Run sku =
            program {
                let! (sales: Sale list) = Program.getSales sku |> Program.defaultValue []
                let! (stockEvents: StockEvent list) = Program.getStockEvents sku |> Program.defaultValue []

                let allEvents = [...]  // merge and sort events
                let quantity = (0, allEvents) ||> Seq.fold (fun acc ... -> ...)

                return Ok { SKU = sku; Quantity = quantity }
            }
```

Notice that `getSales` and `getStockEvents` use `Program.defaultValue []` instead of `requireSome` — returning an empty list when data is missing rather than failing. This is a design choice per workflow.

{% hint style="info" %}
### (\*) Decider pattern

The `Seq.fold` accumulating stock quantity over the sorted event list is the `evolve` function from the Decider pattern: given a current state and an event, produce the next state. Applying it over the full event sequence reconstructs the current state from scratch — the same principle as Event Sourcing, but without a persistent event store.

Complementary resources:

* [Functional Event Sourcing Decider](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider) — Jérémie Chassaing
* [The Equinox Programming Model](https://nordfjord.io/equinox/) — Einar Norðfjörð
{% endhint %}

## Order Domain — Saga and Cancellation

The `Shopfoo.Program.Tests` project contains an **Order domain** that showcases the saga pattern (undo on failure) and workflow cancellation. This is a better illustration of these features than the Product domain.

🔗 [Source code: OrderContext/](https://github.com/rdeneau/shopfoo/tree/main/tests/Shopfoo.Program.Tests/OrderContext)

### Order Instructions

```fsharp
[<Interface>]
type IOrderInstructions =
    inherit IProgramInstructions
    abstract member CreateOrder: (Cmd.CreateOrder -> Async<Result<unit, Error>>)
    abstract member ProcessPayment: (Cmd.ProcessPayment -> Async<Result<PaymentId, Error>>)
    abstract member IssueInvoice: (Cmd.IssueInvoice -> Async<Result<InvoiceId, Error>>)
    abstract member SendNotification: (Cmd.NotifyOrderChanged -> Async<Result<unit, Error>>)
    abstract member ShipOrder: (Cmd.ShipOrder -> Async<Result<ParcelId, Error>>)
    abstract member TransitionOrder: (Cmd.TransitionOrder -> Async<Result<unit, Error>>)
```

All instructions are commands (no queries), each returning a `Result`. Commands that produce an ID (`PaymentId`, `InvoiceId`, `ParcelId`) (\*) return it in the `Ok` track — enabling the saga to pass these IDs to subsequent undo functions.

{% hint style="info" %}
(\*) In production designs, it is generally preferable for the client to generate IDs before sending a command. Client-generated IDs make idempotency straightforward: retrying the same command with the same ID is safe because the server can detect and ignore duplicates. Here, the IDs are generated server-side by the instructions specifically to illustrate two things: how a command's return value flows into the next step of the workflow, and how that same return value is captured by the saga runner and passed to the undo function.
{% endhint %}

### Order Workflow with Cancellation Support

The `OrderWorkflow` accepts an optional `cancelAfterStep` parameter that simulates a **client cancellation** — an event that can occur at any point in real life. A dedicated unit test covers each step at which cancellation may happen, verifying that the workflow behaves as expected in every scenario:

```fsharp
type OrderWorkflow(?cancelAfterStep: OrderStep) =
    interface IProgramWorkflow<IOrderInstructions, Cmd.CreateOrder, unit> with
        override _.Run({ OrderId = orderId; Price = orderPrice } as cmd) =
            let cmder = Cmder orderId

            let cancelAfter step actualStatus =
                program {
                    if cancelAfterStep <> Some step then
                        return Ok()
                    elif step = OrderStep.ShipOrder then
                        return Error(BusinessError OrderCannotBeCancelledAfterShipping)
                    else
                        do! Program.transitionOrder (cmder.TransitionOrder { From = actualStatus; To = OrderCancelled actualStatus })
                        return Error(WorkflowError(WorkflowCancelled(step = $"%A{step}")))
                }

            program {
                // CreateOrder
                do! Program.createOrder cmd
                let currentStatus = OrderCreated
                do! cancelAfter OrderStep.CreateOrder currentStatus

                // ProcessPayment
                let! (paymentId: PaymentId) = Program.processPayment { OrderId = orderId; Amount = orderPrice }
                let currentStatus, previousStatus = OrderPaid paymentId, currentStatus
                do! Program.transitionOrder (cmder.TransitionOrder { From = previousStatus; To = currentStatus })
                do! Program.sendNotification (cmder.NotifyOrderChanged currentStatus)
                do! cancelAfter OrderStep.ProcessPayment currentStatus

                // IssueInvoice
                let! (invoiceId: InvoiceId) = Program.issueInvoice { OrderId = orderId; Amount = orderPrice }
                let currentStatus, previousStatus = OrderInvoiced invoiceId, currentStatus
                do! Program.transitionOrder (cmder.TransitionOrder { From = previousStatus; To = currentStatus })
                do! Program.sendNotification (cmder.NotifyOrderChanged currentStatus)
                do! cancelAfter OrderStep.IssueInvoice currentStatus

                // ShipOrder
                let! (parcelId: ParcelId) = Program.shipOrder { Cmd.ShipOrder.OrderId = orderId }
                let currentStatus, previousStatus = OrderShipped parcelId, currentStatus
                do! Program.transitionOrder (cmder.TransitionOrder { From = previousStatus; To = currentStatus })
                do! Program.sendNotification (cmder.NotifyOrderChanged currentStatus)
                do! cancelAfter OrderStep.ShipOrder currentStatus

                return Ok()
            }
```

**Cancellation mechanism:**

* The `cancelAfter` helper checks if the current step matches the cancellation target.
* If so, it transitions the order to `OrderCancelled` (recording the previous status) and returns a `WorkflowCancelled` error — which the saga recognizes as intentional and does **not** undo.
* After shipping, cancellation returns a `BusinessError` instead — the saga can be configured to not undo in this case either.

### Undo Strategies in the Wiring

Each instruction's undo strategy is defined when wiring instructions in the test setup:

```fsharp
member private _.PrepareInstructions() =
    fun (preparer: IInstructionPreparer<'ins>) ->
        { new IOrderInstructions with
            member _.CreateOrder =
                preparer
                    .Command(orderRepository.CreateOrder, "CreateOrder")
                    .Reversible(fun cmd _ -> orderRepository.DeleteOrder cmd.OrderId)

            member _.ProcessPayment =
                preparer
                    .Command(paymentRepository.ProcessPayment, "ProcessPayment")
                    .Compensatable(fun _ paymentId -> paymentRepository.RefundPayment { PaymentId = paymentId })

            member _.IssueInvoice =
                preparer
                    .Command(invoiceRepository.IssueInvoice, "IssueInvoice")
                    .Compensatable(fun _ invoiceId -> invoiceRepository.CompensateInvoice { InvoiceId = invoiceId })

            member _.SendNotification =
                preparer
                    .Command(notificationClient.SendNotification, ...)
                    .NotUndoable()

            member _.ShipOrder =
                preparer
                    .Command(warehouseClient.ShipOrder, "ShipOrder")
                    .NotUndoable()

            member _.TransitionOrder =
                preparer
                    .Command(orderRepository.TransitionOrder, ...)
                    .Reversible(fun cmd _ -> orderRepository.TransitionOrder(cmd.Revert()))
        }
```

Three undo strategies are demonstrated:

| Strategy        | Example                            | Behavior                                           |
| --------------- | ---------------------------------- | -------------------------------------------------- |
| `Reversible`    | `CreateOrder` → `DeleteOrder`      | Strict undo: restores exact initial state          |
| `Compensatable` | `ProcessPayment` → `RefundPayment` | Logical offset: creates a compensating operation   |
| `NotUndoable`   | `SendNotification`                 | No undo possible: notifications cannot be recalled |

### Test Cases

The tests verify both **undo on failure** and **cancellation without undo**. For cancellation, a dedicated test covers each step where a client cancellation can occur, ensuring the workflow responds correctly in each case:

```fsharp
// Undo: processPayment failed → only createOrder is undone
this.VerifyUndo(
    simulate = { Error = ...; Step = OrderStep.ProcessPayment },
    expectedHistory = [ "CreateOrder", UndoDone ]
)

// Cancel: cancel after ProcessPayment → no undo, order stays in Cancelled state
this.VerifyCancel(
    cancelAfterStep = OrderStep.ProcessPayment,
    expectedStatus = LightOrderCancelled LightOrderPaid,
    expectedHistory = [
        "TransitionOrderFromPaidToCancelled", RunDone
        "SendNotificationOrderPaid", RunDone
        "TransitionOrderFromCreatedToPaid", RunDone
        "ProcessPayment", RunDone
        "CreateOrder", RunDone
    ]
)
```

The saga state preserves the **complete step history in LIFO order**, including each step's status (`RunDone`, `UndoDone`, `RunFailed`, `UndoFailed`). This provides full observability into what happened during the workflow execution.

{% hint style="info" %}
### Saga without messages

This saga pattern operates entirely in-process, synchronously (within a single `Async` computation). There is no message bus, no distributed transaction coordinator. The "undo" functions are plain async functions called in reverse LIFO order. This makes the pattern suitable for orchestrating multiple data-layer calls within a single request, while keeping the workflow logic pure and testable.
{% endhint %}

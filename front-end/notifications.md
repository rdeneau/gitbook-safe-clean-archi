# Notifications

## Toast notifications

Shopfoo displays toast notifications to give feedback after save operations (success or error). Unlike many applications that rely on a dedicated toast library (e.g. `react-toastify`), Shopfoo uses a custom `Toast` React component built with DaisyUI's `toast` and `alert` primitives.

### How it works

The toast mechanism is implemented entirely through the Elmish model at the application level (`View.fs`):

**1. Toast type** (`Shared.fs`): A discriminated union models each kind of toast, carrying the relevant data and an optional `ApiError`.

```fsharp
[<RequireQualifiedAccess>]
type Toast =
    | Lang of Lang
    | Prices of Prices * ApiError option
    | Product of Product * ApiError option
    | Sale of SKU * ApiError option
    | Stock of Stock * ApiError option
    | Supply of SKU * ApiError option
```

**2. App model** (`View.fs`): The root `AppView` component holds a `Toast option` in its Elmish model. The `update` function handles `ToastOn` and `ToastOff` messages.

```fsharp
type private Msg =
    | ...
    | ToastOn of Toast
    | ToastOff

type private Model = {
    ...
    Toast: Toast option
}

let private update (msg: Msg) (model: Model) =
    match msg with
    | ...
    | Msg.ToastOn toast -> { model with Toast = Some toast }, Cmd.none
    | Msg.ToastOff -> { model with Toast = None }, Cmd.none
```

**3. Env abstraction** (`Shared.fs`): An `IShowToast` interface lets child pages and components trigger a toast without depending on the root component, following the Dependency Inversion Principle.

```fsharp
// Shared.fs — interface declaration
module Env =
    type IShowToast =
        abstract member ShowToast: toast: Toast -> unit

// View.fs — implementation in the root component
type private Env(fullContext, dispatch) =
    interface Env.IShowToast with
        member _.ShowToast toast = dispatch (Msg.ToastOn toast)
```

**4. Child components**: After a save operation completes, the child calls `env.ShowToast(Toast.Xxx(...))` from a `Cmd.ofEffect`. The effect passes the result (with or without error) up to the root, which renders the appropriate toast.

```fsharp
// Product/Details/Page.fs — wiring callbacks
let onSavePrice (price, error) = env.ShowToast(Toast.Prices(price, error))
let onSaveStock (stock, error) = env.ShowToast(Toast.Stock(stock, error))

// Product/Details/AdjustStock.fs — triggering from update via Cmd.ofEffect
| AdjustStock(Done result) ->
    { model with SaveDate = result |> Result.map (fun () -> DateTime.Now) |> Remote.ofResult },
    Cmd.ofEffect (fun _ -> onSave (model.Stock, result |> Result.tryGetError))
```

**5. Toast rendering** (`View.fs`): The root view pattern-matches on `model.Toast` to render the appropriate toast. A local helper builds the alert with the right style (success/error) and dismiss mode.

```fsharp
let toast name error =
    let alertType, text, dismiss =
        match error with
        | None -> alert.success, translations.Home.SavedOk name, Toast.Dismiss.Auto
        | Some err -> alert.error, translations.Home.SavedError(name, err.ErrorMessage), Toast.Dismiss.Manual

    Toast.Toast $"toast-{DateTime.Now.Ticks}" [ alertType ] dismiss onDismiss [
        Html.text text
    ]

// In the view:
match model.Toast with
| None -> ()
| Some(Toast.Product(product, error)) ->
    toast $"%s{translations.Home.Product} %s{product.SKU.Value}" error
| Some(Toast.Prices(prices, error)) ->
    toast $"%s{translations.Product.Price} %s{prices.SKU.Value}" error
| ...
```

**6. Toast component** (`Components/Toast.fs`): A React component that renders a DaisyUI alert positioned as a toast. It supports two dismiss modes:

- `Auto`: the toast disappears after a timeout (3 seconds).
- `Manual`: the toast stays visible with a close button — used for errors.

```fsharp
[<RequireQualifiedAccess>]
type Dismiss =
    | Auto
    | Manual

let private Timeout = TimeSpan.FromMilliseconds(3000)

[<ReactComponent>]
let Toast key alertProps dismiss onDismiss children =
    let isVisible, toggle = React.useState true

    React.useEffectOnce (fun () ->
        match dismiss with
        | Dismiss.Auto ->
            let hidingTimeoutId =
                JS.setTimeout (fun _ -> toggle false; onDismiss ()) (int Timeout.TotalMilliseconds)
            { new IDisposable with member _.Dispose() = JS.clearTimeout hidingTimeoutId }
        | Dismiss.Manual ->
            { new IDisposable with member _.Dispose() = () }
    )

    if not isVisible then
        Html.none
    else
        Daisy.toast [
            toast.bottom; toast.end'
            prop.child (
                Daisy.alert [
                    yield! alertProps
                    if dismiss = Dismiss.Manual then prop.className "pr-12"
                    prop.children (elems = children)
                ]
            )
        ]
```

### `Cmd.ofEffect` trade-off

Child components use `Cmd.ofEffect` to call `env.ShowToast` (or other environment callbacks like `env.FillTranslations`) from their `update` function. This is a pragmatic choice: it avoids threading toast messages through each component's own `Msg` type and keeps the communication simple.

[Jordan Marr](https://jordanmarr.github.io/fsharp/unit-testing-fable-dotnet/#avoid-browser-specific-functions-in-elmish-handlers) also suggests using the `Cmd` track for toast notifications. His approach wraps *Toastify* calls behind helper commands (`Cmd.error`, `Cmd.info`…), hiding the library dependency. However, because toast display is handled entirely through side-effect commands, there is no model state to assert on in tests.

Shopfoo takes a different approach: the `Toast` is stored in the Elmish model (`Toast option`), which means the toast state is observable and testable — e.g. verifying that after a save error, the model contains the expected `Toast.Prices(_, Some error)`. This makes the rendering logic deterministic and unit-testable without involving the DOM.

{% hint style="warning" %}
🚧 **TODO:** Shopfoo does not yet have tests demonstrating this capability. Adding them would strengthen the architecture's testability story.
{% endhint %}

---
icon: chart-network
---

# Data flow

With separated Elmish models (`React.useElmish` per component), data transfers between components are **more numerous and more important** than with a single root Elmish program. There is no global state: each component manages its own `Model`. This makes data flow patterns explicit and intentional.

## Top-down: props and environment

### Classic props

Like in any React application, parent components pass data to children through function parameters: values, records, discriminated unions.

### The `Env` pattern

Rather than passing many individual parameters, the root `AppView` builds an **`Env` object** that bundles shared context and capabilities. Each page declares only the interfaces it needs, using **F# flexible type constraints** with intersection:

```fsharp
// Page declares its dependencies
let ProductDetailsView (env: #Env.IFullContext & #Env.IFillTranslations & #Env.IShowToast) sku =
    ...

// A simpler page needs less
let AboutView (env: #Env.IFullContext) =
    ...
```

The `Env` module defines small, focused interfaces following the **Interface Segregation Principle**:

```fsharp
module Env =
    type [<Interface>] IFullContext      = abstract member FullContext: FullContext
    type [<Interface>] IFillTranslations = abstract member FillTranslations: Translations -> unit
    type [<Interface>] ILoginUser        = abstract member LoginUser: User -> unit
    type [<Interface>] IShowToast        = abstract member ShowToast: Toast -> unit
```

The root `AppView` creates the concrete `Env`, wiring each interface to its `fullContext` property and `dispatch` function:

```fsharp
type private Env(fullContext, dispatch) =
    interface Env.IFullContext      with member _.FullContext = fullContext
    interface Env.IFillTranslations with member _.FillTranslations t = dispatch (Msg.FillTranslations t)
    interface Env.ILoginUser        with member _.LoginUser user     = dispatch (Msg.Login user)
    interface Env.IShowToast        with member _.ShowToast toast    = dispatch (Msg.ToastOn toast)
```

{% hint style="info" %}
This pattern is inspired by [Dependency injection in F# — The missing manual](https://medium.com/@lanayx/dependency-injection-in-f-the-missing-manual-d376e9cafd0f) by Vladimir Shchur, which explores using flexible types (`#IFoo`) and interface intersection (`#IFoo & #IBar`) as a lightweight, compile-time DI mechanism.
{% endhint %}

## Bottom-up: callbacks vs. Intent pattern

Propagating data from child to parent is the harder direction. The approach differs between single-program Elmish (SAFE) and multi-program Elmish (SAFEr).

### Single Elmish program (SAFE): Intent pattern

In a classic SAFE application with a single root Elmish program, the child `update` function can return a **third element** alongside `Model` and `Cmd` — an `Intent` (also called `Notification`):

```fsharp
// Child update returns a triple
val update : Msg -> Model -> Model * Cmd<Msg> * Intent

type Intent =
    | UserLoggedIn of User
    | DoNothing
```

The parent intercepts the intent and acts on it within its own `update`. This pattern is described in detail in [The Elmish Book — Intent](https://zaid-ajaj.github.io/the-elmish-book/#/chapters/scaling/intent).

The Intent pattern exists precisely because, in a single-program architecture, the parent's `update` wraps the child's — and **nothing prevents the parent from intercepting a child message directly** rather than going through a dedicated intent. This breaks the separation of responsibilities: the parent becomes coupled to the child's internal messages. The `Intent` type makes the contract explicit, but it remains a convention — the compiler does not enforce it.

### Separated Elmish models (SAFEr): callbacks

With `React.useElmish`, each component has its own isolated Elmish loop. There is no parent `update` function wrapping the child's. Instead, **parent views pass callback functions** to their children:

```fsharp
// Parent defines callbacks
let onSaveProduct (product, error) =
    updateProductModel { productModel with SKU = product.SKU }
    env.ShowToast(Toast.Product(product, error))

// Parent passes them to child
CatalogInfoForm "catalog-info" fullContext productModel env.FillTranslations onSaveProduct
```

The child invokes the callback from its own `update` via `Cmd.ofEffect`:

```fsharp
let update fillTranslations onSaveProduct fullContext msg model =
    match msg with
    | SaveProduct(product, Done result) ->
        { model with SaveDate = ... },
        Cmd.ofEffect (fun _ -> onSaveProduct (product, result |> Result.tryGetError))
```

{% hint style="info" %}
Callbacks typically follow a `(data, ApiError option)` tuple convention, letting the parent decide how to handle success vs. failure.
{% endhint %}

**Limitation:** a callback is just a function — the child only has the parameter name and signature to understand when to call it. This can become unclear when multiple callbacks are passed. It is also unclear when the callback's type is inferred and not used directly but forwarded (e.g. from the view function to `update`): hovering over the parameter only shows a generic function signature, with no hint about its purpose.

One improvement is to **replace individual callback functions with an object** exposing named methods. The object type and method argument names provide more context than a bare function. This is the approach used by [`DrawerControl`](data-flow.md#drawer) described below. The trade-off is that F# type inference does not work with object method calls, so the variable must be **explicitly annotated** (e.g. `(drawerControl: DrawerControl)`).

## Concrete data flows

### Translations

Translations are **lazily loaded per page** and **cached at the root level** — a key example of the single source of truth principle.

1. The root `AppView` holds `FullContext.Translations` in its `Model`.
2. When a page fetches its data, the API response includes fresh translations.
3. The page calls `env.FillTranslations(translations)` to propagate them up.
4. The root merges them into its `FullContext`, which flows back down to the next page (i.e. the page rendered after a navigation event).

```fsharp
// In a page's update function
| ProductFetched(Ok(response, translations)) ->
    { model with Product = Remote.ofOption response.Product },
    Cmd.ofEffect (fun _ -> fillTranslations translations)
```

For more details, see [Translations](../translations.md).

### Toast notifications

Toast messages bubble up from child forms to the root via `env.ShowToast`:

1. `ProductDetailsView` defines callbacks like `onSavePrice` that call `env.ShowToast(Toast.Prices(...))`.
2. These callbacks are passed to child forms (`ActionsForm`, `ManagePriceForm`, etc.).
3. Child forms invoke them after a successful (or failed) API call.
4. The root `AppView` renders the toast based on its `Model.Toast` value.

### Drawer

The `DrawerControl` object manages drawer open/close state with a **listener pattern**:

1. The parent (`ProductDetailsView`) creates a `DrawerControl`, wiring `open'` and `close` to its own `dispatch` function.
2. It passes the `DrawerControl` to child forms.
3. A child calls `drawerControl.Open(Drawer.ManagePrice(...))` — the parent's model updates and the drawer appears.
4. The form inside the drawer calls `drawerControl.Close()` when done.
5. Other children register listeners via `drawerControl.OnClose` to refresh their data:

```fsharp
drawerControl.OnClose(fun drawer ->
    match drawer with
    | Drawer.ManagePrice(_, savedPrices) -> dispatch (PricesFetched(Ok { Prices = Some savedPrices }))
    | Drawer.InputSales _                -> dispatch RefreshAfterSale
    | ...
)
```

### Product Sold-out

The sold-out status is derived from prices in `ActionsForm` and displayed by `CatalogInfoForm`:

1. `ProductDetailsView` holds `productModel` state (including `SoldOut`) via `React.useState` — separate from the Elmish model because it is purely visual data that the `update` function does not need to know about.
2. It passes a `setSoldOut` callback to `ActionsForm`.
3. `ActionsForm` calls `setSoldOut(true/false)` whenever prices are loaded or updated.
4. `ProductDetailsView` passes `productModel` to `CatalogInfoForm`, which renders a "sold out" badge accordingly.

## Summary

<pre class="language-txt"><code class="lang-txt">AppView (root Elmish model: FullContext, Toast)
│
<strong>└── env (Env object: IFullContext, IFillTranslations, ILoginUser, IShowToast)
</strong>    │
    ├── LoginView
    │   ↑ env.FillTranslations(translations)
    │   ↑ env.LoginUser(user)
    │
    ├── ProductDetailsView (local state: drawer, productModel)
    │   │
    │   ├── CatalogInfoForm
    │   │   ↓ fullContext, productModel, fillTranslations
    │   │   ↑ onSaveProduct(product, error)
    │   │
    │   ├── ActionsForm
    │   │   ↓ fullContext, drawerControl
    │   │   ↑ onSavePrice(prices, error)
    │   │   ↑ setSoldOut(bool)
    │   │
    │   └── ManagePriceForm (in drawer)
    │       ↓ fullContext, drawerControl
    │       ↑ onSave(prices, error)
    │
    └── ProductIndexView
        ↑ env.FillTranslations(translations)

↓ = top-down (props, env)
↑ = bottom-up (callbacks)
</code></pre>

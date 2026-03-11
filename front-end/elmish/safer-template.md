---
icon: shield-heart
---

# SAFEr template

{% hint style="warning" %}
This section assumes a basic familiarity with the Elmish pattern. Otherwise, please read [The Elmish Book](https://zaid-ajaj.github.io/the-elmish-book/) first. It is a must-read.
{% endhint %}

The `Shopfoo.Client` project is based on the **SAFEr template**. Unlike the classic SAFE template, where a single Elmish program manages the entire application state in a Russian-doll/top-down hierarchy, the SAFEr template uses **`React.useElmish`** at the component level.

Each page or form component owns its own Elmish loop (`Model`, `Msg`, `init`, `update`), scoped to the component's lifetime. This approach:

* Keeps each component's state **local and self-contained**
* Avoids a monolithic root model that grows with every new feature
* Leverages React's component lifecycle to mount/unmount Elmish loops naturally

{% hint style="info" %}
All components using Elmish import `open Feliz.UseElmish` and follow the same pattern:

```fsharp
let model, dispatch = React.useElmish (init, update, [| dependencies |])
```
{% endhint %}

## Elmish Components in Shopfoo.Client

### Root Page

| Component | File      | Description                                                                      |
| --------- | --------- | -------------------------------------------------------------------------------- |
| `AppView` | `View.fs` | Root component: manages global context (translations, user), dispatches to pages |

### Stateful Pages

| Component            | File                            | Description                                   |
| -------------------- | ------------------------------- | --------------------------------------------- |
| `LoginView`          | `Pages/Login.fs`                | Login page                                    |
| `ProductIndexView`   | `Pages/Product/Index/Page.fs`   | Product listing with filters                  |
| `ProductDetailsView` | `Pages/Product/Details/Page.fs` | Product details, orchestrates sub-forms below |

### Stateless Pages

The remaining pages (`About`, `Admin`, `NotFound`) are purely presentational — they render static content and don't require an Elmish loop.

### Individual Form Components

These components live inside the Product Details page. Each has its own `useElmish` loop for managing form state, validation, and API calls.

| Component           | File                                     | Description                      |
| ------------------- | ---------------------------------------- | -------------------------------- |
| `ActionsForm`       | `Pages/Product/Details/Actions.fs`       | Product actions (drawer)         |
| `CatalogInfoForm`   | `Pages/Product/Details/CatalogInfo.fs`   | Edit product catalog information |
| `ManagePriceForm`   | `Pages/Product/Details/ManagePrice.fs`   | Price management (drawer)        |
| `ReceiveSupplyForm` | `Pages/Product/Details/ReceiveSupply.fs` | Receive supply stock (drawer)    |
| `InputSalesForm`    | `Pages/Product/Details/InputSales.fs`    | Input sales data (drawer)        |
| `AdjustStockForm`   | `Pages/Product/Details/AdjustStock.fs`   | Adjust stock quantity (drawer)   |

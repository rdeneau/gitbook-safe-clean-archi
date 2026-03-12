---
icon: clipboard-list-check
---

# Validation

Front-end validation acts as the **first line of defense** before server-side validation that occurs in domain workflows (see [Domain Model](../domain-workflows/3-domain-workflow/0-domain-model.md) — Guard clauses and Validation sections).

## Shared validation rules

The key design decision is that **validation rules are defined once** in `Domain.Types`, alongside the types they protect, and consumed by both the server and the client (via Fable):

```fsharp
// Shopfoo.Domain.Types / Catalog.fs
[<RequireQualifiedAccess>]
module Product =
    module Guard =
        let SKU          = GuardCriteria.Create("SKU", required = true)
        let Name         = GuardCriteria.Create("Name", required = true, maxLength = 128)
        let BookSubtitle = GuardCriteria.Create("BookSubtitle", maxLength = 256)
        let Description  = GuardCriteria.Create("Description", maxLength = 512)
        let ImageUrl     = GuardCriteria.None
```

Each `GuardCriteria` bundles the constraints for a single field — `Required`, `MinLength`, `MaxLength` — so that both layers derive their behavior from the same source of truth, with **zero rule duplication**.

| Field        | Required | Max length | Notes                                                           |
| ------------ | :------: | :--------: | --------------------------------------------------------------- |
| SKU          |   Yes    |     —      | Validated server-side only (not editable in the form)           |
| Name         |   Yes    |    128     |                                                                 |
| Description  |    No    |    512     |                                                                 |
| BookSubtitle |    No    |    256     | Books only                                                      |
| ImageUrl     |    —     |     —      | `GuardCriteria.None` — broken-link detection handled separately |

## `GuardProps`: bridging criteria to React

On the client side, a `GuardProps` class (defined in `UI.fs`) turns a `GuardCriteria` into ready-to-use React properties:

```fsharp
// Shopfoo.Client / UI.fs
type GuardProps(criteria: GuardCriteria, value: string, translations: AppTranslations, ?invalid) =
    let len = if isNull value then 0 else value.Length

    member private _.Invalid =
        (criteria.Required && len = 0)
        || (criteria.MinLength |> Option.exists (fun minLength -> len < minLength))
        || (criteria.MaxLength |> Option.exists (fun maxLength -> len > maxLength))
        || defaultArg invalid false

    member _.textCharCount   // 👈 e.g. "42 / 128", red when exceeded
    member _.textRequired    // 👈 "(Required)", red when empty
    member this.validation   // 👈 HTML5 attributes + "input-error" CSS class
    member _.value           // 👈 prop.value binding
```

An extension method makes the call site concise:

```fsharp
type GuardCriteria with
    member this.props(value, translations, ?invalid) =
        GuardProps(this, value, translations, ?invalid = invalid)
```

### What each member produces

| Member          | Output                                                                | Visual effect                                       |
| --------------- | --------------------------------------------------------------------- | --------------------------------------------------- |
| `textRequired`  | `" (Required)"` text + optional `text-error` class                    | Label turns red when the field is empty             |
| `textCharCount` | `"len / maxLength"` text + optional `text-error` class                | Counter turns red when the limit is exceeded        |
| `validation`    | `required`, `minLength`, `maxLength` attributes + `input-error` class | DaisyUI input styling + HTML5 constraint validation |
| `value`         | `prop.value` binding                                                  | Keeps the input in sync with the model              |

## Usage pattern in form fieldsets

Every validated field in `CatalogInfo.fs` follows the same structure:

```fsharp
member _.name() =
    Daisy.fieldset [
        prop.key "name-fieldset"
        prop.className "mb-2"
        prop.children [
            let props = Product.Guard.Name.props (product.Title, translations) // 👈 build props

            Daisy.fieldsetLabel [
                Html.text translations.Product.Name
                Html.small [ yield! props.textRequired ]  // 👈 "(Required)" indicator
                Html.small [ prop.className "flex-1" ]
                Html.span [ yield! props.textCharCount ]  // 👈 "3 / 128" counter
            ]

            Daisy.validator.input [                       // 👈 Daisy UI validator component
                prop.className "w-full"
                prop.placeholder translations.Product.Name
                props.value                               // 👈 value binding
                yield! props.validation                   // 👈 HTML5 + error styling
                yield! propOnChangeOrReadonly (fun name -> dispatch (Msg.changeName name product))
            ]
        ]
    ]
```

{% hint style="info" %}
`Daisy.validator.input` comes from [Feliz.DaisyUI](https://dzoukr.github.io/Feliz.DaisyUI/#/validator), the F#/Fable binding for the DaisyUI [Validator](https://daisyui.com/components/validator/) component. It wraps an `<input>` with the `validator` CSS class, enabling built-in error styling when HTML5 constraints are violated.
{% endhint %}

### Image URL: custom `invalid` parameter

The `ImageUrl` field has no length or required constraints (`GuardCriteria.None`), but it supports **broken-link detection** via the optional `invalid` parameter:

```fsharp
let props = Product.Guard.ImageUrl.props (product.ImageUrl.Url, translations, invalid = product.ImageUrl.Broken)
```

The `ImageUrl` type carries a `Broken` flag alongside the URL:

```fsharp
// Shopfoo.Domain.Types / Catalog.fs
type ImageUrl = {
    Url: string
    Broken: bool
} with
    static member Valid(url) : ImageUrl = { Url = url; Broken = false }
    static member None: ImageUrl = { Url = ""; Broken = true }
```

In the image preview, the `<img>` element listens for the `onError` event and flips that flag via the Elmish dispatch:

```fsharp
// CatalogInfo.fs — buildImagePreview
Html.img [
    prop.src product.ImageUrl.Url
    prop.onError (fun _ ->
        dispatch (ProductChanged { product with Product.ImageUrl.Broken = true })
    )
]
```

This triggers a model update (`ProductChanged`), which re-renders the form. The `GuardProps` then picks up the `Broken` flag through its `invalid` parameter and applies the `input-error` class on the URL input — giving the user immediate visual feedback that the image could not be loaded: red <i class="fa-solid fa-link-slash"></i> `link-slash` icon.

### Read-only mode

The `Fieldset` class conditionally replaces editable props with read-only attributes depending on the user's catalog access level:

```fsharp
let propsOrReadonly (props: IReactProperty seq) = [
    match catalogAccess with
    | Some Access.Edit -> yield! props
    | _ ->
        prop.readOnly true
        prop.className "bg-base-300"
]
```

This ensures the same form renders in both **edit** and **view** modes without duplicating the layout.

## Summary

| Aspect                     | Detail                                                                        |
| -------------------------- | ----------------------------------------------------------------------------- |
| **Single source of truth** | `Product.Guard.*` in `Domain.Types`                                           |
| **Shared across layers**   | Same `GuardCriteria` used by server validation and client UI (via Fable)      |
| **Client bridge**          | `GuardProps` class turns criteria into React props                            |
| **Visual feedback**        | Required indicator, character counter, DaisyUI error styling                  |
| **HTML5 integration**      | `required`, `minLength`, `maxLength` attributes for native browser validation |
| **Extensibility**          | Optional `invalid` parameter for custom checks (e.g. broken image URL)        |

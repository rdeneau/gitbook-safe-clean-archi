---
icon: diamond-turn-right
---

# Navigation

## Overview

Navigation in *Shopfoo* is built on top of [Feliz.Router](https://github.com/Zaid-Ajaj/Feliz.Router), a type-safe client-side routing library for Fable/React applications. The routing logic lives in two main files:

- **`Routing.fs`** — Defines the `Page` discriminated union, URL encoding/decoding, and navigation helpers.
- **`View.fs`** — The application entry point, where the root `AppView` React component uses its own Elmish loop to manage global concerns including URL changes via `React.router`.

## The `Page` type

All navigable pages are represented by a single discriminated union:

```fsharp
type Page =
    | About
    | Admin
    | Home
    | Login
    | NotFound of url: string
    | ProductIndex of filters: Filters
    | ProductDetail of SKU
```

Each case carries its own parameters. Notably, `ProductIndex` embeds a full `Filters` record (search term, sorting, category filters...) and `ProductDetail` carries a strongly-typed `SKU`.

## Entry point: `React.router`

In `View.fs`, the top-level `AppView` component wires everything together using `React.router`:

```fsharp
React.router [
    router.pathMode
    router.onUrlChanged (Page.parseFromUrlSegments >> UrlChanged >> dispatch)
    router.children [ navbar; content; toast ]
]
```

Three things happen here:

1. **Path mode** — The router uses the URL path (not hash-based routing).
2. **URL change listener** — When the browser URL changes, `Page.parseFromUrlSegments` converts the raw segments into a typed `Page` value, which is then dispatched as an `UrlChanged` message into the Elmish loop.
3. **Children rendering** — The navbar, page content, and toast notifications are rendered as children of the router.

The model stores the current `Page`, and the view pattern-matches on it to render the appropriate page component:

```fsharp
let pageView =
    match pageToDisplayInline with
    | Page.About -> Pages.About.AboutView env
    | Page.Admin -> Pages.Admin.AdminView env
    | Page.Login -> Pages.Login.LoginView env
    | Page.ProductDetail sku -> Pages.Product.Details.Page.ProductDetailsView env sku
    | Page.ProductIndex filters -> Pages.Product.Index.Page.ProductIndexView env filters
    | ...
```

## Navigating between pages

Two helpers are defined in `Routing.fs` to trigger navigation, depending on the context:

### From a view: `Router.navigatePage`

When navigation is triggered by a user interaction (e.g. a button click), the view calls:

```fsharp
Router.navigatePage Page.About
```

This helper converts the `Page` into URL segments through the `(|PageUrl|)` active pattern (details below), then delegates to `Feliz.Router`'s `Router.navigatePath`:

```fsharp
module Router =
    let navigatePage (PageUrl pageUrl) =
        Router.navigatePath (pageUrl.Segments, queryString = pageUrl.Query)
```

### From an `update` function: `Cmd.navigatePage`

When navigation must happen as an Elmish side effect (e.g., redirecting after login), the `update` function returns:

```fsharp
Cmd.navigatePage Page.ProductIndexDefaults
```

This is the `Cmd` counterpart, producing an Elmish command that triggers the same `navigatePath` call:

```fsharp
module Cmd =
    let navigatePage (PageUrl pageUrl) =
        Cmd.navigatePath (pageUrl.Segments, queryString = pageUrl.Query)
```

Both helpers share the same `(|PageUrl|)` conversion, ensuring a single source of truth for URL generation.

### In anchor elements: `prop.hrefRouted`

For `<a>` links that need both a visible `href` (for accessibility and right-click "copy link") and SPA navigation (preventing full page reload), a helper extension is defined:

```fsharp
type prop with
    static member inline hrefRouted (PageUrl pageUrl) = [
        prop.href (Router.formatPath (pageUrl.Segments, queryString = pageUrl.Query))
        prop.onClick Router.goToUrl
    ]
```

It sets the `href` attribute for the browser and intercepts the click event via `Router.goToUrl` to perform client-side navigation instead. This is used extensively in the navbar breadcrumbs and filter tabs.

```fsharp
Html.a [
    prop.key $"nav-link-%s{page.Key}"
    prop.text text
    yield! prop.hrefRouted page
]
```

## Navbar: breadcrumb-based navigation

The `AppNavBar` component renders a DaisyUI breadcrumb trail that adapts to the current page. A private `Nav` class encapsulates the breadcrumb item rendering logic:

- If the item corresponds to the current page, it renders as plain text (no link).
- Otherwise, it renders as an anchor using `prop.hrefRouted`.

```fsharp
type private Nav(currentPage, ...) =
    // ...
    member private nav.page(page: Page, text: string, ?cssClass: string) =
        if String.IsNullOrWhiteSpace(text) then
            Html.none
        else
            Html.li [
                prop.key $"nav-%s{page.Key}"
                prop.className [
                    match cssClass with
                    | Some css -> css
                    | None -> ()
                ]
                prop.children [
                    if page = currentPage then
                        Html.text text
                    else
                        Html.a [
                            prop.key $"nav-link-%s{page.Key}"
                            prop.text text
                            yield! prop.hrefRouted page
                        ]
                ]
            ]

    // ...
```

The breadcrumb structure reflects the page hierarchy. For example, a `ProductDetail` page for a book shows: **Home > Products > Books > OL12345M**.

```fsharp
type private Nav(..., translations: AppTranslations) =
    let translate =
        if translations.IsEmpty then
            fun _ -> String.Empty
        else
            fun f -> f translations

    // ...

    member nav.Home = nav.page (Page.Home, "🛍️ Shopfoo")
    member nav.About = nav.page (Page.About, translate _.Home.About)
    member nav.Admin = nav.page (Page.Admin, translate _.Home.Admin)
    member nav.Login = nav.page (Page.Login, translate _.Home.Login)
    member nav.Products = nav.page (Page.ProductIndexDefaults, translate _.Home.Products)
    member nav.Bazaar = nav.page (Page.ProductIndexDefaultsWith _.ToBazaar(), translate _.Home.Bazaar)
    member nav.Books = nav.page (Page.ProductIndexDefaultsWith _.ToBooks(), translate _.Home.Books)
    member nav.Product sku = nav.page (Page.ProductDetail sku, text = sku.Value, cssClass = "font-semibold")

[<ReactComponent>]
let AppNavBar key currentPage pageDisplayedInline translations children =
    let nav = Nav(currentPage, translations)

    Daisy.navbar [
        Daisy.breadcrumbs [
            Html.ul [
                nav.Home
                match pageDisplayedInline with
                | Page.Home
                | Page.NotFound _ -> ()
                | Page.About -> nav.About
                | Page.Admin -> nav.Admin
                | Page.Login -> nav.Login
                | Page.ProductIndex { CategoryFilters = None } -> nav.Products

                | Page.ProductIndex { CategoryFilters = Some(CategoryFilters.Bazaar _) } ->
                    nav.Products
                    nav.Bazaar

                | Page.ProductIndex { CategoryFilters = Some(CategoryFilters.Books _) } ->
                    nav.Products
                    nav.Books

                | Page.ProductDetail sku ->
                    nav.Products

                    match sku.Type with
                    | SKUType.FSID _ -> nav.Bazaar
                    | SKUType.ISBN _ -> nav.Books
                    | SKUType.OLID _ -> nav.Books
                    | SKUType.Unknown -> ()

                    nav.Product sku
            ]
        ]

        yield! children
    ]
```

Additional controls (user dropdown, language selector, theme switcher, admin gear icon, about icon) are injected as `children` of the navbar from the `AppView`.

```fsharp
AppNavBar "app-nav" model.Page pageToDisplayInline translations [
    match fullContext.User with
    | User.Anonymous -> ()
    | User.LoggedIn(userName, _) -> UserDropdown "nav-user" userName translations logout

    LangDropdown "nav-lang" fullContext.Lang model.LangMenus startChangeLang
    ThemeDropdown "nav-theme" model.Theme translations onThemeChanged

    if not translations.IsEmpty then
        if fullContext.User.CanAccess Feat.Admin then
            Daisy.button.button [
                // ...
                prop.title translations.Home.Admin
                prop.onClick (fun _ -> Router.navigatePage Page.Admin)
            ]

        Daisy.button.button [
            // ...
            prop.title translations.Home.About
            prop.onClick (fun _ -> Router.navigatePage Page.About)
        ]
]
```

## URL encoding and decoding

### Encoding: `(|PageUrl|)` active pattern

The `(|PageUrl|)` active pattern converts a `Page` value into a `PageUrl` record containing URL segments and query parameters:

```fsharp
let (|PageUrl|) = // Page -> PageUrl
    function
    | Page.About -> PageUrl.WithSegments("about")
    | Page.ProductIndex({ CategoryFilters = Some(CategoryFilters.Books(authorId, tag)) } as filters) ->
        PageUrl
            .WithSegments("books")
            .WithQueryParamOptional("author", authorId |> Option.map (...))
            .WithQueryParamOptional("tag", tag)
            .WithFiltersQueryParams(filters)
    | ...
```

The `PageUrl` type offers a fluent API (`WithQueryParam`, `WithQueryParamOptional`, `WithFiltersQueryParams`) to build the query string incrementally. Only non-default values are serialized — for instance, `Highlighting.Active` (the default) produces no query parameter, while `Highlighting.None` serializes as `highlight=no`.

### Decoding: `Page.parseFromUrlSegments`

URL segments are parsed back into a `Page` using pattern matching:

```fsharp
let parseFromUrlSegments segments =
    match segments with
    | [] -> Page.Home
    | [ "about" ] -> Page.About
    | [ "bazaar"; Route.SKU sku ] -> Page.ProductDetail sku
    | [ "books"; Route.Query(Route.Filters changeFilters & Route.Author authorId & Route.Tag tag) ] ->
        Page.ProductIndexDefaultsWith (fun filters ->
            { changeFilters filters with CategoryFilters = Some(CategoryFilters.Books(authorId, tag)) })
    | _ -> Page.NotFound (Router.formatPath segments)
```

This function leverages a layered system of active patterns (detailed in the next section).

### Roundtrip property

A property-based test verifies that encoding and decoding are inverse operations:

```fsharp
member _.``roundtrip page`` (sanitizedPage: SanitizedPage) =
    let inputPage = sanitizedPage.Value
    let (PageUrl pageUrl) = inputPage
    let parsedPage = Page.parseFromUrlSegments pageUrl.SegmentsWithQueryString
    parsedPage =! inputPage
```

The test does not exercise arbitrary `Page` values — it uses `SanitizedPage`, a record type that constrains all string fields (SKU values, tags, author IDs, search terms...) to an `AlphaNumString` type. Its FsCheck generator is designed to produce 1 to 32 alphanumeric characters (`a-z`, `A-Z`, `0-9`), avoiding special characters that would break URL encoding or query string parsing. Since real-world pages are a subset of these sanitized pages (actual SKUs, tags, and search terms only contain alphanumeric characters), the test remains valid and guarantees that any realistic `Page` value survives a roundtrip through URL serialization and parsing.

## Deep dive: active pattern composition for URL parsing

The URL parsing in `Routing.fs` is a good example of how F# active patterns can be composed to build a clean, declarative parser. Several layers of active patterns work together, each handling a specific concern.

### Primitive helpers

At the base level, small utility patterns extract raw values:

| Pattern           | Purpose                                              |
| ----------------- | ---------------------------------------------------- |
| `(⏐Dashed⏐)`      | Splits a string on `-` into segments (total pattern) |
| `(⏐Param⏐_⏐) key` | Extracts the value of a query parameter by key       |
| `(⏐YesNo⏐_⏐)`     | Recognizes `"yes"` / `"no"` as `bool`                |
| `(⏐Col⏐_⏐)`       | Recognizes a column key like `"name"` as a `Column`  |
| `(⏐Desc⏐_⏐)`      | Recognizes the `"desc"` token                        |

### Domain-level patterns

These compose the primitives into domain-meaningful extractions:

| Pattern          | Input         | Output                                                                                 |
| ---------------- | ------------- | -------------------------------------------------------------------------------------- |
| `(⏐SKU⏐_⏐)`      | Route segment | `SKU option` — recognizes `"FS-42"`, `"BN-978..."`, `"OL123M"`                         |
| `(⏐Highlight⏐)`  | Query params  | `Highlighting` — reads `highlight` param                                               |
| `(⏐MatchCase⏐)`  | Query params  | `CaseMatching` — reads `matchCase` param                                               |
| `(⏐SearchTerm⏐)` | Query params  | `string option` — reads `search` param                                                 |
| `(⏐Sort⏐)`       | Query params  | `(Column * SortDirection) option` — parses `"name-desc"` via `Dashed` + `Col` + `Desc` |
| `(⏐Category⏐)`   | Query params  | `BazaarCategory option`                                                                |
| `(⏐Author⏐)`     | Query params  | `OLID option`                                                                          |
| `(⏐Tag⏐)`        | Query params  | `string option`                                                                        |

### Composite pattern: `(|Filters|)`

The `(|Filters|)` pattern combines four sub-patterns at once using the `&` (AND) combinator:

```fsharp
let (|Filters|) queryParams : Filters -> Filters =
    match queryParams with
    | Highlight highlighting & MatchCase caseMatching & SearchTerm searchTerm & Sort sortBy ->
        fun filters -> {
            filters with
                SortBy = sortBy
                Search = {
                    filters.Search with
                        CaseMatching = caseMatching
                        Highlighting = highlighting
                        Term = searchTerm
                }
        }
```

It returns a _function_ (`Filters -> Filters`) rather than a `Filters` value. This allows callers to apply it on top of default filters, which is how `parseFromUrlSegments` works:

```fsharp
| [ "books"; Route.Query(Route.Filters changeFilters & Route.Author authorId & Route.Tag tag) ] ->
    Page.ProductIndexDefaultsWith (fun filters ->
        { changeFilters filters with CategoryFilters = Some(CategoryFilters.Books(authorId, tag)) })
```

Here, `Route.Query` extracts the query string, then `Route.Filters`, `Route.Author`, and `Route.Tag` are all applied simultaneously via `&`, each extracting its own concern from the same query parameters.

### What makes this approach elegant

This layered active pattern design achieves several things:

1. **Separation of concerns** — Each pattern handles exactly one aspect (one query param, one URL segment format). They are small, testable, and reusable.
2. **Declarative composition** — The `&` combinator lets you combine multiple extractions in a single `match` arm without nested conditionals or manual parameter lookups.
3. **Defaults via totality** — Most domain-level patterns are _total_ (not partial): they always return a value, falling back to a sensible default (e.g. `CaseInsensitive`, `Highlighting.Active`). This means unrecognized or missing query params silently resolve to defaults, avoiding errors.
4. **Functional transformation** — `(|Filters|)` returning a function rather than a flat value enables a clean pipeline where category-specific parameters can be layered on top separately.
5. **Symmetry with encoding** — The key constants (e.g. `"highlight"`, `"matchCase"`) appear in both the `(|Param|_|)` calls (decoding) and the `WithQueryParamOptional` calls (encoding), making the roundtrip relationship obvious and easy to maintain.

The overall result is that `parseFromUrlSegments` reads almost like a route table declaration — each `match` arm maps a URL pattern to a `Page` case, with the active patterns handling all the parsing details behind the scenes.

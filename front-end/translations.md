---
icon: globe
---

# Translations

## Overview

*Shopfoo* uses a custom, type-safe localization system. Translations are organized by page, loaded lazily alongside API responses, and cached in the application's global state. The system avoids a separate round-trip for translations by piggybacking them on existing data queries.

The key files are:

- **`Shopfoo.Domain.Types/Translations.fs`** — Core types: `PageCode`, `TagCode`, `TranslationKey`, and the `Translations` data structure.
- **`Shopfoo.Shared/Translations.fs`** — The `AppTranslations` facade and page-specific translator classes (`Home`, `Login`, `Product`).
- **`Shopfoo.Shared/Remoting.fs`** — `FullContext` with `FillTranslations`, `QueryDataAndTranslations`, and the `QueryWithTranslations` type alias.
- **`Shopfoo.Client/View.fs`** — The Elmish loop that owns the translation cache and handles the dispatched `FillTranslations` messages.
- **`Shopfoo.Client/Pages/Shared.fs`** — The `Env.IFillTranslations` interface that pages use to push translations into the cache.

## Core types

### `PageCode`, `TagCode`, and `TranslationKey`

Every translation entry is identified by a composite key: which page it belongs to and a tag within that page.

```fsharp
[<RequireQualifiedAccess>]
type PageCode =
    | Home
    | Login
    | Product

type TagCode = TagCode of code: string

type TranslationKey = { Page: PageCode; Tag: TagCode }
```

`PageCode` partitions the translations into page-scoped namespaces. `TagCode` is a single-case union wrapping a string key — for example `"Login"`, `"Save"`, `"Theme.Dark"`, or `"PriceAction.Define"`. Together they form a `TranslationKey` that uniquely identifies a translated string.

### The `Translations` record

The raw translation data is a nested map:

```fsharp
type Translations = {
    Pages: Map<PageCode, Map<TagCode, string>>
}
```

- **Outer map:** `PageCode` → inner map (one entry per page).
- **Inner map:** `TagCode` → translated string.

Two lookup members provide access:

```fsharp
member x.GetOrNone(key) =
    x.Pages
    |> Map.tryFind key.Page
    |> Option.bind (Map.tryFind key.Tag)

member x.Get(key, ?defaultValue) =
    x.GetOrNone(key)
    |> Option.orElse defaultValue
    |> Option.defaultWith (fun () -> fallbackText key)
```

When a key is missing and no default is provided, `Get` returns a visible fallback string like `[*** Product.Price ***]`, making missing translations immediately obvious during development.

## Page translators

### The `Base` class and page-specific classes

The `TranslationPages` module defines a `Base` class and three sealed subclasses — `Home`, `Login`, `Product` — one per `PageCode`. Each class wraps the raw map lookup behind named, strongly-typed properties:

```fsharp
type Home internal (?translations) =
    inherit Base(PageCode.Home, ?translations = translations)

    member this.About = this.Get "About"
    member this.Admin = this.Get "Admin"
    member this.Save  = this.Get "Save"
    member this.Error(error: string) = this.Format("Error", error)
    // ...
```

This gives call sites a discoverable, compile-checked API — `translations.Home.Save` instead of a raw string lookup like `translations.Get({ Page = Home; Tag = TagCode "Save" })`. IntelliSense lists all available keys for each page, and a typo in a property name is a compile error rather than a silent missing translation at runtime.

### `WithPrefix` for nested keys

Some pages group related keys under a dot-separated prefix. The `WithPrefix` method on `Base` creates a scoped sub-translator that automatically prepends the prefix to every lookup:

```fsharp
member this.PriceAction =
    this.WithPrefix "PriceAction."
    <| fun this -> {|
        Define = this.Get "Define"           // looks up "PriceAction.Define"
        Remove = this.Get "Remove"           // looks up "PriceAction.Remove"
        MarkAsSoldOutDialog =
            this.WithPrefix "MarkAsSoldOutDialog."
            <| fun this -> {|
                Confirm = this.Get "Confirm" // looks up "PriceAction.MarkAsSoldOutDialog.Confirm"
                Question = this.Get "Question"
            |}
    |}
```

The result is an anonymous record, so call sites read naturally: `translations.Product.PriceAction.MarkAsSoldOutDialog.Confirm`.

The access path through `AppTranslations` and the actual `TagCode` stored in the data are technically decoupled — nothing in the compiler enforces that they match. However, it is strongly recommended to keep them aligned to avoid confusion. In the example above, the correspondence is:

- **Access path:** `translations.Product` `.PriceAction` `.MarkAsSoldOutDialog` `.Confirm`
- **`WithPrefix` composition:** `"PriceAction."` + `"MarkAsSoldOutDialog."` + `"Confirm"`
- **Stored key:** `TagCode "PriceAction.MarkAsSoldOutDialog.Confirm"`

Notice the trick in the definition of `member this.PriceAction`: the lambda parameter is also named `this`, shadowing the outer `this` from the class member. Both are of the same type (`Base`), but the inner `this` is a **new instance** whose `buildTagCode` function prepends the prefix. This shadowing is intentional — it lets the body of the lambda read exactly like a normal class member (`this.Get "Define"`), while the tag resolution silently includes the accumulated prefix.

Here is the implementation of `WithPrefix`:

```fsharp
type Base internal (pageCode: PageCode, ?translations: Translations, ?buildTagCode: string -> TagCode) =
    let buildTagCode = defaultArg buildTagCode TagCode
    // ...

    member internal this.WithPrefix (tagPrefix: string) (useNesting: Base -> _) =
        let newBase = Base(pageCode, translations, buildTagCode = (fun tag -> buildTagCode $"{tagPrefix}{tag}"))
        useNesting newBase
```

The prefix is captured in a new `buildTagCode` lambda that composes with the existing one: it prepends the prefix string before delegating to the parent's `buildTagCode`. When nesting is two levels deep (e.g. `PriceAction.MarkAsSoldOutDialog.Confirm`), each `WithPrefix` call wraps the previous `buildTagCode`, accumulating prefixes through function composition rather than string concatenation at each lookup.

### `Format` for parameterized messages

Some translations are parameterized. They rely on the `Format` method of `Base`, which wraps `String.Format`:

```fsharp
member this.SavedOk(name: string) = this.Format("SavedOk", name)
member this.AuthorSearchLimit(limit: int, totalFound: int) = this.Format("AuthorSearchLimit", limit, totalFound)
```

The underlying format strings use `{0}`, `{1}` placeholders. For instance, `AuthorSearchLimit` resolves to `"{1} authors found but only {0} are displayed"` in English. The benefit is that the parameter count and types are explicit at compile time — a caller cannot forget an argument or pass the wrong type — even though the format string itself is only resolved at runtime.

### Translation data

The actual translation values are stored in `Shopfoo.Home/Data/Translations.fs` as a simple F# list of tuples `(TagCode, english, french)`, grouped by `PageCode`:

```fsharp
let private translationPages = [
    PageCode.Home,
    [
        TagCode "About", "About", "À propos"
        TagCode "Admin", "Admin", "Administration"
        TagCode "Save", "Save", "Enregistrer"
        TagCode "Error", "Error: {1}", "Erreur : {1}"
        TagCode "SavedOk", "{0} saved successfully", "{0} enregistré avec succès"
        // ...
    ]

    PageCode.Login,
    [
        TagCode "Access.Edit", "Edit", "Édition"
        TagCode "Feat.Catalog", "Catalog", "Catalogue"
        TagCode "SelectPersona", "Select a persona based on access rights", "Choisir un persona en fonction des droits d'accès"
        // ...
    ]

    PageCode.Product,
    [
        TagCode "Name", "Name", "Nom"
        TagCode "Price", "Price", "Prix"
        TagCode "AuthorSearchLimit", "{1} authors found but only {0} are displayed", "{1} auteurs trouvés mais seuls {0} sont affichés"
        TagCode "PriceAction.Define", "Define", "Définir"
        TagCode "PriceAction.MarkAsSoldOutDialog.Confirm", "Yes, mark as sold out", "Oui, marquer comme épuisé"
        TagCode "StoreCategory.Clothing", "Clothing", "Vêtements"
        // ...
    ]
]
```

A `repository` map is then built by projecting the appropriate column for each language:

```fsharp
let repository =
    Map [
        Lang.English, mapTranslationPages (fun (tagCode, en, _) -> tagCode, en)
        Lang.French,  mapTranslationPages (fun (tagCode, _, fr) -> tagCode, fr)
    ]
```

This design keeps all translations in a single file, side by side, making it easy to spot missing translations or inconsistencies between languages. The dot-separated keys (e.g. `"PriceAction.MarkAsSoldOutDialog.Confirm"`) are stored flat in the map — the hierarchical nesting is reconstructed on the client side by the `WithPrefix` mechanism described above.

{% hint style="info" %}
In a production application, translations would typically be stored in an external database or a dedicated localization service rather than in a source file. Here, the in-code approach keeps the demo self-contained and avoids an external dependency.
{% endhint %}

## `AppTranslations` — the facade

`AppTranslations` aggregates the three page translators and tracks which pages have been populated:

```fsharp
type AppTranslations private (home: Home, login: Login, product: Product, ?translations) =
    new() = AppTranslations(Home(), Login(), Product())

    member val Home = home
    member val Login = login
    member val Product = product

    member val EmptyPages = pageCodes _.IsEmpty        // Set<PageCode>
    member val PopulatedPages = pageCodes (not << _.IsEmpty)  // Set<PageCode>
    member this.IsEmpty = this.PopulatedPages.IsEmpty

    member _.Fill(translations: Translations) =
        AppTranslations(
            recreatePageIfNeeded home (fun () -> Home translations),
            recreatePageIfNeeded login (fun () -> Login translations),
            recreatePageIfNeeded product (fun () -> Product translations),
            translations
        )
```

Key characteristics:

- **Immutable** — `Fill` returns a new `AppTranslations` instance; the original is unchanged.
- **Lazy recreation** — Only page translators whose `PageCode` appears in the incoming `Translations` map are recreated. Pages that already have data and receive no new data keep their existing translator instance.
- **`EmptyPages` / `PopulatedPages`** — Two computed sets that partition the `PageCode` values based on whether the page translator has any data. These sets drive the piggyback loading mechanism (see below).

## Translation cache in `FullContext`

The application's global state is held in `FullContext`, which stores the current `AppTranslations`:

```fsharp
[<RequireQualifiedAccess>]
type FullContext = {
    Lang: Lang
    User: User
    Token: AuthToken option
    Translations: AppTranslations
}
```

Two members manage the cache:

```fsharp
member this.FillTranslations(translations: Translations) =
    { this with Translations = this.Translations.Fill translations }

member this.ResetTranslations() =
    { this with Translations = AppTranslations() }
```

`FillTranslations` merges incoming translations into the existing cache. `ResetTranslations` clears everything (used on logout).

## Loading translations: the piggyback pattern

Translations are not fetched through a dedicated API call at startup. Instead, they are piggybacked onto the first data query each page makes.

### `QueryWithTranslations`

The shared contract defines a query variant that carries translation metadata alongside the domain payload:

```fsharp
type QueryDataAndTranslations<'query> = {
    Query: 'query
    TranslationPages: PageCode Set  // pages the client is missing
}

type QueryWithTranslations<'query, 'response> =
    Query<QueryDataAndTranslations<'query>, 'response * Translations>
```

The client sends the set of `PageCode`s it still lacks, and the server returns the corresponding translations alongside the response data:

```fsharp
type HomeApi = {
    Index: QueryWithTranslations<unit, HomeIndexResponse>
    GetTranslations: Query<GetTranslationsRequest, GetTranslationsResponse>
}
```

`Index` is a `QueryWithTranslations` — translations are piggybacked on the initial data load. `GetTranslations` is a plain `Query` used when the user switches the display language from the UI: the client already has data, but needs all populated pages re-translated in the new language (see [Language switching](#language-switching) below).

### `PrepareQueryWithTranslations`

On the client, `FullContext.PrepareQueryWithTranslations` automatically includes the empty pages:

```fsharp
member this.PrepareQueryWithTranslations query =
    this.PrepareRequest { Query = query; TranslationPages = this.Translations.EmptyPages }
```

This is typically called once per page, in `init`:

```fsharp
let private init (env: #Env.IFullContext) =
    initialModel,
    Cmd.loadHomeData (env.FullContext.PrepareQueryWithTranslations())
```

Subsequent calls from the same page use `PrepareRequest` (no translations needed — they are already cached).

### Server side

The server receives the `TranslationPages` set and returns only the missing pages. The handler delegates to the domain API, which filters translations by the user's authorized pages and the requested language:

```fsharp
let! translations =
    api.Home.GetAllowedTranslations {
        lang = lang
        allowed = authorizedPageCodes
        requested = request.TranslationPages
    }
let response = ResponseBuilder.withTranslations user translations
```

`ResponseBuilder.withTranslations` tacks the `Translations` bundle onto the success value. The client then unpacks the tuple and fills the cache.

## How pages update the cache

Pages do not own the translation cache — it lives in the root `AppView` Elmish loop. Pages communicate upward through the `Env.IFillTranslations` interface:

```fsharp
module Env =
    type IFillTranslations =
        abstract member FillTranslations: Translations -> unit
```

When a page receives data from the server, its `update` function pushes the translations into the cache:

```fsharp
let private update (env: #Env.IFillTranslations) msg model =
    match msg with
    | Msg.HomeDataFetched(Ok(data, translations)) ->
        { model with Personas = Remote.Loaded data.Personas },
        Cmd.ofEffect (fun _ -> env.FillTranslations translations)

    | Msg.HomeDataFetched(Error apiError) ->
        { model with Personas = Remote.LoadError apiError },
        Cmd.ofEffect (fun _ -> env.FillTranslations apiError.Translations)
```

For more details on the `Env` pattern and how child pages communicate with the root `AppView`, see [Data flow — The `Env` pattern](elmish/data-flow.md#the-env-pattern).

{% hint style="info" %}
Even error responses can carry translations — `ApiError` has a `Translations` field. This ensures that error messages can be displayed in the user's language even if the main data load fails.
{% endhint %}

In `View.fs`, the root Elmish loop handles the `FillTranslations` message by merging the incoming data into the global cache:

```fsharp
| Msg.FillTranslations translations ->
    { model with FullContext = model.FullContext.FillTranslations(translations) }, Cmd.none
```

## Language switching

When the user changes the display language, the root `AppView` fetches translations for all pages that were previously populated — there is no need to re-fetch pages that were never loaded:

```fsharp
module private Cmd =
    let fetchTranslations (cmder: Cmder, request: Request<GetTranslationsRequest>) =
        let lang = request.Body.Lang // Warning: `request.Lang` is the current lang, not the requested one

        cmder.ofApiRequest {
            Call = fun api -> api.Home.GetTranslations request
            Error = fun err -> ChangeLang(lang, Done(Error err))
            Success = fun data -> ChangeLang(lang, Done(Ok data))
        }

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let updateLangStatus lang status = [
        for menu in model.LangMenus do
            if menu.Lang = lang then
                { menu with Status = status }
            else
                menu
    ]

    match msg with
    | Msg.ChangeLang(lang, Start) ->
        { model with LangMenus = updateLangStatus lang Remote.Loading },
        Cmd.fetchTranslations (
            model.FullContext.PrepareRequest { Lang = lang; PageCodes = model.FullContext.Translations.PopulatedPages }
        )

    | Msg.ChangeLang(lang, Done(Ok data)) ->
        let fullContext = {
            model.FullContext with
                Lang = lang
                Translations = AppTranslations().Fill(data.Translations)
        }
        { model with FullContext = fullContext; LangMenus = updateLangStatus lang (Remote.Loaded()) },
        Cmd.batch [ ... ]

    ...
```

Notice that on success, translations are built from scratch (`AppTranslations().Fill(...)`) rather than merged into the existing cache. This ensures a clean replacement — no stale strings from the previous language leak through.

## Consuming translations in views

### Accessing translations

Views access translations through the `Env` interface:

```fsharp
[<ReactComponent>]
let XxxView (env: #Env.IFullContext) =
    let translations = env.Translations
    ...
    Html.span [
        prop.key "login-legend-text"
        prop.className "ml-2"
        prop.text translations.Home.Login
    ]
    ...
    Html.div [
        prop.key "discount"
        prop.className "ml-auto"
        prop.text $"%s{translations.Product.Discount}%s{translations.Home.Colon} %.0f{discount.Value}%%"
    ]
    ...
    Html.text $" (%s{translations.Home.FormatDate(date, standardDateFormat)})"
    ...
    SearchCompletionStatus.Info(translations.Product.AuthorSearchLimit(limit = data.Authors.Count, totalFound = data.TotalCount))
```

### `TranslationsMissing` active pattern

For views that must wait for a specific page's translations before rendering, the `TranslationsMissing` partial active pattern acts as a guard:

```fsharp
let (|TranslationsMissing|_|) pageCode (translations: AppTranslations) =
    translations.PopulatedPages.Contains pageCode |> not |> Option.ofBool
```

Usage in a view:

```fsharp
match model.Prices, translations with
| Remote.Empty, _ -> ()
| Remote.Loading, _
| _, TranslationsMissing PageCode.Product -> Daisy.skeleton [ ... ]
| Remote.LoadError apiError, _ -> Alert.apiError "prices-load-error" apiError fullContext.User
| Remote.Loaded prices, _ -> // Render content with translations
```

This pattern treats missing translations the same as loading data — both show a skeleton. Once both the data and translations are available, the view renders the full content.

## Data flow summary

The full lifecycle of translations follows this path:

1. **Page `init`** — Calls `PrepareQueryWithTranslations`, which includes the set of `PageCode`s with empty translations (`EmptyPages`).
2. **Client `Cmd`** — The remoting call carries the `TranslationPages` set to the server.
3. **Server handler** — Returns the requested translations alongside the response data, filtered by the user's authorized pages and the requested language.
4. **Page `update`** — Receives `(data, translations)` and dispatches `env.FillTranslations translations`.
5. **Root `AppView` update** — Handles `Msg.FillTranslations` by calling `FullContext.FillTranslations`, which merges translations into the cache via `AppTranslations.Fill`.
6. **Views** — Access translations through `AppTranslations.Home`, `.Login`, `.Product` properties. The `TranslationsMissing` pattern guards against rendering before translations are available.
7. **Language change** — Fetches translations for `PopulatedPages` only, replaces the entire cache with the new language's data.

## Recipe: adding a new translation

### Case 1 — Adding a key to an existing `PageCode`

Two files to edit:

1. **`Shopfoo.Home/Data/Translations.fs`** — Add a new tuple in the appropriate `PageCode` group:

   ```fsharp
   TagCode "MyNewKey", "English value", "French value"
   ```

   For a parameterized message, use `{0}`, `{1}`... placeholders:

   ```fsharp
   TagCode "MyNewKey", "{0} items remaining", "{0} éléments restants"
   ```

2. **`Shopfoo.Shared/Translations.fs`** — Add a member to the corresponding page translator class (e.g. `Home`, `Login`, or `Product`):

   ```fsharp
   member this.MyNewKey = this.Get "MyNewKey"
   ```

   For a parameterized message:

   ```fsharp
   member this.MyNewKeyWithParam(count: int) = this.Format("MyNewKeyWithParam", count)
   ```

   For a nested key, add it inside a `WithPrefix` block, or create a new one.

The view can then use `translations.Product.MyNewKey` (or `translations.Product.MyNewKeyWithParam(42)`). No other wiring is needed — the key is automatically included in the `Translations` map for the page and flows through the existing piggyback and cache mechanisms.

### Case 2 — Adding a new `PageCode`

This is a more involved change that touches several files across the stack:

1. **`Shopfoo.Domain.Types/Translations.fs`** — Add a new case to the `PageCode` union:

   ```fsharp
   type PageCode =
       | Home
       | Login
       | NewPage  // 👈
       | Product
   ```

2. **`Shopfoo.Home/Data/Translations.fs`** — Add a new `PageCode.NewPage` group with its translation tuples.

3. **`Shopfoo.Shared/Translations.fs`** — Create a new translator class inheriting from `Base`, then integrate it into `AppTranslations`:

   ```fsharp
   // 1. New translator class in the TranslationPages module
   type NewPage internal (?translations) =
       inherit Base(PageCode.NewPage, ?translations = translations)
       member this.NewKey = this.Get "NewKey"

   // 2. Update AppTranslations: constructor, property, sections, and Fill
   type AppTranslations
       private
       (
           home: Home,
           login: Login,
           newPage: NewPage,   // 👈
           product: Product,
           ?translations
       ) =
       let sections = [
           Section.Home, home :> Base
           Section.Login, login
           Section.NewPage, newPage  // 👈
           Section.Product, product
       ]

       new() = AppTranslations(Home(), Login(), NewPage(), Product())
       //                                       👆

       member val NewPage = newPage  // 👈

       member _.Fill(translations: Translations) =
           AppTranslations(
               recreatePageIfNeeded home (fun () -> Home translations),
               recreatePageIfNeeded login (fun () -> Login translations),
               recreatePageIfNeeded newPage (fun () -> NewPage translations),  // 👈
               recreatePageIfNeeded product (fun () -> Product translations),
               translations
           )
   ```

4. **`Shopfoo.Client/Pages/Shared.fs`** — No change needed — the `Env.IFillTranslations` interface is generic over `Translations`, not `PageCode`.

5. **Server `{Area}ApiBuilder`** — Each API builder defines a static `pages` set that lists the `PageCode`s its handlers are allowed to return. Add the new `PageCode` to the relevant builder(s):

   ```fsharp
   // In HomeApiBuilder — serves the initial page load (Login)
   [<Sealed>]
   type HomeApiBuilder(api: FeatApi) =
       static let pages =
           Set [
               PageCode.Home
               PageCode.Login
               PageCode.NewPage  // 👈
           ]

       member _.Build() : HomeApi = {
           Index = IndexHandler(api, pages) |> Security.authorizeHandler Claims.none
           //                        👆
           ...
       }

   // In CatalogApiBuilder — serves the product pages
   [<Sealed>]
   type CatalogApiBuilder(api: FeatApi) =
       static let pages =
           Set [
               PageCode.Home
               PageCode.Login
               PageCode.NewPage   // 👈 if needed by product pages too
               PageCode.Product
           ]

       member _.Build() : CatalogApi = {
           GetProducts = GetProductsHandler(api, pages) |> Security.authorizeHandler (claim Access.View)
           // ...
       }
   ```

   This `pages` set is passed as `authorizedPageCodes` to each `SecureQueryDataAndTranslationsHandler`. If you forget to add the new `PageCode` here, the translations will simply not be returned — the client will keep showing skeleton placeholders for that page.

{% hint style="warning" %}
Adding a new `PageCode` requires updating `AppTranslations` — a type shared between client and server. Make sure to rebuild both sides after the change. The compiler will guide you: any missing match arm on the new `PageCode` case will produce a warning.
{% endhint %}

## Recipe: localized date formatting

Date formatting is a good example of how the translation system handles locale-sensitive rendering without relying on `DateTime.ToString` or browser locale APIs. The format, the month abbreviations, and the ordinal day suffixes are all driven by translation keys.

### Translation data (bis)

The `Home` page translations include three groups of keys:

- **`StandardDateFormat`** — A format template describing the order of date parts. In English: `"ShortMonth DayInMonth, Year"` (e.g. "Mar 5th, 2026"). In French: `"DayInMonth ShortMonth Year"` (e.g. "5 Mar 2026").
- **`ShortMonth.Jan`** through **`ShortMonth.Dec`** — Abbreviated month names. English: `"Jan"`, `"Feb"`... French: `"Jan"`, `"Fév"`...
- **`DayInMonth.1st`** through **`DayInMonth.31st`** — Ordinal day representations. English: `"1st"`, `"2nd"`, `"3rd"`... French: `"1er"`, `"2"`, `"3"`...

### Parsing the format template

The `StandardDateFormat` property on the `Home` translator parses the format string into a list of `DatePartFormat` tokens:

```fsharp
type DatePartFormat =
    | DayInMonth
    | MonthShortName
    | Year
    | Separator of string

member this.StandardDateFormat: DatePartFormat list = [
    for part in Regex.Split(input = this.Get("StandardDateFormat"), pattern = @"(DayInMonth|DDD|ShortMonth|MMM|Year|YYYY)") do
        match part with
        | ("DDD" | "DayInMonth") -> DayInMonth
        | ("MMM" | "ShortMonth") -> MonthShortName
        | ("YYYY" | "Year") -> Year
        | separator -> Separator separator
]
```

For English (`"ShortMonth DayInMonth, Year"`), this produces: `[MonthShortName; Separator " "; DayInMonth; Separator ", "; Year]`.

For French (`"DayInMonth ShortMonth Year"`), this produces: `[DayInMonth; Separator " "; MonthShortName; Separator " "; Year]`.

### Rendering the date

The `FormatDate` method concatenates the parts by resolving each token against the translation keys:

```fsharp
member this.FormatDate(date: DateOnly, format: DatePartFormat list) =
    String.concat "" [
        for part in format do
            match part with
            | DayInMonth -> this.DayInMonth date.Day        // "5th" or "5"
            | MonthShortName -> this.ShortMonth date.Month  // "Feb" or "Fév"
            | Year -> string date.Year                      // "2026"
            | Separator sep -> sep                          // " " or ", "
    ]
```

### Usage in views

The view calls `FormatDate` with the pre-parsed format (typically cached in the component to avoid re-parsing on every render):

```fsharp
let standardDateFormat = translations.Home.StandardDateFormat

Html.text $" (%s{translations.Home.FormatDate(date, standardDateFormat)})"
// English: " (Mar 5th, 2026)"
// French:  " (5 Mar 2026)"
```

The format template is parsed once and reused across multiple dates. Each part — month name, day ordinal, separator, order — is fully driven by translation keys, so adding a new locale only requires adding translation entries, with no code change.

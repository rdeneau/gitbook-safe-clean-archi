---
icon: flask-vial
---

# Elmish loop tests

## Challenge: the `Cmd` track

The `update` function returns a `Model * Cmd<Msg>` pair. The `Model` part is easy to assert on — it is a plain immutable record. The `Cmd<Msg>` part is harder: an Elmish `Cmd` is an opaque list of side-effect functions, not designed for equality comparisons. In practice, most unit tests focus on the model and **discard** the command.

```fsharp
let newModel, _ =
    defaultModel
    |> update (Msg.ChangeLang(lang, Start))

newModel |> LangStatus.allOfModel =! expectedMenus
```

The wildcard `_` silently ignores whatever commands the `update` function emitted. This is the approach recommended by the Elmish community and described by [Jordan Marr](https://jordanmarr.github.io/fsharp/unit-testing-fable-dotnet/).

{% hint style="info" %}
The `=!` operator comes from [Unquote](https://github.com/SwensenSoftware/unquote). It means "must equal" and provides clear, expression-based failure messages.
{% endhint %}

## `UnitTestSession` — making `Cmd` safe for tests

Even though we discard the `Cmd` return value, the `update` function still **builds** it. If the `Cmd` construction involves a real Fable Remoting proxy (`Server.api`), it will fail at runtime in a .NET test host because the proxy only works in a browser context.

Shopfoo solves this with `UnitTestSession`, defined in `Shared/Remoting.fs`:

```fsharp
[<RequireQualifiedAccess>]
type DelayedMessageHandling =
    | Drop
    | EmitImmediately

type UnitTestSession = {
    DelayedMessageHandling: DelayedMessageHandling
    MockedApi: RootApi
    Now: DateTime option
}
```

When `FullContext.UnitTestSession` is `Some`, the [`Cmder.ofApiRequest`](../remoting.md) method uses the provided `MockedApi` instead of `Server.api`. The `Now` field allows tests to inject a deterministic date instead of `DateTime.Now`. In production, `UnitTestSession` is always `None` and `fullContext.Now` falls back to `DateTime.Now`:

```fsharp
member this.Now =
    this.UnitTestSession
    |> Option.bind _.Now
    |> Option.defaultValue DateTime.Now
```

{% hint style="info" %}
This design combines three patterns from James Shore's [Testing Without Mocks](https://www.jamesshore.com/v2/projects/nullables/testing-without-mocks):

- **Nullable** — `UnitTestSession` makes `Cmder` "nullable": it disables external communication while preserving normal behavior. `WithUnitTestSession` plays the role of Shore's `createNull()` factory.
- **Configurable Responses** — each test can configure the responses it needs, both via the `MockedApi` (overriding specific endpoints) and via `DelayedMessageHandling` (`Drop` or `EmitImmediately`).
- **Embedded Stub** — `RootApiMock.NothingImplemented` is a stub of the `RootApi` record where every method raises `NotImplementedException`, serving as the default that tests specialize.

Strictly speaking, `MockedApi` should be named `StubbedApi`: a *stub* provides preconfigured responses (which is what `MockedApi` does), whereas a *mock* verifies interactions (calls made, arguments received). The current naming follows a common but imprecise convention in the industry.
{% endhint %}

### Test setup helper

In `Shopfoo.Client.Tests`, a `FullContext` extension method makes it easy to enable the test session:

```fsharp
type FullContext with
    member fullContext.WithUnitTestSession(delayedMessageHandling, ?mockedApi, ?now) = {
        fullContext with
            UnitTestSession =
                Some {
                    DelayedMessageHandling = delayedMessageHandling
                    MockedApi = defaultArg mockedApi RootApiMock.NothingImplemented
                    Now = now
                }
    }
```

`RootApiMock.NothingImplemented` provides a `RootApi` where every method throws `NotImplementedException`. This ensures that if a test accidentally triggers a real API call, it fails immediately with a clear error.

The typical test model is set up with `DelayedMessageHandling.Drop`, which discards any delayed messages (like `Cmd.ofMsgDelayed`) rather than trying to schedule them through the JavaScript runtime:

```fsharp
let defaultModel: Model = {
    Page = Page.Login
    Theme = Theme.Light
    LangMenus = LangMenu.all
    Toast = None
    FullContext = FullContext.Default.WithUnitTestSession DelayedMessageHandling.Drop
}
```

### How `Cmder` adapts in test mode

When `UnitTestSession` is `Some`, `Cmder` adjusts two behaviors:

**API calls** — The mocked API is used instead of the real Fable Remoting proxy, and `Cmd.OfAsyncWith.either Async.StartImmediate` replaces `Cmd.OfAsync.either` so that async operations execute synchronously:

```fsharp
member this.ofApiRequest(args) : Cmd<'msg> =
    let api, cmdOfAsyncEither =
        match this.UnitTestSession with
        | None -> Server.api, Cmd.OfAsync.either
        | Some x -> x.MockedApi, Cmd.OfAsyncWith.either Async.StartImmediate
    // ...
    cmdOfAsyncEither args.Call api onResponse onException
```

**Delayed messages** — `ofMsgDelayed` (used for UI animations, polling, etc.) is controlled by `DelayedMessageHandling`:

```fsharp
member this.ofMsgDelayed(msg, delay) =
    match this.UnitTestSession with
    | Some { DelayedMessageHandling = Drop }            -> Cmd.none
    | Some { DelayedMessageHandling = EmitImmediately } -> Cmd.ofMsg msg
    | None -> Cmd.OfAsync.perform (fun () -> Async.Sleep ...) () (fun () -> msg)
```

- **`Drop`** — silences delayed messages; useful when testing a single `update` call in isolation
- **`EmitImmediately`** — dispatches the message synchronously as `Cmd.ofMsg`; useful when running a full Elmish loop in tests to verify the complete message cascade

## Testing strategies

### Strategy 1: single `update` call

The simplest approach calls `update` directly, discards the `Cmd`, and asserts on the model. This is sufficient when the message handling has no cascading effects worth verifying.

#### `AppShould` — testing the root `update`

The `AppShould` test class exercises the `App.update` function, which handles messages like `ChangeLang`, `FillTranslations`, `Login`, and `Logout`.

Each test follows the same pattern: build an initial model, apply one message through `update`, and assert on the resulting model.

```fsharp
type AppShould() =
    [<Test>]
    [<Arguments(Lang.Enum.English, "About")>]
    [<Arguments(Lang.Enum.French, "A propos")>]
    member _.``populate the FullContext after a ChangeLang success message``(Lang.FromEnum lang, about) =
        let { FullContext = actual }, _ =
            defaultModel
            |> update (Msg.ChangeLang(lang, Done(Ok { Lang = lang; Translations = Translations.In lang })))

        (actual.Lang, actual.Translations.Home.About, actual.Translations.PopulatedPages)
        =! (lang, about, Translations.AllPages)
```

Notable test coverage includes:

- **`ChangeLang Start`** — verifies the loading indicator is set on the correct language menu
- **`ChangeLang Done Ok`** — verifies translations are populated, including localized strings
- **`ChangeLang Done Error`** — verifies the existing translations are preserved and the error status is set
- **`FillTranslations`** — verifies that new translations are merged into existing ones
- **`PrepareQueryWithTranslations`** — verifies that the request body lists the pages still missing translations

{% hint style="info" %}
The `update` function is `internal` (not `private`) to allow the test project `Shopfoo.Client.Tests` to call it via `InternalsVisibleTo`.
{% endhint %}

### Strategy 2: full message cascade with mocked API

Some scenarios require verifying the **entire message cascade** — the initial message triggers a `Cmd`, which produces another message, which triggers another `Cmd`, and so on. Strategy 1 discards the `Cmd` and only asserts on the model after a single `update` call. Strategy 2 executes the full cascade, including API calls via mocked endpoints, and asserts on the final state and side effects (callbacks).

The examples below are drawn from `CatalogInfoShould.fs`, which tests the `CatalogInfo` form component — the form that adds or edits a product's catalog information.

#### Extracting message construction helpers

In the view, messages are typically constructed inline inside `dispatch(...)` calls. To make them testable, we can extract them into a `Msg` companion module:

```fsharp
[<RequireQualifiedAccess>]
module internal Msg =
    type ProductMsg = Product -> Msg

    // -- Product ----

    let addProduct: ProductMsg = fun product -> Msg.AddProduct(product, Start)

    let private changeCategory category : ProductMsg = fun product -> Msg.ProductChanged { product with Category = category }
    let changeDescription description : ProductMsg = fun product -> Msg.ProductChanged { product with Description = description }
    let changeImageUrl url : ProductMsg = fun product -> Msg.ProductChanged { product with ImageUrl = ImageUrl.Valid url }
    let changeName name : ProductMsg = fun product -> Msg.ProductChanged { product with Title = name }

    // -- Bazaar ----

    let private changeBazaarProduct newBazaarProduct : ProductMsg = changeCategory (Category.Bazaar newBazaarProduct)
    let changeBazaarCategory newBazaarCategory bazaarProduct : ProductMsg = changeBazaarProduct { bazaarProduct with Category = newBazaarCategory }

    // -- Book ----

    let private changeBook newBook : ProductMsg = changeCategory (Category.Books newBook)
    let changeBookSubtitle subtitle book : ProductMsg = changeBook { book with Subtitle = subtitle }
    let toggleBookTag (isChecked, tag) book : ProductMsg = changeBook { book with Tags = book.Tags.Toggle(tag, isChecked) }
    let toggleBookAuthor (isChecked, author) book : ProductMsg = changeBook { book with Authors = book.Authors.Toggle(author, isChecked) }
```

The view then delegates to these helpers:

```fsharp
// Before: inline construction
yield! propOnChangeOrReadonly (fun name ->
    dispatch (ProductChanged { product with Title = name }))

// After: extracted helper
yield! propOnChangeOrReadonly (fun name ->
    dispatch (Msg.changeName name product))
```

#### Fake data and mocked API endpoints

The test starts from `RootApiMock.NothingImplemented` and selectively overrides the endpoints needed by the scenario.

`FakeData` is a record that holds the data injected into the `UnitTestSession` of `FullContext` to adapt it to the current test. Using a record serves two purposes: it is easy to thread through `mockedApi`, `fullContext`, and `runScenario` as a single argument, and FsCheck can generate random values for it automatically — covering both the happy path and error cases without separate test methods.

```fsharp
type FakeData = {
    Now: DateTime
    AddProductResponse: Response<unit>
} with
    member private this.AddProductResult: ApiResult<unit> = this.AddProductResponse |> Response.toApiResult
    member this.AddProductError: ApiError option = this.AddProductResult |> Result.tryGetError

    member this.AddProductDate: Remote<DateTime> =
        match this.AddProductResult with
        | Ok _ -> Remote.Loaded this.Now
        | Error apiError -> Remote.LoadError apiError

let mockedApi (fake: FakeData) = {
    RootApiMock.NothingImplemented with
        RootApi.Catalog.AddProduct = fun _ -> async { return fake.AddProductResponse }
        RootApi.Catalog.GetBooksData = fun _ -> async { return Ok { Authors = Set.empty; Tags = Set.empty } }
}

let fullContext (fake: FakeData) =
    FullContext.Default
        .WithTranslations(Translations.In Lang.English)
        .WithPersona({ Persona = Persona.CatalogEditor; Token = AuthToken "test" })
        .WithUnitTestSession(DelayedMessageHandling.Drop, mockedApi fake, fake.Now)
```

`FakeData.AddProductDate` computes the expected `Remote<DateTime>` for assertions — `Remote.Loaded now` on success, `Remote.LoadError` on failure. This lets the same test verify both the happy path and error cases without separate test methods.

#### The `Scenario` module — generic Elmish loop simulator

Instead of running a real Elmish `Program`, we simulate the loop manually. This is simpler and avoids browser-dependent code (`Cmd.navigatePath`, etc.).

Each scenario step is a `'model -> 'msg` function (not a plain `'msg`), because `Msg.*` helpers need the **current** product from the model — mirroring how the view always has the current product in scope.

The loop simulation is extracted into a generic, page-agnostic `Scenario` module:

```fsharp
[<RequireQualifiedAccess>]
module Scenario

type Step<'model, 'msg> = 'model -> 'msg
type Update<'model, 'msg> = 'msg -> 'model -> 'model * Cmd<'msg>

let rec private processCmd (update: Update<'model, 'msg>) (model: 'model) (cmd: Cmd<'msg>) : 'model =
    let dispatchedMsgs = ResizeArray()
    let dispatch: 'msg -> unit = dispatchedMsgs.Add

    for sub in cmd do
        sub dispatch

    (model, dispatchedMsgs)
    ||> Seq.fold (fun m msg ->
        let m', cmd' = update msg m
        processCmd update m' cmd'
    )

/// Simulates an Elmish loop: applies each step to the current model,
/// then recursively processes all cascading messages from the resulting Cmd.
let run (initialModel: 'model) (update: Update<'model, 'msg>) (steps: Step<'model, 'msg> list) : 'model =
    (initialModel, steps)
    ||> List.fold (fun model step ->
        let msg = step model
        let model', cmd = update msg model
        processCmd update model' cmd
    )
```

**How it works:**

1. Each `step` function receives the current model and returns a `Msg`
2. `update` processes the message, returning `(model', cmd)`
3. `processCmd` executes each sub in the `Cmd` list, collecting dispatched messages
4. Cascading messages are processed recursively until no more are dispatched
5. `Cmd.ofEffect` (used for callbacks like `onSaveProduct`) executes the effect, captured via `saveProductCalls`
6. `Cmd.none` produces no dispatched messages, ending the cascade

**Why this works synchronously:** In test mode, `Cmder.ofApiRequest` uses `Cmd.OfAsyncWith.either Async.StartImmediate` instead of `Cmd.OfAsync.either`. This makes the async API call execute synchronously when the sub is invoked, so `dispatched.Add` receives the response message immediately.

#### The `Step` module and `runScenario` helper

A test class for an Elmish page can optionally define step helpers that know how to extract domain objects from the model. Grouping them in a `Step` module with `[<RequireQualifiedAccess>]` gives clean qualified access (`Step.changeProduct`, `Step.changeBook`, etc.) and makes scenarios easier to write and read:

```fsharp
type Step = Model -> Msg // Scenario.Step<Model, Msg>

[<RequireQualifiedAccess>]
module Step =
    let inline private productStep (f: Product -> Msg) : Step = productOf >> f

    let addProduct: Step = productStep Msg.addProduct
    let fetchProduct product : Step = fun _ -> Msg.ProductFetched(Ok({ Product = Some product }, Translations.Empty))

    let changeProduct (f: 'a -> Product -> Msg) (value: 'a) : Step = productStep (f value)
    let changeBook (f: 'a -> Book -> Product -> Msg) (value: 'a) : Step = productStep (fun product -> f value (bookOf product) product)
    let changeBazaar (f: 'a -> BazaarProduct -> Product -> Msg) (value: 'a) = productStep (fun product -> f value (bazaarOf product) product)
```

`runScenario` wires `FakeData` into `Scenario.run` and captures `onSaveProduct` callbacks:

```fsharp
let runScenario fakeData (steps: Step list) =
    let saveProductCalls = ResizeArray()
    let onSaveProduct (product, apiError) = saveProductCalls.Add(product, apiError)

    let update': Msg -> Model -> Model * Cmd<Msg> =
        update ignore onSaveProduct (fullContext fakeData)

    let finalModel = Scenario.run emptyModel update' steps
    finalModel, List.ofSeq saveProductCalls
```

#### Complete test: `CatalogInfoShould`

A private shared helper asserts on the product, save date, and `onSaveProduct` callback in a single tuple comparison. Each test method then reads as a pure scenario — expectations out, data and steps in:

```fsharp
type CatalogInfoShould() =
    member private _.``add a product and get`` expected fakeData steps =
        let model, saveProductCalls = runScenario fakeData steps

        (model.Product, model.SaveDate, saveProductCalls)
        =! (Remote.Loaded expected, fakeData.AddProductDate, [ expected, fakeData.AddProductError ])

    [<Test; FsCheckProperty(MaxTest = 5)>]
    member this.``add a complete book, filling in field by field`` fakeData =
        this.``add a product and get`` TidyFirst.product fakeData [
            Step.fetchProduct (Empty.bookProduct TidyFirst.isbn)

            Step.changeProduct Msg.changeName TidyFirst.product.Title
            Step.changeProduct Msg.changeDescription TidyFirst.product.Description
            Step.changeProduct Msg.changeImageUrl TidyFirst.product.ImageUrl.Url
            Step.changeBook Msg.changeBookSubtitle TidyFirst.subtitle
            Step.changeBook Msg.toggleBookAuthor (true, TidyFirst.author)
            Step.changeBook Msg.toggleBookTag (true, TidyFirst.tag1)
            Step.changeBook Msg.toggleBookTag (true, TidyFirst.tag2)

            Step.addProduct
        ]
```

The same pattern supports bazaar products — `Step.changeBazaar` extracts the `BazaarProduct` from the model, mirroring how `Step.changeBook` extracts the `Book`:

```fsharp
    [<Test; FsCheckProperty(MaxTest = 5)>]
    member this.``add a complete bazaar product, filling in field by field`` fakeData =
        this.``add a product and get`` MensCottonJacket.product fakeData [
            Step.fetchProduct (Empty.bazaarProduct MensCottonJacket.fsid)

            Step.changeBazaar Msg.changeBazaarCategory MensCottonJacket.category
            Step.changeProduct Msg.changeName MensCottonJacket.product.Title
            Step.changeProduct Msg.changeDescription MensCottonJacket.product.Description
            Step.changeProduct Msg.changeImageUrl MensCottonJacket.product.ImageUrl.Url

            Step.addProduct
        ]
```

The `[<FsCheckProperty(MaxTest = 5)>]` attribute generates 5 random `FakeData` values per test, covering different dates and both `Ok()` and `Error(...)` API responses. The `fakeData.AddProductDate` member computes the expected `SaveDate` for each case, so the same scenario handles both the happy path and error cases.

The `Step.addProduct` step triggers the following cascade:

1. `update` returns `{ model with SaveDate = Remote.Loading }` + `Cmd.addProduct ...`
2. `processCmd` executes the Cmd — mocked `AddProduct` API returns `fakeData.AddProductResponse` — dispatches `AddProduct(product, Done(...))`
3. `update` processes `Done(...)` — sets `SaveDate = Remote.Loaded fullContext.Now` (or `Remote.LoadError`) + `Cmd.ofEffect(onSaveProduct(product, error))`
4. `processCmd` executes `Cmd.ofEffect` — `onSaveProduct` is called, captured in `saveProductCalls`
5. `Cmd.ofEffect` does not dispatch any message — cascade ends

#### Controlling delayed messages

Tests can switch `DelayedMessageHandling` to verify different stages of an animated sequence:

- **`Drop`** — silences delayed messages; useful when testing a single `update` call in isolation or when delayed messages are irrelevant
- **`EmitImmediately`** — dispatches the message synchronously as `Cmd.ofMsg`; useful when running a full cascade to verify the complete message sequence including animations

## Other Client tests

### `AppViewShould` — page access resolution

`AppViewShould` tests the `resolvePageAccess` function — the logic that decides which page to display based on the current `Page` and the `User`. It covers scenarios like:

- An anonymous user accessing a protected page is redirected to `Login`
- A logged-in user accessing `Home` or `Login` is redirected to the default product index page
- Pages requiring specific features (e.g. `Admin`, `Catalog`) produce the expected access check

These tests are pure functions of `(Page, User) -> (Page, Feat option)`, with no Elmish loop involved.

### `AppTranslationsShould` — translation caching

`AppTranslationsShould` validates the `AppTranslations` type, which manages incremental translation loading. Tests verify that pages can be filled individually, that populating one page does not affect others, and that switching language replaces all cached strings cleanly.

### `RoutingTests` — URL roundtrip

A single FsCheck property-based test generates random `Page` values via custom arbitraries (`SanitizedPage`), converts them to URL segments, and parses them back. This gives strong confidence that the `Page -> URL -> Page` roundtrip is lossless.

### `FiltersShould` — filtering and sorting with FsCheck

`FiltersShould` is the most comprehensive test class. It validates the `Filters.apply` function, which is a **pure domain function** on the client side — no Elmish, no `Cmd`, no `update`. It takes a list of products, a `Filters` record, and returns matching rows with search highlighting.

All tests use [FsCheck](https://fscheck.github.io/FsCheck/) property-based testing via a custom `[<ShopfooFsCheckProperty>]` attribute.

**Filtering:**

- Filter bazaar products by category
- Filter books by author (randomly chosen from available authors)
- Filter books by tag (randomly chosen from available tags)

**Search** — with case-sensitive and case-insensitive variants:

- Search by description, title, subtitle, or author name
- Verify that matching rows contain the search term in the expected column
- Verify that case-sensitive search fails when the case is changed

**Sorting:**

- Sort by product number (index), SKU, title
- Sort bazaar products by category (with title as tiebreaker)
- Sort books by authors or tags (with title as tiebreaker)
- Each sort test verifies both ascending and descending directions

The test helpers (`buildSearchConfig`, `verifySearchSuccess`, `performSortBy`) keep individual test methods concise while the property-based approach provides broad coverage with minimal hand-written examples.

## Conclusion

The Elmish architecture makes the **logic** in views straightforward to test: the `update` function is a pure function from `(Msg, Model)` to `(Model, Cmd)`, and pure client-side functions like `Filters.apply` or `resolvePageAccess` can be tested in complete isolation. Combined with `UnitTestSession`, even `Cmd`-producing code can run safely in a .NET test host — either by discarding commands (Strategy 1) or by executing the full Elmish loop with a mocked API (Strategy 2).

All these tests run fast, are deterministic, and exercise the application logic without a browser.

{% hint style="info" %}
**Going further: browser-level testing**

To test the **graphical interface** itself — DOM rendering, CSS transitions, component interactions — heavier end-to-end tests are needed. Tools like [Playwright](https://playwright.dev/) can drive a real browser, with test scripts written in F#. These tests are more costly to set up and more fragile to maintain (they depend on the DOM structure), but they validate the application as the user actually experiences it.

Shopfoo could benefit from this approach to test cross-component interactions that are difficult to cover with unit tests alone: opening and closing Drawers, displaying and dismissing Toasts, or verifying that navigation between pages triggers the expected visual transitions.
{% endhint %}

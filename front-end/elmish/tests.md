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

type UnitTestSession = { DelayedMessageHandling: DelayedMessageHandling; MockedApi: RootApi }
```

When `FullContext.UnitTestSession` is `Some`, the [`Cmder.ofApiRequest`](../remoting.md) method uses the provided `MockedApi` instead of `Server.api`. In production, `UnitTestSession` is always `None`.

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
    member fullContext.WithUnitTestSession(delayedMessageHandling, ?mockedApi) = {
        fullContext with
            UnitTestSession =
                Some {
                    DelayedMessageHandling = delayedMessageHandling
                    MockedApi = defaultArg mockedApi RootApiMock.NothingImplemented
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
        let newModel, _ =
            defaultModel
            |> update (Msg.ChangeLang(lang, Done(Ok { Lang = lang; Translations = Translations.In lang })))

        newModel.FullContext.Lang =! lang
        newModel.FullContext.Translations.Home.About =! about
        newModel.FullContext.Translations.PopulatedPages =! Translations.AllPages
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

### Strategy 2: full Elmish loop with mocked API

Some scenarios require verifying the **entire message cascade** — the initial message triggers a `Cmd`, which produces another message, which triggers another `Cmd`, and so on. In this case, we run a real Elmish `Program` in the test, with the `MockedApi` implementing the endpoints that will be called.

The code excerpts below come from a real application built on this architecture, whose source cannot be shared in full. The key idea: the test builds a `Fixture` with a mocked API, injects a list of scenario messages, and lets the Elmish loop execute them — including all cascading messages produced by `Cmd` values.

#### Mocking specific API endpoints

The fixture starts from `RootApiMock.NothingImplemented` and selectively overrides the endpoints needed by the scenario:

```fsharp
init fixture
|> withChannels []
|> withPools [ poolToDelete ]
|> mockHotelChannelsApi (fun api ->
    { api with
        DeleteChannelPool = mockResponseOk () }
)
|> runScenario [ msgs.Delete ]
```

#### Running the Elmish program

`runScenario` builds a real Elmish `Program` that:

1. Calls `init` to get the initial model and commands
2. Dispatches each scenario message in order
3. Lets cascading messages (from `Cmd` values) execute between scenario messages
4. Records a **trace** of every model state, tagged with its origin (`Init`, `ScenarioMsg`, `CascadingMsg`, etc.)

```fsharp
Program.mkProgram init update view
|> Program.withErrorHandler ignore
|> Program.run
```

The `view` function acts as the message dispatcher — it dispatches the next queued message if the previous `update` produced no `Cmd` (i.e., no cascading message is expected):

```fsharp
let view state dispatch =
    Option.iter dispatch state.NextMessageToDispatch
    currentState <- state
```

#### Tracing message origins

Each model snapshot is tagged with a `TraceOrigin`:

```fsharp
type TraceOrigin =
    | Init
    | AfterInit of Msg
    | ScenarioMsg of Msg
    | CascadingMsg of Msg
```

This allows assertions on intermediate states — for example, verifying that during a deletion, the model shows "deleting" before reaching the final state:

```fsharp
|> expectAll [
    // Verify the intermediate state during the Deleting phase
    findTraceHaving (TraceOrigin.CascadingMsg msgs.Deleting)
    >> (fun { Model = model } -> model.PoolBeingDeleted =! Some deletedPoolId)

    // Verify the final state
    (fun (OutputModel model) -> model.PoolBeingDeleted =! None)
]
```

#### Controlling delayed messages per scenario

Tests can switch `DelayedMessageHandling` to verify different stages of an animated sequence. With `Drop`, the test observes the state right after the success message (e.g. a highlight animation still visible). With `EmitImmediately`, the delayed message fires synchronously, so the test observes the final cleaned-up state:

```fsharp
// Drop: the highlight stays on after pool creation
fixture |> runCreateChannelPoolScenario [
    PoolName newPoolName.Value
    MockCreatePoolResult (Created poolId)
    DelayedMessageHandling Drop
]
|> expectPoolBeingCreated (Some(poolId, Highlight.On))

// EmitImmediately: the delayed message fires, highlight is gone
fixture |> runCreateChannelPoolScenario [
    PoolName newPoolName.Value
    MockCreatePoolResult (Created poolId)
    DelayedMessageHandling EmitImmediately
]
|> expectPoolBeingCreated None
```

{% hint style="info" %}
Shopfoo does not yet use Strategy 2. Its `update` tests all follow Strategy 1 (single call, discard `Cmd`). The examples above illustrate what becomes possible when more complex scenarios need to be validated end-to-end.
{% endhint %}

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

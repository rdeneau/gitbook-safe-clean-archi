---
icon: vial-circle-check
---

# Tests

The [Shopfoo.Product.Tests](https://github.com/rdeneau/shopfoo/tree/main/tests/Shopfoo.Product.Tests) project demonstrates how to test domain workflows. Tests exercise workflows through the `IProductApi` entry point — the same boundary used by the real application — making them integration-style tests that validate the full wiring from API to data layer.

## Test Stack

| Library         | Role                                          |
| --------------- | --------------------------------------------- |
| **TUnit**       | Test runner                                   |
| **Unquote**     | Quotation-based assertions                    |
| **NSubstitute** | Mocking external HTTP clients                 |
| **FsCheck**     | Property-based testing with custom generators |

## Test Organization

The project mirrors the domain structure:

```txt
📂 Shopfoo.Product.Tests/
├──📄 Examples.fs              ← Shared test data (books, authors, products)
├──📂 Data/
│  └──📄 OpenLibraryShould.fs
└──📂 Workflows/
   ├──📄 Types.fs              ← Test-specific types and helpers
   ├──📄 ApiTestFixture.fs     ← Test harness with DI and mocks
   ├──📄 AddProductShould.fs
   ├──📄 DetermineStockShould.fs
   ├──📄 GetProductShould.fs
   ├──📄 GetPurchasePricesShould.fs
   ├──📄 MarkAsSoldOutShould.fs
   ├──📄 ReceiveSupplyShould.fs
   └──📄 SavePricesShould.fs
```

Each workflow has its own test file — the file tree is a map of tested features, reflecting the convention used in the production code.

## Test Naming

Test classes are named `{Feature}Should`, and each test method completes the sentence started by the class name. This produces human-readable test names when the runner displays them as `{Class}.{Method}`:

> `DetermineStockShould` ... `deduct sales from initial stock`
> `SavePricesShould` ... `reject invalid RetailPrice`

The main benefit is that "should" is factored out — no need to repeat it in every test name. The drawback is less flexibility in phrasing test names, since they must all grammatically follow "should".

This convention works particularly well with use case tests, where the feature name starts with a verb: `AddProductShould`, `DetermineStockShould`, `SavePricesShould`… In this case, the class name naturally maps to the _When_ in BDD terms — the action being exercised. It is less literal for non-use-case classes like `OpenLibraryShould`, but the resulting phrases still read correctly.

{% hint style="info" %}
Prefer **"given"** over "when" to describe preconditions — e.g. `reject supply given quantity is zero` rather than `reject supply when quantity is zero`. This aligns with the **Given/When/Then** convention from BDD (Gherkin): the use case name already captures the _When_, so the test method describes the expected outcome _(Then)_ followed by the precondition _(Given)_. It reads slightly less naturally but is more rigorous.
{% endhint %}

Here is the full test list, illustrating how the naming convention scales across the project. Parameterized tests appear as child nodes under the parent test name, with argument values in parentheses.

<details>

<summary>Full test list</summary>

```txt
Shopfoo.Product.Tests
  Data
    OpenLibraryShould
      make sanitized AuthorKey
        make sanitized AuthorKey(/authors/OL2653686A)
        make sanitized AuthorKey(/OL2653686A)
        make sanitized AuthorKey(authors/OL2653686A)
        make sanitized AuthorKey(OL2653686A)
  Workflows
    AddProductShould
      add initial prices too, in the given currency
        add initial prices too, in the given currency(e)
        add initial prices too, in the given currency(u)
      prevent adding a book already existing in the catalog
        prevent adding a book already existing in the catalog(False)
        prevent adding a book already existing in the catalog(True)
      prevent adding Bazaar products
        prevent adding Bazaar products(False)
        prevent adding Bazaar products(True)
      prevent adding the prices already existing
        prevent adding the prices already existing(False)
        prevent adding the prices already existing(True)
      reject invalid product
    DetermineStockShould
      deduct sales from initial stock
      differentiate stock by SKU
      take into account stock adjustment, ignoring previous events
      take into account stock arrivals after sales
    GetProductShould
      1-Cache: return None when no books in the cache match the given ISBN
      1-Cache: return Some book from the cache
      2-FakeStore: return None when no products match the given FSID in FakeStore
      2-FakeStore: return Some Product from FakeStore
      3-OpenLibrary: return None given book not found in OpenLibrary
      3-OpenLibrary: return Some Product when book and related author and work exist in OpenLibrary
    GetPurchasePricesShould
      return last price from most recent purchase event
      return no average prices given all purchases are older than one year
      return no average prices given purchases in 1Y with mixed currencies
      return no prices given no stock events
      return the average price of purchases within 1Y
    MarkAsSoldOutShould
      be rejected given a stock quantity greater than zero
        be rejected given a stock quantity greater than zero(1)
        be rejected given a stock quantity greater than zero(100)
        be rejected given a stock quantity greater than zero(5)
      update retail price to SoldOut given a product with no stock
        update retail price to SoldOut given a product with no stock(e, 19.99)
        update retail price to SoldOut given a product with no stock(u, 24.99)
    ReceiveSupplyShould
      create stock event when input is valid
      reject supply when purchase price is zero or negative
        reject supply when purchase price is zero or negative(-19.99)
        reject supply when purchase price is zero or negative(0)
      reject supply when quantity is zero or negative
        reject supply when quantity is zero or negative(-1)
        reject supply when quantity is zero or negative(-10)
        reject supply when quantity is zero or negative(0)
    SavePricesShould
      accept RetailPrice with ListPrice
        accept RetailPrice with ListPrice(e)
        accept RetailPrice with ListPrice(u)
      accept RetailPrice without ListPrice
        accept RetailPrice without ListPrice(e)
        accept RetailPrice without ListPrice(u)
      accept SoldOut with ListPrice
        accept SoldOut with ListPrice(e)
        accept SoldOut with ListPrice(u)
      accept SoldOut without ListPrice
        accept SoldOut without ListPrice(e)
        accept SoldOut without ListPrice(u)
      reject invalid ListPrice
        reject invalid ListPrice(-19.99, e, was -19·99)
        reject invalid ListPrice(-24.95, u, was -24·95)
        reject invalid ListPrice(0, e, was 0)
        reject invalid ListPrice(0, u, was 0)
      reject invalid RetailPrice
        reject invalid RetailPrice(-19.99, e, was -19·99)
        reject invalid RetailPrice(-24.95, u, was -24·95)
        reject invalid RetailPrice(0, e, was 0)
        reject invalid RetailPrice(0, u, was 0)
```

</details>

## Test Fixture

The `ApiTestFixture` is the central test harness. It builds a dependency injection container that combines:

- **Production dependencies**: the real `IProductApi` wiring via `AddProductApi()`
- **Test dependencies**: program mocks via `AddProgramMocks()`, in-memory repositories, and NSubstitute mocks for external HTTP clients (`IFakeStoreClient`, `IOpenLibraryClient`)

```fsharp
type ApiTestFixture(?books, ?pricesSet, ?sales, ?stockEvents) =
    let services =
        ServiceCollection()
            .AddProgramMocks()
            .AddProductApi()
            .AddSingleton(BookRepository(defaultArg books []))
            .AddSingleton(PricesRepository(defaultArg pricesSet []))
            // ...
    // ...
    member _.Api = serviceProvider.GetRequiredService<IProductApi>()
```

Tests create a fixture with the initial data they need, then call `fixture.Api` to exercise workflows. This approach tests the complete chain — from the API surface through workflow execution to the data layer — while keeping external dependencies mocked.

### TDD Style: Outside-In Diamond

This testing strategy follows the **Outside-In Diamond** approach (also called _Outside-In Classicist_), which combines two ideas:

- **Outside-In**: tests enter through the outermost public boundary — here `IProductApi` — rather than testing internal components (workflows, pipelines) in isolation. This validates the full integration: instruction wiring, undo strategies, and data-layer access.
- **Classicist / Diamond**: internal collaborators use **real implementations** (the actual `Api` class, real pipelines, in-memory repositories) rather than mocks. Only the **system boundary** is mocked — external HTTP clients (`IFakeStoreClient`, `IOpenLibraryClient`) that call third-party APIs.

This results in a **diamond-shaped** test distribution: few unit tests at the bottom, a thick layer of integration tests in the middle (through the API), and few end-to-end tests at the top. The shape contrasts with the classic test pyramid (many unit tests, fewer integration tests) and the London-school approach (mock every collaborator).

The Product test project illustrates this shape well:

- **Unit tests**: only one — `OpenLibraryShould` — which tests a pure utility (key sanitizing) in isolation.
- **Integration tests**: all the workflow tests in the `Workflows/` folder, exercising the full chain through `IProductApi`.
- **End-to-end tests**: none in the Shopfoo repository. E2E tests typically involve the full deployed stack (browser, server, database) and are generally better managed by a dedicated QA team — though nothing prevents the development team from writing them as well.

The main benefits are:

- **Confidence**: tests cover the real wiring, catching integration issues that isolated unit tests would miss.
- **Refactoring safety**: internal code can be restructured freely — only the `IProductApi` contract matters.
- **Few mocks**: less test setup, less coupling to implementation details.

## Assertions with Unquote

Unquote's key feature is **step-by-step reduction**: on failure, it evaluates the quoted expression incrementally, revealing intermediate values rather than a generic "expected X but got Y" message. It provides two assertion styles:

- **`actual =! expected`** — reads as "should equal". Simpler and more concise, suitable for straightforward equality checks:

  ```fsharp
  stock =! Ok { SKU = sku; Quantity = 6 }
  ```

- **`test <@ boolean-expression @>`** — more verbose but more flexible: it supports combining multiple assertions in a single quoted expression (with `&&`):

  ```fsharp
  test
      <@
          result = Error expectedError
          && orderCreated = None
          && sagaState.Status = SagaStatus.Failed(originalError = expectedError, undoErrors = [])
          && lightHistory sagaState = expectedHistory
      @>
  ```

  The caveat is that the reduction stops at the first `false` sub-expression — subsequent assertions are not evaluated.

### Multiple assertions

For **multiple assertions**, an alternative to `test <@ ... && ... @>` is to bundle actual and expected values into an **anonymous record** with `=!`:

```fsharp
{| A = actualA; B = actualB |} =! {| A = expectedA; B = expectedB |}
```

This reports all mismatches at once — unlike `&&` which stops at the first failure — while remaining a single assertion.

### Prefer constructing the expected value

When the result is a union type (`Option`, `Result`…), avoid pattern matching to extract the inner value. Instead, construct the full expected value and compare directly:

```fsharp
// ✖️ Fragile: pattern match + failwith
match result with
| Ok actual -> actual =! expected
| Error _ -> failwith "Expected Ok"

// ✅ Better: single assertion, full reduction on failure
result =! Ok expected
```

This keeps assertions as a single expression, produces clearer failure messages (Unquote shows the actual value alongside the expected one), and avoids `failwith` branches that provide no useful diagnostic.

When the result contains non-deterministic values (e.g. a generated `Guid` or a `DateTime`), constructing the full expected value is not possible. In this case, **project** the result to strip out the unpredictable parts before asserting:

```fsharp
// The result contains a generated InvoiceId — project it away
result |> Result.map _.Amount =! Ok 100m
```

This technique is known as **scrubbing** in approval testing libraries (e.g. [Verify](https://github.com/VerifyTests/Verify)): replacing volatile values with stable placeholders or removing them entirely so that assertions remain deterministic.

## Example-Based Tests

Most tests follow a straightforward pattern: set up a fixture, call the API, and assert on the result using Unquote:

```fsharp
[<Test>]
member _.``deduct sales from initial stock``() =
    let fixture = ApiTestFixture(
        stockEvents = isbn.Events [ Units.Purchased(10, EUR 5) ],
        sales = isbn.Sales [ unitsSold 1; unitsSold 2; unitsSold 1 ])

    async {
        let! stock = fixture.Api.DetermineStock sku
        stock =! Ok { SKU = sku; Quantity = 6 }
    }
```

### Parameterized Tests

TUnit offers several ways to provide data to the tests. `[<Arguments>]` attribute is simpler one — equivalent to xUnit's `[<InlineData>]` — but it accepts only **constant values**: numbers, strings, and enums. Since F# domain code typically uses discriminated unions rather than enums, a useful trick is to define a **mirror enum** alongside an **active pattern** that converts it to the domain type. The active pattern is applied directly in the parameter definition, so the test body works with the domain type without any conversion boilerplate:

```fsharp
// Types.fs — mirror enum + active pattern
type CurrencyEnum =
    | EUR = 'e'
    | USD = 'u'

module Currency =
    let (|FromEnum|) (currency: CurrencyEnum) =
        match currency with
        | CurrencyEnum.EUR -> Currency.EUR
        | CurrencyEnum.USD -> Currency.USD
        | _ -> invalidArg "currency" $"Invalid currency: {currency}"
```

```fsharp
// Test — the active pattern converts in the parameter itself
[<Test>]
[<Arguments(CurrencyEnum.EUR)>]
[<Arguments(CurrencyEnum.USD)>]
member _.``update retail price to SoldOut given a product with no stock``(Currency.FromEnum currency) =
    // `currency` is already a `Currency` domain type here
```

For more complex test data that cannot be expressed as constants, TUnit offers `[<MethodDataSource>]` and `[<ClassDataSource>]`. These attributes reference a method or a class that produces test cases at runtime, removing the constant-value constraint. They are more powerful but also more verbose — requiring a separate data source definition. See the [TUnit data-driven testing overview](https://tunit.dev/docs/writing-tests/data-driven-overview) for all the possibilities.

## Property-Based Tests

FsCheck is used for testing domain invariants that must hold for all valid inputs. For example, the purchase price average calculation is tested with mathematical properties:

```fsharp
[<Test>]
member _.``return the average price of purchases within 1Y``(...) =
    // Bounded: min ≤ average ≤ max
    // Homogeneity: scaling quantities doesn't change the average
    // Idempotency: uniform prices yield that exact price
```

### Active patterns as lightweight generators

Writing custom FsCheck generators can be verbose — especially for domain types with many constraints. A lighter alternative is to use **active patterns** that transform types FsCheck already knows how to generate:

```fsharp
let (|PositiveEuros|) (PositiveInt amount) : Money = Money.Euros(decimal amount)
```

`PositiveInt` is a built-in FsCheck wrapper. The `PositiveEuros` active pattern converts it into a valid `Money` value — no custom `Arb` or `Gen` required. It can be used directly in the test parameter:

```fsharp
member _.``return the average price of purchases within 1Y``
    (PositiveInt q1, PositiveEuros p1, PositiveInt q2, PositiveEuros p2) = ...
```

Compare this with generating a valid `Book`, which requires a custom generator composing multiple fields — a valid ISBN (with checksum), a list of authors (each with a valid OLID), tags, etc.:

```fsharp
let genBook: Gen<Book> =
    gen {
        let! isbn = genISBN          // Custom generator: 978 prefix + 9 digits + checksum
        let! subtitle = Gen.frequency [ 2, Gen.constant ""; 8, genMultiWords 7 ]
        let! authors = genAuthor |> Gen.listOfLength authorsCount  // Each with a valid OLID
        let! tags = genAlphaNumString |> Gen.map _.Value |> Gen.listOfLength tagsCount
        return { ISBN = isbn; Subtitle = subtitle; Authors = Set authors; Tags = Set tags }
    }
```

The rule of thumb: **use active patterns** when you can derive a valid domain value from a single primitive that FsCheck already generates; **use custom generators** when the domain type has structural invariants spanning multiple fields.

### Validation Testing

Generating **invalid** domain values is even harder than generating valid ones. The `AddProductShould` test class tackles this with a `FieldIssueType` discriminated union that models each possible validation error, and a `FieldIssue` record that pairs the expected error with a function to inject the issue into a valid product:

```fsharp
[<RequireQualifiedAccess>]
type FieldIssueType =
    | ISBNEmpty of NullOrWhitespace
    | NameEmpty of NullOrWhitespace
    | NameTooLong of TooLong
    | DescriptionTooLong of TooLong
    | BookSubtitleTooLong of TooLong

type FieldIssue = {
    Name: string
    Value: string
    Criteria: string
    UpdateProduct: Product -> Product
} with
    member prop.ExpectedError: GuardClauseError = { ... }
```

FsCheck generates a `NonEmptySet<FieldIssueType>` — an arbitrary combination of issues — and the test folds them onto a valid product to produce an invalid one, then verifies that all expected errors are reported:

```fsharp
[<Test>]
member _.``reject invalid product``(FieldIssues issues) =
    async {
        let invalidProduct, errors =
            ((CleanCode.Domain.product, []), issues)
            ||> Seq.fold (fun (product, errors) issue ->
                issue.UpdateProduct product, issue.ExpectedError :: errors)

        use fixture = new ApiTestFixture()
        let! result = fixture.Api.AddProduct(invalidProduct, Currency.EUR)
        // ...
    }
```

This approach requires substantial scaffolding — the `NullOrWhitespace` and `TooLong` wrapper types, the `Extensions` module converting each issue type to a `FieldIssue` with the right field name, max length, and product updater — but it ensures that the `validation { }` applicative CE correctly collects all errors rather than stopping at the first one.

## Saga Tests

Saga and cancellation scenarios are tested in the `Shopfoo.Program.Tests` project using a dedicated [Order domain](https://github.com/rdeneau/shopfoo/tree/main/tests/Shopfoo.Program.Tests/OrderContext). These tests are documented in the [Workflows](../3-domain-workflow/2-workflows.md#order-domain--saga-and-cancellation) page.

## Going Further: Mutation Testing

Property-based testing assesses test **quality** — do the tests verify meaningful properties? Another complementary technique is [mutation testing](https://learn.microsoft.com/en-us/dotnet/core/testing/mutation-testing): it introduces small changes (mutations) into the production code and checks whether the test suite detects them. While code coverage is a **quantitative** metric (how much code is exercised), mutation testing is **qualitative** (how well do the tests actually catch regressions).

[Stryker](https://stryker-mutator.io) is the most popular mutation testing framework for .NET. In practice, mutation testing is more commonly used in C# codebases, whereas property-based testing is more prevalent in F#. The two techniques are not mutually exclusive — combining them provides stronger confidence in test quality when the domain warrants it.

## Key Takeaways

- **Test through the API boundary**: workflows are tested via `IProductApi`, not by calling programs directly. This validates the full wiring including instruction preparation and undo strategies.
- **In-memory repositories**: data-layer implementations are replaced with simple in-memory stores, keeping tests fast and deterministic.
- **External clients are mocked**: only HTTP-based external dependencies (`IFakeStoreClient`, `IOpenLibraryClient`) use NSubstitute — everything else is a real implementation with in-memory storage.
- **Property-based testing for domain rules**: FsCheck validates mathematical properties and validation completeness, complementing example-based tests.

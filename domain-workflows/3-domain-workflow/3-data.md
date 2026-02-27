---
icon: database
---

# Data

The `Data/` folder implements the **Interface Adapters** ring of Clean Architecture — what Uncle Bob calls the layer that "convert[s] data from the format most convenient for the use cases and entities, to the format most convenient for some external agency such as the Database or the Web." In Hexagonal Architecture terms, these are the **secondary (driven) adapters**: the right-side adapters that the application core drives to reach external systems (HTTP APIs, databases, caches).

Each file plays a specific role in a layered architecture built around **Pipelines**, **Clients**, **Repositories**, **DTOs**, and **Mappers**.

## Concepts

### Pipeline

A **Pipeline** is the central abstraction in the Data layer. It exposes domain-oriented operations (`GetProduct`, `SavePrices`, `GetSalesStats`...) while hiding how and where data is stored or fetched.

```fsharp
type internal BooksPipeline(repository: BooksRepository) =
    member _.GetProducts() : Async<Product list> = ...
    member _.GetProduct(isbn: ISBN) : Async<Product option> = ...
    member _.AddProduct(product: Product) : Async<Result<unit, Error>> = ...
    member _.SaveProduct(product: Product) : Async<Result<PreviousValue<Product>, Error>> = ...
    member _.DeleteProduct(isbn: ISBN) : Async<Result<unit, Error>> = ...
```

Pipelines are always `internal` — they are not visible outside the domain project. The `Api` class and `CatalogPipeline` facade are the only consumers, and they depend on the **concrete pipeline classes** directly — no interface abstraction is needed here. Dependency inversion is only applied at the true boundaries: HTTP clients and repositories. In unit tests, this means we only need to control those boundaries — mock the HTTP clients (`IFakeStoreClient`, `IOpenLibraryClient`) and substitute the repositories with test data:

```fsharp
// ApiTestFixture.fs — test setup overrides only the boundaries
ServiceCollection()
    .AddProductApi()                                        // Production wiring (pipelines, etc.)
    .AddSingleton<IFakeStoreClient>(fakeStoreClientMock)    // Override: mock HTTP client
    .AddSingleton<IOpenLibraryClient>(openLibraryClientMock)// "
    .AddSingleton(BooksRepository.ofList testBooks)         // Override: test data
    .AddSingleton(PricesRepository.ofList testPrices)       // "
    .AddSingleton(SalesRepository testSales)                // "
    .AddSingleton(StockEventRepository testStockEvents)     // "
```

The production `AddProductApi()` registers all pipelines and their dependencies. The test then overrides only the leaf dependencies (clients and repositories) — the DI container's "last registration wins" behavior ensures the test versions are used. Pipelines themselves are never mocked; they run as production code, exercising the real mapping and routing logic. This aligns with **Outside-In Classicist TDD**—style taught by [Michaël Azerhad](https://www.linkedin.com/in/michael-azerhad/): tests verify the observable result of a use case through the public `IProductApi` interface, not the interactions between internal components. Only the external boundaries are substituted — the rest of the system is exercised as a whole. The key advantage: **tests are not fragile** — they are not coupled to implementation details, which makes internal refactorings safe and frictionless. This approach is also known as **Outside-In Diamond TDD**, a term coined by [Thomas Pierrain](https://tpierrain.blogspot.com/2021/03/outside-in-diamond-tdd-1-style-made.html).

### Client

A **Client** wraps an external HTTP API behind an interface. The interface is the contract; the concrete class handles HTTP mechanics (request construction, response parsing, deserialization).

```fsharp
[<Interface>]
type IFakeStoreClient =
    abstract member GetProductsAsync: unit -> Task<Result<ProductDto list, DataRelatedError>>

type internal FakeStoreClient(httpClient: HttpClient, serializerFactory: HttpApiSerializerFactory) =
    interface IFakeStoreClient with
        member this.GetProductsAsync() = ...
```

Two clients exist: `FakeStoreClient` (for the [FakeStore API](https://fakestoreapi.com/)) and `OpenLibraryClient` (for the [Open Library API](https://openlibrary.org/dev/)).

### Repository

A **Repository** abstracts a data store. In Shopfoo, for simplicity, there are no external databases or event stores — each repository hides an internal in-memory data store. Two flavors exist:

- **Dictionary-based** — for keyed lookups (books by ISBN, prices by SKU):

```fsharp
type BooksRepository = Dictionary<ISBN, BookRaw>
type PricesRepository = Dictionary<SKU, Prices>
```

- **Event-collection-based** — `FakeRepository<'k, 'v>` groups items by key into `ResizeArray` collections, suitable for event-sourced data (sales, stock events):

```fsharp
type internal FakeRepository<'k, 'v when 'k: comparison>(data: 'v seq, getKey: 'v -> 'k) =
    member _.Add(value: 'v) : unit
    member _.Get(key: 'k) : 'v list option
```

### DTO (Data Transfer Object)

Each external data source defines its own **DTO types** — flat records that mirror the API response shape, decoupled from domain types:

```fsharp
// FakeStore DTO
[<CLIMutable>]
type ProductDto = { Id: ProductId; Title: string; Price: decimal; Description: string; Category: string; Image: string }

// Books internal DTO
type BookRaw = { ISBN: ISBN; Title: string; Subtitle: string; Description: string; Authors: AuthorRaw list; Image: string; Tags: string list }
```

DTOs use `[<CLIMutable>]` for JSON deserialization compatibility. They live in the same namespace as their pipeline — never in the domain layer.

### Mapper

**Mappers** convert between DTOs and domain models.

```fsharp
module private Mappers =
    module DtoToModel =
        let mapBook (dto: BookRaw) : Product = { ... }
    module ModelToDto =
        let mapBook (book: Book) (product: Product) : BookRaw = { ... }
```

Bidirectional mappers (as in `Books.fs`) support round-trip persistence: domain model to DTO for writes, DTO to domain model for reads.

Mappers are preferably `private` for maximum encapsulation, but this is not a strict rule. When mappers involve complex logic (e.g., multi-step transformations, category mapping, cover URL resolution), dedicated unit tests become valuable. In that case, promote the mappers module to `internal` and grant the test project access to internals via `[<InternalsVisibleTo>]`. This is an acceptable trade-off — **testability generally takes precedence over encapsulation**.

### Cache

The `InMemoryProductCache` is a `ConcurrentDictionary`-based cache storing `(Product * Prices)` pairs for FakeStore products. It supports partial updates — changing just the `Product` or just the `Prices` while preserving the other half:

```fsharp
type internal InMemoryProductCache() =
    let cache = ConcurrentDictionary<FSID, Product * Prices>()

    member _.TryGetProduct(fsid) : Product option
    member _.TryGetPrices(fsid) : Prices option
    member this.SetProduct(fsid, product) = this.Set(fsid, product = product)
    member this.SetPrices(fsid, prices) = this.Set(fsid, prices = prices)
    member this.SetAll(entries) = ...
```

Caching is transparent — the `FakeStorePipeline` uses it internally; no consumer knows it exists.

### Facade

The `CatalogPipeline` is a **Facade** that unifies three product pipelines behind a single interface, routing by SKU type or product category:

```fsharp
type internal CatalogPipeline(booksPipeline, fakeStorePipeline, openLibraryPipeline, monitors) =
    member _.GetProduct(sku: SKU) : Async<Product option> =
        match sku.Type with
        | SKUType.FSID fsid -> fakeStorePipeline.GetProduct fsid
        | SKUType.ISBN isbn -> booksPipeline.GetProduct isbn
        | SKUType.OLID olid -> (* OpenLibrary call *)
        | SKUType.Unknown   -> async { return None }
```

Higher layers (the `Api` class) interact with `CatalogPipeline` only — they never reference individual pipelines directly.

## Shopfoo File Overview

| File                      | Concept               | Data Source             | Domain                         |
| ------------------------- | --------------------- | ----------------------- | ------------------------------ |
| `Prelude.fs`              | Shared utilities      | -                       | Fake latency, event repository |
| `Helpers.fs`              | DSL for test data     | -                       | Sales, Warehouse               |
| `Books.fs`                | Pipeline + Mappers    | In-memory Dictionary    | Catalog (Books)                |
| `FakeStore.fs`            | Client + Pipeline     | HTTP API                | Catalog (Bazaar)               |
| `OpenLibrary.fs`          | Client + Pipeline     | HTTP API                | Catalog (Books search)         |
| `InMemoryProductCache.fs` | Cache                 | ConcurrentDictionary    | FakeStore products             |
| `Prices.fs`               | Pipeline              | Dictionary + delegation | Pricing                        |
| `Sales.fs`                | Pipeline + Repository | FakeRepository          | Sales analytics                |
| `Warehouse.fs`            | Pipeline + Repository | FakeRepository          | Stock events                   |
| `Catalog.fs`              | Facade                | Delegates to above      | Unified product access         |

{% hint style="info" %}
In Shopfoo, each data pipeline is simple enough to live in a **single file** — DTOs, mappers, client, pipeline, and fake data all colocated. Two complementary rules of thumb help find the right balance:

- **Split when a file exceeds ~200 lines** — extract into separate files in a dedicated folder. The `OpenLibrary.fs` file (330+ lines) is a good candidate: it could be split into `OpenLibrary/Client.fs`, `OpenLibrary/Dto.fs`, `OpenLibrary/Keys.fs`, `OpenLibrary/Mappers.fs`, and `OpenLibrary/Pipeline.fs`.
- **Don't create too many small files either** — the C# dogma "1 class = 1 file" leads to excessive fragmentation. Conversely, F# codebases tend toward the opposite extreme: overly long files that become hard to navigate. The sweet spot is grouping **cohesive concepts together** while keeping files manageable.
{% endhint %}

## Dependency Inversion

The Data layer applies the **Dependency Inversion Principle** at multiple levels.

### Client interfaces abstract HTTP details

External API clients are consumed through interfaces, not concrete types. The pipeline depends on the abstraction; the concrete implementation is injected by the DI container:

```fsharp
// Interface (public — visible to tests and DI)
[<Interface>]
type IFakeStoreClient =
    abstract member GetProductsAsync: unit -> Task<Result<ProductDto list, DataRelatedError>>

// Pipeline depends on the interface, not the class
type internal FakeStorePipeline(fakeStoreClient: IFakeStoreClient) = ...

// Implementation (internal — hidden from consumers)
type internal FakeStoreClient(httpClient: HttpClient, serializerFactory: HttpApiSerializerFactory) =
    interface IFakeStoreClient with
        member this.GetProductsAsync() = ...
```

Notice the declaration order in the code snippet: **interface, then pipeline, then client implementation**. This is not a stylistic choice — F#'s strict top-to-bottom compilation order enforces it. The pipeline depends on the interface, so the interface must be declared first. The client implementation comes last because nothing in the same file depends on it. This compilation constraint naturally promotes dependency inversion: you _must_ define the abstraction before its consumers.

This also illustrates the **Interface Segregation Principle** (ISP): the `IFakeStoreClient` interface is designed to fit the pipeline's needs — the pipeline _owns_ the interface, and the client _adapts_ to it. The interface exposes only `GetProductsAsync`, not the full surface of the underlying HTTP client. This keeps the contract minimal and focused.

This separation also enables testing the pipeline without real HTTP calls — a test double implementing `IFakeStoreClient` can be injected instead.

### IProductApi hides the entire Data layer

The `Api` class implements the public `IProductApi` interface, which is the **only** type visible to outer layers. All pipelines, repositories, clients, and caches are registered as `internal` types in `DependencyInjection.fs`:

```fsharp
type IServiceCollection with
    member services.AddProductApi() =
        services
            .AddSingleton<IFakeStoreClient>(fun sp -> sp.GetRequiredService<FakeStoreClient>())
            .AddSingleton<FakeStorePipeline>()
            .AddSingleton<BooksPipeline>()
            .AddSingleton<CatalogPipeline>()
            // ...
            .AddSingleton<IProductApi, Api>()  // Only IProductApi is public
```

The presentation layer depends only on `IProductApi` — it has no knowledge of how many data sources exist, which HTTP APIs are called, or whether caching is involved. Both layers are fully decoupled: the presentation layer can change its UI framework, and the application layer can swap data sources, add caching, or restructure pipelines — without impacting the other.

## Encapsulation

Encapsulation is enforced through **access modifiers** and **layered delegation**.

### Visibility boundaries

| Level                | Visibility      | Example                                        |
| -------------------- | --------------- | ---------------------------------------------- |
| Client interfaces    | `public`        | `IFakeStoreClient`, `IOpenLibraryClient`       |
| Client classes       | `internal`      | `FakeStoreClient`, `OpenLibraryClient`         |
| Pipelines            | `internal`      | `BooksPipeline`, `FakeStorePipeline`           |
| Cache                | `internal`      | `InMemoryProductCache`                         |
| Mappers              | `private`       | `Mappers.DtoToModel`, `Mappers.ModelToDto`     |
| DTOs                 | `module-scoped` | `BookRaw`, `ProductDto`                        |
| Shared utilities     | `internal`      | `Fake.latencyInMilliseconds`, `FakeRepository` |
| Public API interface | `public`        | `IProductApi`                                  |

### What each layer hides

- **Clients** hide HTTP mechanics: request construction, response reading, JSON deserialization, URL encoding, error mapping.
- **Pipelines** hide data location: whether data comes from a `Dictionary`, a `ConcurrentDictionary`, an HTTP API, or a combination. The `PricesPipeline` routes to different backends depending on the product type — book/OpenLibrary prices live in a `Dictionary`, FakeStore prices live in the `FakeStorePipeline`'s cache:

```fsharp
type internal PricesPipeline(repository: PricesRepository, fakeStorePipeline: FakeStorePipeline) =
    member _.GetPrices(sku: SKU) =
        async {
            match sku.Type with
            | SKUType.ISBN _ -> return repository.Values |> Seq.tryFind (fun x -> x.SKU = sku)
            | SKUType.FSID fsid -> return! fakeStorePipeline.GetPrice fsid
            | _ -> return None
        }
```

- **Mappers** hide DTO-to-model translation logic, including category mapping, currency assignment, and cover URL generation.
- **The Facade** (`CatalogPipeline`) hides provider routing: consumers call `GetProduct(sku)` without knowing which pipeline handles which SKU type.
- **The Cache** is entirely invisible — `FakeStorePipeline` uses `InMemoryProductCache` as an implementation detail. No other type references it.

### Result-based error handling

All pipelines return `Async<Result<'T, Error>>` for fallible operations — no exceptions cross the Data layer boundary. Errors are explicit values (`DataNotFound`, `DuplicateKey`, `GuardClause`) that the workflow layer can match on and the saga can use to decide undo strategy.

## Data Flow

A typical read operation flows through these layers:

```txt
IProductApi.GetProduct(sku)
  --> CatalogPipeline.GetProduct(sku)     -- routes by SKU type
    --> BooksPipeline.GetProduct(isbn)     -- Dictionary lookup + DTO-to-model mapping
    --> FakeStorePipeline.GetProduct(fsid) -- cache lookup
    --> OpenLibraryPipeline(olid)          -- 3 sequential HTTP calls + mapping
```

A typical write operation adds workflow orchestration and undo support:

```txt
IProductApi.SaveProduct(product)
  --> runWorkflow SaveProductWorkflow
    --> Saga.RunInSaga
      --> CatalogPipeline.SaveProduct(product)  -- routes by category
        --> BooksPipeline: model-to-DTO + Dictionary update + returns PreviousValue
      --> On failure: undo via PreviousValue (restore initial state)
```

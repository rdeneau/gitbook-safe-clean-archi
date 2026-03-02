---
icon: cube
---

# Domain Model

In Clean Architecture, the **domain model** sits at the very center — it defines the core types and their business rules. In Shopfoo, the domain model is split across two distinct locations:

| Concern      | Project                  | Purpose                                                  |
| ------------ | ------------------------ | -------------------------------------------------------- |
| **Types**    | `Shopfoo.Domain.Types/`  | Type definitions — shared across all layers up to the UI |
| **Behavior** | `Shopfoo.Product/Model/` | Aggregates — business rules and invariants (DDD)         |

This separation is deliberate: types must travel across all layers (from server to front-end), while the **aggregate behavior** — validation, invariants, domain rules — stays in the domain project, internal and close to the workflows that enforce them.

## Types in `Domain.Types`

The `Shopfoo.Domain.Types` project defines all the domain types as **pure data structures** — records and discriminated unions — with no business logic beyond simple construction helpers and basic "getter".

```txt
📂 src/Core/
└──🗃️ Shopfoo.Domain.Types
   ├──📄 Common.fs        — Money, Currency, SKU, FSID, ISBN, OLID
   ├──📄 Errors.fs        — Guard, Validation, Error types
   ├──📄 Security.fs      — User, Claims, Access
   ├──📄 Translations.fs  — TranslationKey, Translations
   ├──📄 Catalog.fs       — Product, Category, Book
   ├──📄 Sales.fs         — Prices, Sales
   └──📄 Warehouse.fs     — Stock, StockEvent
```

### Why a shared types project?

The types are referenced by **six projects** across the entire solution:

- Shopfoo.Client via Shopfoo.Shared
- Shopfoo.Server
- Shopfoo.Product
- Shopfoo.Home
- Shopfoo.Program
- Shopfoo.Data

The Client (front-end Fable/Elmish project) accesses `Domain.Types` transitively through `Shopfoo.Shared`. This means the same F# types are used on both server and client — no DTO mapping or code generation is needed for the UI layer. One notable exception is the `Error` union: it is **not** used by the front-end. Instead, the *Server* maps domain errors to an [`ApiError`](../../front-end/remoting.md) type better suited for *Client* consumption.

{% hint style="info" %}
This is possible because `Domain.Types` contains only **Fable-compatible F#** — pure types without .NET-specific dependencies. Types that require .NET-only features (like `DateOnly`, `Reflection`) are guarded behind `#if !FABLE_COMPILER` or are inlined by the compiler using `inline` keyword.
{% endhint %}

### Type examples

**Product** (in `Catalog.fs`) — a core aggregate with typed identifiers and constrained fields:

```fsharp
type Product =
    { SKU: SKU
      Title: string
      Description: string
      Category: Category
      ImageUrl: ImageUrl }
```

**Category** — a discriminated union modeling the two product families:

```fsharp
type Category =
    | Bazaar of BazaarProduct
    | Books of Book
```

**Prices** (in `Sales.fs`) — with a factory method enforcing construction invariants:

```fsharp
type RetailPrice =
    | Regular of Money
    | SoldOut

type Prices =
    { SKU: SKU
      Currency: Currency
      RetailPrice: RetailPrice
      ListPrice: Money option }
```

**Stock** (in `Warehouse.fs`) — an entity identified by its `SKU`:

```fsharp
type Stock = { SKU: SKU; Quantity: int }
```

## Error model

The `Errors.fs` file in `Domain.Types` defines a comprehensive error model that spans the entire application.

### The `Error` union

All error kinds converge into a single discriminated union:

```fsharp
type Error =
    | BusinessError of IBusinessError
    | Bug of exn
    | DataError of DataRelatedError
    | OperationNotAllowed of OperationNotAllowedError
    | GuardClause of GuardClauseError
    | Validation of GuardClauseError list
    | WorkflowError of WorkflowError
    | Errors of Error list
```

Each case covers a distinct error category:

| Case                  | Origin                                                            |
| --------------------- | ----------------------------------------------------------------- |
| `GuardClause`         | A single field guard failure (see below)                          |
| `Validation`          | Multiple failures collected by applicative validation (see below) |
| `DataError`           | Data layer issues (not found, duplicate key, HTTP failure)        |
| `OperationNotAllowed` | A business rule rejects the operation                             |
| `WorkflowError`       | Workflow cancelled or undo failed (saga)                          |
| `Bug`                 | Unexpected exception                                              |
| `BusinessError`       | Domain-specific error (see below)                                 |
| `Errors`              | Recursive composition of multiple errors                          |

### Guard clauses

A **guard clause** protects a single field or value — it prevents constructing or persisting an object in an invalid state. The `Guard` class provides a fluent API for common checks:

```fsharp
type GuardClauseError = { EntityName: string; ErrorMessage: string }

type Guard(entityName: string) =
    member _.IsPositive(value) : Result<decimal, GuardClauseError>
    member _.IsNotEmpty(value) : Result<string, GuardClauseError>
    member _.Satisfies(value, condition, error) : Result<'a, GuardClauseError>
    member _.Satisfies(value, criteria: GuardCriteria) : Result<string, GuardClauseError>
    // ...
```

Each method returns a `Result` — either the validated value or a `GuardClauseError` describing what went wrong and on which entity.

`GuardCriteria` bundles multiple constraints for a single string field (required, min/max length), making guard declarations declarative:

```fsharp
type GuardCriteria = { PropertyName: string; MaxLength: int; MinLength: int; Required: bool }
```

In practice, criteria are declared alongside the domain type they protect, in a dedicated `Guard` module, then consumed via `guard.Validate` in the validation function:

```fsharp
// Shopfoo.Domain.Types/Catalog.fs
[<RequireQualifiedAccess>]
module Product =
    module Guard =
        let SKU          = GuardCriteria.Create("SKU",           required = true)
        let Name         = GuardCriteria.Create("Name",          required = true, maxLength = 128)
        let Description  = GuardCriteria.Create("Description",   maxLength = 512)
        let BookSubtitle = GuardCriteria.Create("BookSubtitle",  maxLength = 256)
        let ImageUrl     = GuardCriteria.None

// Shopfoo.Product/Model/Product.fs
let private guard = Guard("Product")

let private validateBook (product: Product) =
    match product.Category with
    | Category.Bazaar _ -> Validation.Ok()
    | Category.Books book -> guard.Validate(Product.Guard.BookSubtitle, book.Subtitle)

let validate (product: Product) =
    validation {
        let! _ = guard.Validate(Product.Guard.SKU,         product.SKU.Value)
        and! _ = guard.Validate(Product.Guard.Name,        product.Title)
        and! _ = guard.Validate(Product.Guard.Description, product.Description)
        and! _ = guard.Validate(Product.Guard.ImageUrl,    product.ImageUrl.Url)
        and! _ = validateBook product
        return ()
    }
```

### Validation

While a guard clause checks **one** field, **validation** aggregates multiple guard results into a single pass that reports *all* failures at once — not just the first one:

```fsharp
type Validation<'t, 'e> = Result<'t, 'e list>
```

The `validation` computation expression supports **applicative** composition with `let! ... and! ...`, as illustrated in the `validate` function above — each branch runs independently, so if `SKU` and `Description` both fail, both errors are collected. This is essential for user-facing forms where reporting all issues at once is expected.

The `Product` and `Prices` aggregates in the [Aggregates in `Model/`](#aggregates-in-model) section below provide more concrete examples of how guard clauses and validation are combined in practice.

### Extensibility via `IBusinessError`

A discriminated union in F# is a **closed set** — new cases cannot be added outside the declaring module. OCaml, F#'s closest sibling, offers [**polymorphic variants**](https://ocaml.org/manual/5.4/polyvariant.html) as a native alternative: open union types whose cases can be composed and extended across modules while still supporting exhaustive pattern matching. F# deliberately chose not to adopt them, favouring simplicity and predictability of closed unions. The `BusinessError of IBusinessError` with the `IBusinessError` interface pattern below is one idiomatic way to recover extensibility within that constraint.

```fsharp
[<Interface>]
type IBusinessError =
    abstract member Code: string
    abstract member Message: string
```

Any domain project can define its own business error type implementing `IBusinessError` and wrap it as `Error.BusinessError(myError)`. The central `Error` union does not need to know about every concrete error kind — it only depends on the interface. The `ErrorCategory` and `ErrorMessage` modules consume `Code` and `Message` for logging and display, without coupling to the concrete type.

{% hint style="info" %}
This is the **Open/Closed Principle** applied to F# unions: the `Error` type is closed to modification but open to new business error kinds through the `IBusinessError` interface — achieving the same extensibility that OCaml's open variants or polymorphic variants provide natively.
{% endhint %}

The idea of using an **interface as the payload of a union case** was inspired by Paul Blasucci's [FaultReport: a Theoretical Alternative to Result](https://paul.blasuc.ci/posts/fault-report.html). His proposal goes further: replace `Result` entirely with a richer type called `Report`, paired with an `IFault` interface, to address several structural weaknesses of `Result`. It is worth noting that in Shopfoo, the `program` computation expression already handles **error lifting** — the transparent unification of each workflow's specific error sub-type into the root `Error` type — which eliminates a significant class of the friction points Paul identifies. The `IBusinessError` pattern therefore captures the extensibility insight from his work without requiring a full departure from `Result`.

## Aggregates in `Model/`

In DDD, an **aggregate** groups an entity with its invariants — the business rules that must always hold true. Shopfoo borrows this concept from DDD but applies it selectively:

- **What Shopfoo retains** — the aggregate as a module that owns the **invariants** of a domain entity. By convention, workflows delegate validation to the aggregate before persisting — though the type system does not enforce this.
- **What Shopfoo does not adopt** — the aggregate as a **transactional boundary** designed for concurrency control (e.g., optimistic locking on a concert ticket sale). Shopfoo has no such contention scenario.

While all types live in `Domain.Types` — entities (`Product`, `Stock`, `Prices`), value objects (`Money`, `Currency`), and typed identifiers (`SKU`, `ISBN`, `FSID`) — the **aggregate logic** for entities is split out into the domain project's `Model/` folder:

```txt
📂 src/Feat/
└──🗃️ Shopfoo.Product
   └──📂 Model/
      ├──📄 Extensions.fs  — Internal helpers for Guard
      ├──📄 Product.fs     — Product aggregate (invariants)
      ├──📄 Prices.fs      — Prices aggregate (invariants)
      └──📄 Stock.fs       — Stock aggregate (invariants)
```

Each module corresponds to a **DDD aggregate** and is marked `[<RequireQualifiedAccess>]` — callers must write `Product.validate`, `Prices.validate`, etc. — keeping the API explicit and making each aggregate's responsibilities clearly visible.

{% hint style="warning" %}
Notice what is **absent** from `Model/`: there is no `IProductRepository`, `IPricesRepository`, or any persistence interface. In a classic DDD/OOP design, each aggregate root owns a repository interface. Shopfoo takes a different, functional approach: persistence is handled entirely in the application layer through [Instructions](1-instructions.md), where each instruction represents **a single function** (`GetPrices`, `SaveProduct`, `AddPrices`...) rather than an object grouping multiple operations. This keeps `Model/` purely focused on invariants, with no coupling to persistence concerns.
{% endhint %}

### How workflows enforce aggregate invariants

The aggregate modules are consumed by [Workflows](2-workflows.md). Any workflow that persists an aggregate calls its validation **before** saving, ensuring the domain stays consistent:

```fsharp
// SaveProductWorkflow — calls Product.validate before saving
interface IProductWorkflow<Product, unit> with
    override _.Run product =
        program {
            do! Product.validate product       // 👈 Aggregate invariant
            let! (PreviousValue _) = Program.saveProduct product
            return Ok()
        }

// SavePricesWorkflow — calls Prices.validate before saving
interface IProductWorkflow<Prices, unit> with
    override _.Run prices =
        program {
            do! Prices.validate prices         // 👈 Aggregate invariant
            let! (PreviousValue _) = Program.savePrices prices
            return Ok()
        }

// MarkAsSoldOutWorkflow — calls Stock.verifyNoStock before changing prices
interface IProductWorkflow<SKU, unit> with
    override _.Run sku =
        program {
            let! stock = ...
            do! Stock.verifyNoStock stock      // 👈 Aggregate invariant
            let! (PreviousValue _) = Program.savePrices { prices with RetailPrice = SoldOut }
            return Ok()
        }
```

The pattern is always the same: **validate first, persist second**. If an invariant fails, the `program` computation expression short-circuits with a validation error — no data is written.

### Product aggregate

The `Product` module enforces that all required fields satisfy their business constraints (non-empty, max length) using the `Guard` type from `Domain.Types`:

```fsharp
[<RequireQualifiedAccess>]
module Shopfoo.Product.Model.Product

let validate (product: Product) =
    validation {
        let! _ = guard.Validate(Product.Guard.SKU, product.SKU.Value)
        and! _ = guard.Validate(Product.Guard.Name, product.Title)
        and! _ = guard.Validate(Product.Guard.Description, product.Description)
        and! _ = guard.Validate(Product.Guard.ImageUrl, product.ImageUrl.Url)
        and! _ = validateBook product
        return ()
    }
```

Key points:

- **Applicative validation** — `let! ... and! ...` collects *all* errors at once instead of stopping at the first failure. This is powered by the `validation` computation expression defined in `Errors.fs`.
- **GuardCriteria** — Each `Product.Guard.*` value (e.g., `Product.Guard.SKU`) encapsulates the rules for a field (required, min/max length). These criteria are defined alongside the `Product` type in `Domain.Types`, keeping the rules close to the data they constrain. Because `GuardCriteria` is a plain F# record, it can also be consumed by the **front-end** (e.g., to drive form validation), avoiding any duplication of these rules between layers.
- **Category-specific rules** — The private `validateBook` helper adds extra invariants for `Books` products (e.g., subtitle constraints), while `Bazaar` products pass through.

### Prices aggregate

The `Prices` module ensures price values are positive when present:

```fsharp
[<RequireQualifiedAccess>]
module Shopfoo.Product.Model.Prices

let validate (prices: Prices) =
    validation {
        let! _ = guardListPrice(prices).ToValidation()
        and! _ = guardRetailPrice(prices).ToValidation()
        return ()
    }
```

The two private helpers handle the optionality of each price:

- `guardListPrice` — validates only when `ListPrice` is `Some`, otherwise returns `Ok`.
- `guardRetailPrice` — validates `Regular` prices but skips `SoldOut` (no price to validate).

### Stock aggregate

The `Stock` module enforces a contextual invariant: a product can only be marked as sold out when its stock quantity is zero.

```fsharp
[<RequireQualifiedAccess>]
module Shopfoo.Product.Model.Stock

let verifyNoStock (stock: Stock) =
    validation {
        let! _ =
            Guard(nameof Stock)
                .Satisfies(stock.Quantity = 0, "Stock quantity must be zero to mark as sold out.")
                .ToValidation()
        return ()
    }
```

Unlike `Product.validate` or `Prices.validate` which are general-purpose guards, `verifyNoStock` is a **contextual invariant** — it protects one specific business operation (`MarkAsSoldOut`). This illustrates that aggregate modules can contain both general validation and business rules (invariants).

### Internal extension

The `Extensions` module adds a convenience method to the `Guard` type, combining `Satisfies` and `ToValidation` into a single call:

```fsharp
module internal Shopfoo.Product.Model.Extensions

type Guard with
    member guard.Validate(criteria: GuardCriteria, value: string) =
        validation {
            let! _ = guard.Satisfies(value, criteria).ToValidation()
            return ()
        }
```

This is an **F# type extension** — it augments the `Guard` class from `Domain.Types` without modifying it. The `internal` visibility ensures this helper is only available within the `Shopfoo.Product` project.

## Summary

| Aspect               | `Domain.Types`                       | `Model/`                                |
| -------------------- | ------------------------------------ | --------------------------------------- |
| **DDD role**         | Entities + Value Objects (data only) | Aggregates (invariants, business rules) |
| **Contains**         | Type definitions (records, DUs)      | Validation functions, domain invariants |
| **Visibility**       | Public — shared across all layers    | Internal to the domain project          |
| **Fable-compatible** | Yes — usable in the front-end        | No — server-side only                   |
| **Depends on**       | `Shopfoo.Common` only                | `Domain.Types`                          |

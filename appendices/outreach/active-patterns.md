# What if your pattern matching could call functions?

Every modern language has some form of pattern matching. You can destructure records, match on enum cases, check types. But you're always limited to what the data *looks like* — its structure.

What if you could match on what the data *means*? Parse a string into a domain type, test-and-cast through an interface, extract query parameters — all inside the match expression itself, composable with other patterns?

That's what F# Active Patterns do.

## The idea in one sentence

An active pattern is a **function invoked inside a match branch**. It transforms the input data so it can be matched differently. That's it.

The different forms are just variations of this transformation:

- **Single-case** `(|P|)` — always transforms (reshapes the data into a new form)
- **Partial** `(|P|_|)` — transforms or rejects (acts as a filter)
- **Multi-case** `(|A|B|⋯|)` — partitions into categories

A quick example — turning a string parse into a pattern:

```fsharp
let (|Int|_|) (s: string) = Int32.TryParse s |> Option.ofPair

match userInput with
| Int n -> printfn "Got number %d" n   // Int is a function called here
| _     -> printfn "Not a number"
```

No guard clause, no intermediate variable. The function lives *inside* the pattern.

## Concrete example 1: composable type tests with `As`

F# has a built-in type test pattern (`:?`), but it cannot be nested inside other patterns — it requires a separate `match` or a guard clause. The `As` active pattern solves this:

```fsharp
let inline (|As|_|) (input: obj) : 't option =
    match input with
    | :? 't as value -> Some value
    | _ -> None
```

In the *Shopfoo* demo app, business errors are wrapped behind an `IBusinessError` interface:

```fsharp
type Error =
    | BusinessError of IBusinessError
    | ...

type OrderError =
    | OrderCannotBeCancelledAfterShipping
    | OrderTransitionForbidden of current: OrderStatus * attempted: OrderStatus
    interface IBusinessError with ...
```

Since `BusinessError` holds an `IBusinessError`, you can't directly match on the concrete `OrderError`. With `As`, you can drill through four nesting levels in a single expression:

```fsharp
let canUndoExceptAfterShipOrder undoCriteria =
    match undoCriteria with
    | { WorkflowError = BusinessError(As OrderCannotBeCancelledAfterShipping) } -> false
    | _ -> true
```

What happens here:

1. `{ WorkflowError = ... }` — destructures the record
2. `BusinessError(...)` — matches the union case
3. `As ...` — type-tests the interface to the concrete type
4. `OrderCannotBeCancelledAfterShipping` — matches the specific error case

Without `As`, this would require nested `match` expressions or a guard clause. With it, the intent reads in one line.

## Concrete example 2: composable URL parsing

The *Shopfoo* frontend parses URLs into typed `Page` values using layered active patterns. The design builds up from primitives to domain concepts to full route parsing.

**Primitive patterns** handle raw extraction:

| Pattern           | Purpose                                 |
| ----------------- | --------------------------------------- |
| `(⏐Dashed⏐)`      | Splits a string on `-` into segments    |
| `(⏐Param⏐_⏐) key` | Extracts a query parameter value by key |
| `(⏐YesNo⏐_⏐)`     | Recognizes `"yes"` / `"no"` as `bool`   |
| `(⏐Col⏐_⏐)`       | Recognizes a column key as a `Column`   |
| `(⏐Desc⏐_⏐)`      | Recognizes the `"desc"` token           |

**Domain patterns** compose primitives into meaningful extractions:

```fsharp
let (|Sort|) queryParams =
    match queryParams with
    | Param "sort" (Dashed [ Col col; Desc ]) -> Some(col, Descending)
    | Param "sort" (Col col) -> Some(col, Ascending)
    | _ -> None
```

`Sort` calls `Param`, which calls `Dashed`, which calls `Col` and `Desc` — all nested inside one pattern expression. The string `"name-desc"` is parsed into `Some(Column.Name, Descending)` through composition alone.

**The `&` combinator** lets you apply multiple patterns simultaneously on the same input:

```fsharp
let (|Filters|) queryParams =
    match queryParams with
    | Highlight highlighting & MatchCase caseMatching & SearchTerm searchTerm & Sort sortBy ->
        fun filters -> { filters with SortBy = sortBy; Search = { ... } }
```

**The final result** — `parseFromUrlSegments` reads like a route table:

```fsharp
let parseFromUrlSegments segments =
    match segments with
    | ...
    | [ "books"; Route.Query(Route.Filters changeFilters & Route.Author authorId & Route.Tag tag) ] ->
        Page.ProductIndexDefaultsWith (...)
    | _ -> Page.NotFound(Router.formatPath segments)
```

Each match arm maps a URL pattern to a `Page` value. All the parsing — splitting, parameter lookup, type conversion, default handling — is hidden behind composable active patterns.

## What about other languages?

| Language    | Feature                                  | Custom extractors? |
| ----------- | ---------------------------------------- | ------------------ |
| **Scala**   | Extractors (`unapply`)                   | Yes                |
| **Haskell** | View Patterns + Pattern Synonyms         | Yes                |
| **C#**      | Property / relational / logical patterns | No                 |
| **Rust**    | Enum patterns + guard clauses            | No                 |
| **Kotlin**  | Destructuring + `when`                   | No                 |
| **Java**    | `instanceof` / `switch` patterns (21+)   | No                 |
| **Python**  | Structural `match/case` (3.10+)          | No                 |
| **Swift**   | `guard case` / `if case`                 | No                 |
| **OCaml**   | —                                        | No                 |

Only **Scala** (extractors via `unapply`) and **Haskell** (view patterns + pattern synonyms) offer true equivalents — the ability to define custom extraction logic that plugs into pattern matching.

The rest — C#, Rust, Java, Python, Kotlin, Swift — do *structural* pattern matching: they can destructure known types, check conditions, even combine patterns with logical operators. But they can't call user-defined functions *inside* a pattern branch. You're limited to what the type system already exposes.

C# has made impressive progress (property patterns, relational patterns, `and`/`or`/`not` combinators), but it still can't express something like `BusinessError(As OrderCannotBeCancelledAfterShipping)` — a user-defined extractor composed with built-in patterns.

## Takeaway

Active Patterns turn pattern matching from a deconstruction tool into a **modeling tool**. Instead of matching on raw structure, you build a vocabulary of composable, domain-specific patterns — parsers, validators, extractors — that read like declarations of intent.

In *Shopfoo*, they appear everywhere: URL routing, error handling, security checks, test data generation. Each pattern is small, testable, and reusable — and they compose into expressive, declarative code.

## Read more

👉 [The `As` active pattern](../../tips-and-tricks/as-active-pattern.md)
👉 [Active pattern composition for URL parsing](../../front-end/navigation.md#deep-dive-active-pattern-composition-for-url-parsing)
👉 [Active patterns as lightweight FsCheck generators](../../tips-and-tricks/lightweight-fscheck-generators.md)

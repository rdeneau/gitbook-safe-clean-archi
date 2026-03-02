# Strongly-typed identifiers with generics

🏷️ `#TypeModeling` `#TypeSafety`

## The problem

Using raw `Guid` for entity identifiers is a form of [primitive obsession](https://refactoring.guru/fr/smells/primitive-obsession) — a code smell where primitive types are used instead of dedicated domain types. It is error-prone: nothing prevents accidentally passing an `OrderId` where a `PaymentId` is expected. The compiler treats them as the same type.

## Usual F# approaches: tagged primitives

The standard remedy in F# is to wrap the primitive in a **tagged type** — a thin wrapper that exists solely to distinguish values at the type level. Three idioms are commonly used, as described below. All three approaches solve the basic type-confusion problem but come with trade-offs in ergonomics, serialization, and boilerplate — which motivates the generic solution described here after.

### Single-case discriminated union

The most idiomatic choice:

```fsharp
type OrderId   = OrderId   of Guid
type PaymentId = PaymentId of Guid
```

The compiler now rejects any confusion between the two. The main drawback is the need to unwrap the value explicitly (e.g. `let (OrderId id) = orderId`).

### Single-field record

Slightly more verbose but allows adding members directly:

```fsharp
type OrderId   = { Value: Guid }
type PaymentId = { Value: Guid }
```

However, when multiple record types share the same field name, F# type inference resolves `{ Value = someGuid }` to the **last type defined in scope**, which can lead to subtle and confusing type errors.

### Unit of measure

F# allows tagging numeric primitives with a phantom measure:

```fsharp
[<Measure>] type Order
[<Measure>] type Payment

type OrderId   = Guid<Order>   // requires FSharp.UMX
type PaymentId = Guid<Payment>
```

This approach requires the [FSharp.UMX](https://github.com/fsprojects/FSharp.UMX) library to extend units of measure to non-numeric types such as `Guid` and `string`.

### Resources

- [Designing with types: Single case union types](https://fsharpforfunandprofit.com/posts/designing-with-types-single-case-dus/) — Scott Wlaschin, *F# for fun and profit*
- [You Really Wanna Put a Union There? You Sure?](https://paul.blasuc.ci/posts/really-scu.html) — Paul Blasucci
- [The Equinox Programming Model › Identity](https://nordfjord.io/equinox/#identity) — Einar Norðfjörð

## The approach

Define a generic `Id<'kind>` record parameterised by a `'kind` type used both for **type discrimination** and at **runtime** via the stored `Kind` field:

```fsharp
[<AutoOpen>]
module rec Id =
    type Id<'kind> = private {
        Kind: 'kind
        Prefix: string
        Id: Guid
    } with
        member this.Type = $"%A{this.Kind}"
        member this.Value = $"%s{this.Prefix}-%s{this.Id.ToString()[0..7]}"

        static member New (kind: 'kind) prefix : Id<'kind> = {
            Kind = kind
            Prefix = prefix
            Id = Guid.NewGuid()
        }
```

Unlike a true phantom type — where the type parameter is erased and carries no runtime presence — `'kind` is stored in the `Kind` field and can be used for serialisation or formatting (see the `Type` property). This comes at virtually no cost because each `'kind` is defined as an **empty single-case union** (e.g. `type Order = private | Order`), which the F# compiler represents as a singleton with no heap allocation.

This design also changes the call site ergonomics: rather than passing a type argument (`Id.New<Order>`), you pass the **unique union case as a value** (`Id.New Order`), which reads naturally and lets the compiler infer `'kind` without annotation.

{% hint style="info" %}
If runtime access to the kind is not needed, the `Kind` field and the `Type` member can simply be removed, reducing `Id<'kind>` to a true phantom-type wrapper with no behavioral change at the call site.
{% endhint %}

Each entity then defines its own ID type via a **kind case** and a **factory module**:

```fsharp
    module OrderId =
        type Order = private | Order
        let New () : OrderId = Id.New Order "ORD"

    module PaymentId =
        type Payment = private | Payment
        let New () : PaymentId = Id.New Payment "PAY"

    type OrderId = Id<OrderId.Order>
    type PaymentId = Id<PaymentId.Payment>
```

Usage is straightforward:

```fsharp
let orderId = OrderId.New()    // Value: "ORD-a1b2c3d4"
let paymentId = PaymentId.New() // Value: "PAY-e5f6g7h8"
```

## Key design decisions

**`module rec`** — The module is declared **recursive** so that the `OrderId.New` function can reference the `OrderId` type alias in its return type annotation, even though the alias is declared *after* the module. This is optional but provides a safety net: the type annotation ensures you cannot accidentally use the wrong prefix (e.g. `"PAY"` for an `OrderId`).

**`private` record constructor** — The `Id<'kind>` record fields are `private`, forcing all creation to go through `Id.New`. This guarantees the prefix is always consistent with the kind.

**`private` phantom case** — Each phantom type (e.g. `Order`, `Payment`) is also `private`, preventing external code from constructing IDs directly via `Id.New Order "..."`.

**Formatted `Value`** — The `Value` property produces a human-readable string like `"ORD-a1b2c3d4"` (prefix + first 8 characters of the GUID), useful for logging and debugging. The `Type` property uses `%A` formatting on the phantom case to get the entity name (e.g. `"Order"`).

## Trade-offs

### Advantages

- Minimal boilerplate per ID type (3 lines)
- Standalone — no external dependency
- Type-safe — compiler prevents mixing IDs
- Terse Fantomas formatting

### Limitations

- `module rec` can be confusing
- The `'kind` refers to the entity, not the ID itself
- The kind type parameter adds a level of indirection
- Deserialization requires a `FromString` (or `FromGuid`) factory method to reconstruct an ID from a value stored in a database, which opens a small breach in the protection net: that entry point bypasses the `private` constructor but cannot be avoided

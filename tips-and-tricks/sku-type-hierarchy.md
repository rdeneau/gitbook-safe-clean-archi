# Modeling a type hierarchy without OOP: the SKU example

🏷️ `#TypeModeling` `#MutualRecursion` `#Serialization`

In Shopfoo, a product identifier (SKU — Stock Keeping Unit) can be an `FSID` (FakeStore), an `ISBN` (book), or an `OLID` (OpenLibrary). This looks like a textbook case for OOP inheritance: a base `SKU` interface with concrete subtypes. That way, the `Book` type could declare its identifier as `ISBN` (not just `SKU`), giving compile-time guarantees that a book always carries an ISBN — something a plain discriminated union like `type SKU = FSID of int | ISBN of string | OLID of string` cannot express.

However, the design uses a **record + discriminated union** combination instead:

```fsharp
type SKU = { Type: SKUType; Value: string }
and FSID = FSID of int
and ISBN = ISBN of string
and OLID = OLID of string
and SKUType =
    | FSID of FSID
    | ISBN of ISBN
    | OLID of OLID
    | Unknown
```

**Why not an interface?** Fable.Remoting (the library used for client-server communication) cannot serialize interfaces with custom coders in V5. A record with a `SKUType` discriminated union is fully serializable out of the box.

**Why `type ... and ...` (mutually recursive types)?** Normally, F# declarations follow a **top-down** order: each type is defined before it is used. Here, `SKU` references `SKUType`, and `SKUType` references `FSID`, `ISBN`, `OLID` — which are defined _between_ the two. The `and` keyword creates a **mutually recursive group**, allowing all these types to reference each other regardless of declaration order. This is a pragmatic trade-off: we lose the clean top-down flow, but gain a cohesive type group where the common abstraction (`SKU`) and its variants (`FSID`, `ISBN`, `OLID`) are defined together.

The conversion properties (`AsSKU`, `Value`) are defined in a **separate extension module** to avoid two issues:

```fsharp
[<AutoOpen>]
module SKUExtensions =
    type FSID with
        member this.Value = let (FSID fsid) = this in $"FS-%i{fsid}"
        member this.AsSKU = { Type = SKUType.FSID this; Value = this.Value }
    type ISBN with
        member this.Value = let (ISBN isbn) = this in isbn
        member this.AsSKU = { Type = SKUType.ISBN this; Value = this.Value }
    // ...
```

- **Infinite recursion**: if `AsSKU` were defined inside the `type ... and ...` group, the `Value` property of `SKU` would call `this.Value` on `FSID`/`ISBN`/`OLID`, which in turn could reference `SKU` — creating a circular dependency.
- **Serialization**: Fable.Remoting serializes all members of a type automatically. If `AsSKU` were a regular member of `FSID`/`ISBN`/`OLID`, the serializer would follow it into `SKU`, which references `SKUType`, which references `FSID`/`ISBN`/`OLID` again — causing a circular serialization. As type extensions, these properties are invisible to the serializer, so serialization works out of the box without having to configure depth limits — which would be fragile and error-prone for future types.

Each domain type can then use the specific identifier — `Book` has an `ISBN` field, `BazaarProduct` has an `FSID` field — while `Product` holds a `SKU` for the common abstraction. This gives **type-safe identifiers per domain** without relying on OOP inheritance.

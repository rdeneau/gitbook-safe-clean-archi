# Active patterns as lightweight FsCheck generators

🏷️ `#ActivePattern` `#FsCheck` `#Test`

Writing custom FsCheck generators can be verbose — especially for domain types with many constraints. A lighter alternative is to use **active patterns** that transform types FsCheck already knows how to generate:

```fsharp
let (|PositiveEuros|) (PositiveInt amount) : Money = Money.Euros(decimal amount)
```

`PositiveInt` is a built-in FsCheck wrapper. The `PositiveEuros` active pattern converts it into a valid `Money` value — no custom `Arb` or `Gen` required. It can be used directly in the test parameter:

```fsharp
member _.``return the average price of purchases within 1Y``
    (PositiveInt q1, PositiveEuros p1, PositiveInt q2, PositiveEuros p2) = ...
```

The rule of thumb: **use active patterns** when you can derive a valid domain value from a single primitive that FsCheck already generates; **use custom generators** when the domain type has structural invariants spanning multiple fields.

# Parameterized tests: mirror enums with active patterns

🏷️ `#ActivePattern` `#Test` `#TUnit`

TUnit's `[<Arguments>]` attribute accepts only **constant values**: numbers, strings, and enums. Since F# domain code typically uses discriminated unions rather than enums, a useful trick is to define a **mirror enum** alongside an **active pattern** that converts it to the domain type. The active pattern is applied directly in the parameter definition, so the test body works with the domain type without any conversion boilerplate:

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

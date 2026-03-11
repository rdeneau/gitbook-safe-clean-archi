# Better assertions with Unquote

🏷️ `#Test` `#Assertion` `#Unquote`

## Prefer full-state assertions

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

## Multiple assertions in a single check

This full-state approach also combines well with **multiple assertions**. Rather than checking values one by one (and losing context on the first failure), bundle all actual and expected values into a **tuple** or an **anonymous record** with `=!`:

```fsharp
// ✖️ Stops at the first failure — you don't see the other mismatches
test
    <@
        result = Error expectedError
        && orderCreated = None
        && sagaState.Status = SagaStatus.Failed(originalError = expectedError, undoErrors = [])
    @>

// ✅ Tuple: sufficient for a few values with obvious meaning
(result, orderCreated) =! (Error expectedError, None)

// ✅ Anonymous record: prefer for more than 4 values, or when a primitive's values are ambiguous
{| Result = result; OrderCreated = orderCreated; SagaStatus = sagaState.Status |}
=! {| Result = Error expectedError; OrderCreated = None; SagaStatus = SagaStatus.Failed(originalError = expectedError, undoErrors = []) |}
```

The `test <@ ... && ... @>` style with Unquote stops reducing at the first `false` sub-expression — subsequent assertions are not evaluated. Both the tuple and anonymous record approaches report **all** mismatches at once while remaining a single assertion, giving a complete diagnostic on failure.

- Use a **tuple** when you have ≤ 4 values and their meaning is obvious from context.
- Prefer an **anonymous record** when you have more than 4 values, or when a primitive value's role is not obvious — the field names act as built-in labels.

## Pre-conditions with `assume` / `assumeThat`

In longer tests, some assertions are **not** the final check — they guard intermediate steps (Arrange/Act phases) so that the test fails early with a clear message instead of a confusing `NullReferenceException` or wrong diagnostic later.

To make this intent explicit, define `assume` and `assumeThat` aliases:

```fsharp
let inline assume expr = test expr
let inline assumeThat message assertion = testThat message assertion
```

{% hint style="info" %}
`assume` / `assumeThat` are semantically identical to `test` / `testThat` — they produce the same Unquote reduction on failure. The distinct names signal to the reader: *"this is a guard, not the thing we're testing."*
{% endhint %}

{% hint style="warning" %}
The naming mirrors NUnit's `Assume.That()`, but the behavior differs: NUnit marks the test as **Inconclusive** (skipped) when an assumption fails, whereas our `assume` / `assumeThat` raise a **Failure** like any other assertion. This is a TUnit limitation (no Inconclusive status), but it's arguably better — a broken pre-condition means the test setup is wrong and should be fixed, not silently skipped.
{% endhint %}

## Named assertions with `testThat` / `assumeThat`

When `test <@ ... @>` or `assume <@ ... @>` is not self-explanatory enough, you can attach a **message** that appears in the Unquote reduction on failure. This is especially useful for complex boolean expressions where the reduction alone may not tell you *what* the assertion is checking.

```fsharp
let inline testThat message assertion =
    test <@ let _ = message in %assertion @>

let inline assumeThat message assertion = testThat message assertion
```

The trick is `let _ = message in %assertion`: the `message` string is spliced into the quotation so that Unquote prints it as the first reduction step, acting as a human-readable label.

{% hint style="info" %}
`testThat` / `assumeThat` are semantically identical to `test` / `assume` — they produce the same Unquote reduction on failure, with the message prepended. Use the named variants when the expression alone would not make the intent obvious.
{% endhint %}

### Failure output

```fsharp
let actual, expected = -12, 9
testThat "actual should equal expected" <@ actual = expected @>
```

```text
message; actual = expected
"actual should equal expected"; actual = expected
actual = expected
-12 = 9
false
```

The first line shows the message alongside the expression, giving immediate context.

### Example

```fsharp
[<Test>]
member _.``clear non-seed data after reset``() =
    async {
        use fixture = new ApiTestFixture()

        // Arrange
        let! _ = fixture.Api.ResetAllCaches()
        let product = CleanCode.Domain.product // Alternate version of the seed book, with an ISBN formatted differently
        let sku = product.SKU
        let! addResult = fixture.Api.AddProduct(product, Currency.EUR)
        let! productBefore = fixture.Api.GetProduct sku
        let! pricesBefore = fixture.Api.GetPrices sku

        assumeThat
            "Non-seed product and its prices have been added"
            <@
                addResult = Ok()
                && productBefore |> Option.map _.SKU = Some sku
                && pricesBefore |> Option.map _.SKU = Some sku
            @>

        // Act
        let! resetResult = fixture.Api.ResetAllCaches()

        // Assert
        let! productAfter = fixture.Api.GetProduct sku
        let! pricesAfter = fixture.Api.GetPrices sku
        testThat "Non-seed product and prices are cleared" <@ resetResult = Ok() && productAfter = None && pricesAfter = None @>
    }
```

Here `assume` and `assumeThat` guard the Arrange phase while `testThat` carries the final assertion with an explicit description.

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

This full-state approach also combines well with **multiple assertions**. Rather than checking values one by one (and losing context on the first failure), bundle all actual and expected values into an **anonymous record** with `=!`:

```fsharp
// ✖️ Stops at the first failure — you don't see the other mismatches
test
    <@
        result = Error expectedError
        && orderCreated = None
        && sagaState.Status = SagaStatus.Failed(originalError = expectedError, undoErrors = [])
    @>

// ✅ Reports all mismatches at once
{| Result = result; OrderCreated = orderCreated; SagaStatus = sagaState.Status |}
    =! {| Result = Error expectedError; OrderCreated = None; SagaStatus = SagaStatus.Failed(originalError = expectedError, undoErrors = []) |}
```

The `test <@ ... && ... @>` style with Unquote stops reducing at the first `false` sub-expression — subsequent assertions are not evaluated. The anonymous record approach reports **all** mismatches at once while remaining a single assertion, giving a complete diagnostic on failure.

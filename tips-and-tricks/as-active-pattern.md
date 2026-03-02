# The `As` active pattern: type test and cast in pattern matching

🏷️ `#ActivePattern`

The `As` active pattern performs a type test and cast, inspired by C#'s `as` operator:

```fsharp
let inline (|As|_|) (input: obj) : 't option =
    match input with
    | :? 't as value -> Some value
    | _ -> None
```

F# has a built-in type test pattern (`:?`), but it cannot be **nested** inside other patterns — it requires a separate `match` expression or a guard clause. `As` solves this by wrapping the type test in a partial active pattern, making it composable with any other pattern.

## Use case: `BusinessError(As OrderCannotBeCancelledAfterShipping)`

In Shopfoo, the common `Error` union wraps business errors behind an `IBusinessError` interface:

```fsharp
[<Interface>]
type IBusinessError =
    abstract member Code: string
    abstract member Message: string

type Error =
    | BusinessError of IBusinessError
    | Bug of exn
    // ...
```

Each domain defines its own error type implementing this interface:

```fsharp
type OrderError =
    | OrderCannotBeCancelledAfterShipping
    | OrderTransitionForbidden of current: OrderStatus * attempted: OrderStatus
    interface IBusinessError with
        override this.Code = ...
        override this.Message = ...
```

Since `BusinessError` holds an `IBusinessError`, we cannot directly pattern-match on the concrete `OrderError` case. `As` solves this by enabling deep pattern matching across multiple layers. Consider this saga undo predicate:

```fsharp
type UndoCriteria = { WorkflowError: Error; History: ProgramStep list }

let canUndoExceptAfterShipOrder undoCriteria =
    match undoCriteria with
    | { WorkflowError = BusinessError(As OrderCannotBeCancelledAfterShipping) } -> false
    | _ -> true
```

The pattern drills through **four nesting levels** in a single expression:

1. `{ WorkflowError = ... }` — destructures the `UndoCriteria` record
2. `BusinessError(...)` — matches the `BusinessError` case of the `Error` union
3. `As ...` — type-tests the `IBusinessError` interface to the concrete `OrderError` type
4. `OrderCannotBeCancelledAfterShipping` — matches the specific `OrderError` case

Without `As`, this would require a guard clause or nested `match` expressions to perform the intermediate type test.

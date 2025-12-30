---
icon: hands-asl-interpreting
---

# Dependency Interpretation

**Dependency Interpretation** is a functional programming pattern that addresses the limitations of dependency injection by treating dependencies as **data** rather than behavior. This is the first version of our `program` computation expression.

## From Interfaces to Instructions

Instead of injecting interface implementations, we abstract dependencies as **instructions** represented by a discriminated union type.

### Data Layer Interfaces

Let's start with typical dependencies from the Data layer:

```fsharp
[<Interface>]
type IChannelClient =
    abstract GetChannelDescription: channelId: int -> Async<string option>

[<Interface>]
type IMailSender =
    abstract Send: MailEntities.Mail -> Async<unit>

[<Interface>]
type IMappingClient =
    abstract NotifyLinkEvent: channelId: int * hotelId: int * LinkStatus -> Async<unit>
    abstract GetMappingActivation: channelId: int * hotelId: int -> Async<MappingEntities.MappingActivationDto option>
```

### The Program Type

We transform these interfaces into a recursive union type that represents a program as a **list of instructions**:

```fsharp
type Program<'a> =
    | Stop of 'a

    // Channel
    | GetChannelDescription of ChannelId * (ChannelDescription option -> Program<'a>)

    // Mail
    | SendMail of MailType * (Result<unit, Error> -> Program<'a>)

    // Mapping
    | NotifyLinkEvent of (ChannelId * CrsHotelId * LinkStatus) * (Result<unit, Error> -> Program<'a>)
    | GetMappingActivation of (ChannelId * CrsHotelId) * (MappingActivation option -> Program<'a>)
```

## Understanding Program Instructions

Each instruction follows a common pattern:

```fsharp
| Instruction           of Input                                 * (Output                    -> Program<'a>)

// Queries
| GetChannelDescription of ChannelId                             * (ChannelDescription option -> Program<'a>)
| GetMappingActivation  of (ChannelId * CrsHotelId)              * (MappingActivation  option -> Program<'a>)

// Commands
| NotifyLinkEvent       of (ChannelId * CrsHotelId * LinkStatus) * (Result<unit, Error>       -> Program<'a>)
| SendMail              of MailType                              * (Result<unit, Error>       -> Program<'a>)
```

### Command/Query Separation Convention

We adopt a clear convention for instruction return types:

- **Commands** return a `Result<unit, Error>` (no output data, only success or error)
- **Queries** return an `option` (where `None` ≃ HTTP 404 `NotFound`)

## How the Program Type Works

```fsharp
type Program<'a> = // <───────Recursion─────────┐
    | Stop of 'a   //                     ┌─────┴─────┐
    | Instruction1 of Input1 * (Output1 -> Program<'a>)
    | Instruction2 of Input2 * (Output2 -> Program<'a>)
    | ...                   // └─────Continuation─────┘
```

**Key concepts:**

- **Recursive type:** `Program` is a list of instructions, each containing the next program step.
- **Terminal case:** `Stop` contains the program's final returned value.
- **Continuation:** The second element of each instruction—`Output -> Program<'a>`—is a function that:
  - Processes the instruction's output,
  - Returns the rest of the program,
  - Contains the program logic.

## Building Programs

### Example 1: Simple Value

```fsharp
// Program returning a value—10 here
let p : Program<int> = Stop 10
```

### Example 2: Single Query

```fsharp
// Program returning the channel description by its id
let getChannelDescription (channelId: ChannelId) : Program<ChannelDescription option> =
    GetChannelDescription (channelId, Stop)
                                  //  └──┘ Continuation
```

{% hint style="info" %}
#### Key point

`Stop` matches the continuation signature—`'a -> Program<'a>`. The program's returned value is passed to `Stop`, hence the returned type: `Program<int>` and `Program<ChannelDescription option>`.
{% endhint %}

### Example 3: Transforming Results

```fsharp
// Program returning the channel name by its id
let getChannelName channelId : Program<ChannelName option> =
    GetChannelDescription (channelId, fun channelOption -> 
        Stop (channelOption |> Option.map _.Name))
```

## The `map` Function

To avoid nested continuations, we can use a functorial `map` function:

```fsharp
let getChannelName channelId : Program<ChannelName option> =
    getChannelDescription channelId
    |> Program.map (Option.map _.Name)
```

### Implementing `map`

The `map f program` function is based on pattern matching:

```fsharp
module Program =
    let rec map (f: 't -> 'u) (program: Program<'t>) : Program<'u> =
        match program with
        | GetChannelDescription(x, next) -> GetChannelDescription(x, next >> map f)
        | GetMappingActivation(x, next) -> GetMappingActivation(x, next >> map f)
        | NotifyLinkEvent(x, next) -> NotifyLinkEvent(x, next >> map f)
        | SendMail(x, next) -> SendMail(x, next >> map f)
        | Stop x -> Stop(f x)
```

**How it works:**

- Each instruction has a continuation function `next` that returns the rest of the program
- We compose `next` with `map f` to transform the final result: `next >> map f`
- When we reach `Stop x`, we apply `f` to the returned value: `f x`

## The Program Computation Expression

We can improve the syntax further with a computation expression (CE):

```fsharp
let getChannelName channelId : Program<ChannelName option> =
    program {
        let! channelDescription = getChannelDescription channelId
        return channelDescription |> Option.map _.Name
    }
```

### Building the CE

The `program` CE is a monadic computation expression. The minimum required methods are:

```fsharp
type ProgramBuilder() =
    member _.Return(x) = Stop x
    member _.Bind(px, f) = Program.bind f px

let program = ProgramBuilder()
```

- **`Return`:** Elevates a value to a `Program`—it's just `Stop`.
- **`Bind`:** Delegates to the `Program.bind` function for monadic composition.

### Implementing `bind`

The monadic `bind f program` is very similar to `map`:

```fsharp
module Program =
    let rec bind (f: 't -> Program<'u>) (program: Program<'t>) : Program<'u> =
        match program with
        | GetChannelDescription(x, next) -> GetChannelDescription(x, next >> bind f)
        | GetMappingActivation(x, next) -> GetMappingActivation(x, next >> bind f)
        | NotifyLinkEvent(x, next) -> NotifyLinkEvent(x, next >> bind f)
        | SendMail(x, next) -> SendMail(x, next >> bind f)
        | Stop x -> f x
```

**Difference from `map`:**

- We bind the program returned by the instruction continuation.
- The `Stop x` case returns the program produced by `f x` (not `f x` wrapped in `Stop`).

### 🔗 Additional resources

Here are three series of articles, each offering a different perspective to explore these difficult-to-grasp concepts in greater depth:

- [My "Computation Expressions" series (2025)](https://dev.to/rdeneau/f-computation-expressions-4ge6) explains the functional concepts—*Functor*, *Monad*, *Applicative*—applied to build F# computation expressions.
- [Scott Wlaschin's "Computation Expressions" series (2013)](https://fsharpforfunandprofit.com/series/computation-expressions/) is another detailed introduction to CEs, choosing not to mention the underlying functional concepts in order to be more accessible, at the expense of a comprehensive overview.
- [Scott Wlaschin's "Map and Bind and Apply, Oh my!" series (2015)](https://fsharpforfunandprofit.com/series/map-and-bind-and-apply-oh-my/) is useful for understanding the `map` and `bind` functions, independently of CEs and functional concepts.

## Instruction Helpers

To make programs more readable, we define helpers for each instruction:

```fsharp
// Queries
let getChannelDescription args = GetChannelDescription(args, Stop)
let getMappingActivation args = GetMappingActivation(args, Stop)

// Commands
let notifyLinkEvent args = NotifyLinkEvent(args, Stop)
let sendMail args = SendMail(args, Stop)
```

The pattern is simple: `let instruction args = Instruction(args, Stop)`

Now we can write:

```fsharp
program {
    let! channelDesc = getChannelDescription channelId
    do! sendMail mailContent
    return channelDesc
}
```

## The Interpreter

A program is a **pure value**—producing no side effects—that needs to be **interpreted** to execute and get its returned value.

The **interpreter** collaborates with dependencies from the Data layer to execute the instructions.

{% hint style="info" %}
The interpreter lives not in the *Domain* layer but in the *Application* layer of the *Clean Architecture*.
{% endhint %}

### Interpreter Implementation

The interpreter is a recursive, asynchronous function:

```fsharp
let rec interpretProgram dependencies (prog: Program<'a>) : Async<'a> =
    async {
        match prog with
        | Stop x ->
            return x

        | GetChannelDescription(x, next) ->
            let! (res: ChannelDescription option) = // Type annotation optional, given here for more clarity
                dependencies.ChannelClient.getChannelDescription x
            return! interpretProgram dependencies (next res)

        | GetMappingActivation(x, next) ->
            let! res = dependencies.MappingClient.getMappingActivation x
            return! interpretProgram dependencies (next res)

        | NotifyLinkEvent(x, next) ->
            let! res = dependencies.MappingClient.notifyLinkEvent x
            return! interpretProgram dependencies (next res)

        | SendMail(x, next) ->
            let! (res: Result<unit, Error>) =
                dependencies.MailSender.sendMail x
            return! interpretProgram dependencies (next res)
    }
```

**How it works:**

1. Pattern match on the program
2. For `Stop`, return the value
3. For each instruction:
   - Execute the operation using the appropriate dependency
   - Pass the result to the continuation to get the next program
   - Recursively interpret the rest of the program

## Complete Example

Let's see a complete workflow:

```fsharp
// Domain workflow
let processPayment (currentDate: DateTimeOffset, payment) =
    program {
        let! cmd = validateProcessPaymentCommand payment |> expectValidationError
        let! card = tryGetCard cmd.CardNumber
        let today = currentDate.Date |> DateTimeOffset
        let tomorrow = currentDate.Date.AddDays 1. |> DateTimeOffset
        let! operations = getBalanceOperations (cmd.CardNumber, today, tomorrow)
        let spentToday = BalanceOperation.spentAtDate currentDate cmd.CardNumber operations
        let! (card, op) =
            CardActions.processPayment currentDate spentToday card cmd.PaymentAmount
            |> expectOperationNotAllowedError
        do! saveBalanceOperation op |> expectDataRelatedErrorProgram
        do! replaceCard card |> expectDataRelatedErrorProgram
        return card |> toCardInfoModel |> Ok
    }
```

This example comes from Roman Nevolin's excellent article [Fighting complexity in software development](https://github.com/atsapura/CardManagement/blob/master/article/Fighting.Complexity.md#business-logic).

## Pattern Name and Origins

This pattern is called **Dependency Interpretation** by Scott Wlaschin in his [Dependency Injection series](https://fsharpforfunandprofit.com/posts/dependencies-4/).

The key insight: instead of injecting dependencies as objects, we **interpret** a data structure (our program) that describes what operations to perform.

## Benefits

### Clear Separation

Complete separation between:

- **What** to do (the program as data)
- **How** to do it (the interpreter)

### Pure Domain Logic

Domain workflows remain pure functions returning `Program<'a>` values. All side effects are isolated in the interpreter.

### Easy Testing

Programs can be inspected, transformed, or interpreted differently for tests without any mocking framework.

### Explicit Effects

The `Program` type makes all possible side effects visible and trackable.

## Limitations

Despite its benefits, this approach has a significant limitation:

{% hint style="warning" %}
The more instructions you add, the bigger the `Program` type becomes, until it gets hard to read and too complicated to maintain.
{% endhint %}

Every new instruction requires:

- Adding a new case to the `Program` type
- Updating both `map` and `bind` functions
- Adding a case to the interpreter

**How can we improve this pattern to scale better?**

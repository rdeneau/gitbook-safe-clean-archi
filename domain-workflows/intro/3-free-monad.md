---
icon: diamond-half-stroke
---

# Free Monad

The **Free Monad** pattern improves upon *Dependency Interpretation* by separating instructions into domain-specific groups, making the codebase more scalable and maintainable. This is version 2 of our `program` computation expression.

## What is a Free Monad?

{% hint style="info" %}
F# does not support a general definition of the free monad, but we can adapt the concept for our `Program` type.
{% endhint %}

A free monad adapted for our use case looks like this, where `Instruction` is a functor (hence its `map` function):

```fsharp
type Program<'a> =
    | Free of Instruction<Program<'a>>
    | Pure of 'a

module Program =
    let rec bind f = function
        | Free inst -> inst |> Instruction.map (bind f) |> Free
        | Pure x -> f x
```

Key changes from Version 1:

- `Pure` replaces our `Stop` case (same concept, different name).
- `Free of Instruction` gives us a way to **separate instructions** from the program.
- We can now **group instructions by domain**.

## Separating Instructions by Domain

Instead of one monolithic `Program` type with all instructions, we organize code by domain:

### Folder Structure

```txt
🗃️ Domain/
├──📂 Program/
│  ├──📄 Instructions.fs      # Domain-specific instruction types
│  ├──📄 Program.fs           # Program type and CE
│  └──📄 Environments.fs      # Instruction helpers
└──📂 Workflows/
   ├──📄 CategoryWorkflow.fs
   └──...
```

## Instruction Types and Map Functions

Each domain defines its own instruction type and `map` function:

```fsharp
// -- Program/Instructions.fs ----

// Mail domain instructions
type Mail<'a> =
    | SendMail of MailType * (Result<unit, Error> -> 'a)

module Mail =
    let map f = function
        | SendMail(x, next) -> SendMail(x, (next >> f))

// Partner domain instructions
type Partner<'a> =
    | GetChannelDescription of ChannelId * (ChannelDescription option -> 'a)
    | GetMappingActivation of (ChannelId * CrsHotelId) * ((MappingActivation * LinkStatus) option -> 'a)
    | NotifyLinkEvent of (ChannelId * CrsHotelId * LinkStatus) * (Result<unit, Error> -> 'a)

module Partner =
    let map f = function
        | GetChannelDescription(x, next) -> GetChannelDescription(x, (next >> f))
        | GetMappingActivation(x, next) -> GetMappingActivation(x, (next >> f))
        | NotifyLinkEvent(x, next) -> NotifyLinkEvent(x, (next >> f))
```

Each instruction type:

- Is generic over `'a` (the continuation return type)
- Has a corresponding `map` function that composes the continuation with the mapping function

## The Improved Program Type

Now the `Program` type references domain-specific instruction types:

```fsharp
// -- Program/Program.fs ----

type Program<'a> =
    | Stop of 'a
    | Mail of Instructions.Mail<Program<'a>>
    | Partner of Instructions.Partner<Program<'a>>

module Program =
    let rec bind f = function
        | Stop x -> f x
        | Mail inst -> Mail(inst |> Instruction.Mail.map (bind f))
        | Partner inst -> Partner(inst |> Instruction.Partner.map (bind f))
```

**Benefits:**

- Instructions are organized by domain
- Adding instructions to one domain doesn't affect others
- The `bind` function delegates to domain-specific `map` functions
- Better code organization and maintainability

## Instruction Helpers

As with our V1 program, we define helpers that return a `Program<_>` to facilitate calling instructions when writing *Domain Workflows*:

```fsharp
// -- Program/Environments.fs ----

module Mail =
    let sendMail args =
        Instructions.Mail.SendMail(args, Stop)
        |> Mail

module Partner =
    let getChannelDescription args =
        Instructions.Partner.GetChannelDescription(args, Stop)
        |> Partner

    let getMappingActivation args =
        Instructions.Partner.GetMappingActivation(args, Stop)
        |> Partner

    let notifyLinkEvent args =
        Instructions.Partner.NotifyLinkEvent(args, Stop)
        |> Partner
```

## Working with Domain Errors

Workflows typically return a `Program<Result<xxx, Error>>` where `Error` is a discriminated union:

```fsharp
type Error =
    | DataError of DataRelatedError
    | ValidationError of ValidationError
    // ... other error types
```

### Validation Error Helpers

To call domain type smart constructors within a `program` CE:

```fsharp
// Helpers
let expectValidationError result =
    Result.mapError ValidationError result

let createOrValidationError constructor input =
    constructor input |> expectValidationError

// Domain type with smart constructor
type ChannelId = private ChannelId of int with
    member this.Value = match this with ChannelId i -> i

    static member FromString(str: string) =
        match System.Int32.TryParse(str) with
        | true, value -> ChannelId value |> Ok
        | false, _ -> validationError "ChannelId" $"Should be positive. Value was %s{str}"

// Using in a workflow
program {
    let! channelId = createOrValidationError ChannelId.FromString args.channelId
    // ...
}
```

### Query Helpers

When a query result is required (not optional), we need to handle the `None` case:

```fsharp
// Helpers
let expectDataRelatedError result = 
    Result.mapError DataError result

let inline noneToError id (a: 'a option) =
    let error = DataNotFound(id, $"%s{typeof<'a>.Name}")
    Result.ofOption error a

// Workflow usage
program {
    let! hotelContact = getHotelContact hotelId

    let! hotelContact =  // ⚠️ shadowing the previous binding
        hotelContact
        |> noneToError $"H%s{hotelId.Value}"
        |> expectDataRelatedError
    // ...
}
```

{% hint style="info" %}
#### 💡 Tips

If your `program` doesn't compile and the error message isn't clear, try adding **type annotations** to identify where the unexpected type appears. Once fixed, you can remove unnecessary annotations.

{% endhint %}

## Unit Testing Workflows

Testing a workflow is different from traditional object-oriented unit testing with mocks. We use a **custom interpreter** parameterized through **hooks**.

### The Hook Types

```fsharp
// Command input arguments (to verify what was called)
type HookCalls =
    { NotifiedLinks: (ChannelId * CrsHotelId * LinkStatus) list
      SentMails: Mail.MailType list }

// Query output data (to control what queries return)
type HookData =
    { ChannelDescription: Map<ChannelId, ChannelDescription>
      MappingActivation: Map<ChannelId * CrsHotelId, MappingActivation * LinkStatus> }

type Hooks = 
    { Calls: HookCalls
      Data: HookData }

module Hooks =
    let empty = { Calls = ...; Data = ... }

    let addMappingActivation key value hooks =
        { hooks with 
            Data.MappingActivation = 
                hooks.Data.MappingActivation |> Map.add key value }
    // ... other helper functions
```

### Test Interpreter

The test interpreter executes the program using hook data instead of real dependencies:

```fsharp
let runProgram hooks program =
    let rec loop ({ Calls = calls } as hooks: Hooks) (subprogram: Program<'T>) : Hooks * 'T =
        match subprogram with
        | Stop a -> 
            hooks, a

        | Mail(Instructions.Mail.SendMail(key, next)) ->
            Ok()
            |> next
            |> loop { hooks with Hooks.Calls.SentMails = key :: calls.SentMails }

        | Partner(Instructions.Partner.NotifyLinkEvent(key, next)) ->
            Ok()
            |> next
            |> loop { hooks with Hooks.Calls.NotifiedLinks = key :: calls.NotifiedLinks }

        | Partner(Instructions.Partner.GetChannelDescription(key, next)) ->
            hooks.Data.ChannelDescription
            |> Map.tryFind key
            |> next
            |> loop hooks

        | Partner(Instructions.Partner.GetMappingActivation(key, next)) ->
            hooks.Data.MappingActivation
            |> Map.tryFind key
            |> next
            |> loop hooks

    loop hooks program
```

**Key points:**

- You only need to handle instructions used in your tests
- Unused instructions can return `failwith "not implemented"`
- Commands always return `Ok()` (limitation: can't simulate failures)
- Queries return data from `hooks.Data`
- Command calls are recorded in `hooks.Calls`

### Test Helpers

```fsharp
open Swessen.Unquote

type WorkflowCheck<'success> =
    | FailedWithError of expectedError: Error
    | SucceededWithResult of expectedValue: 'success
    | SucceededWithCalls of expectedCalls: HookCalls

let checkWorkflow check (initialHooks, (hooks, result)) =
    match check with
    | FailedWithError expectedError ->
        result =! Error expectedError
    | SucceededWithResult expectedValue ->
        result =! Ok expectedValue
    | SucceededWithCalls expectedCalls ->
        test <@ Result.isOk result @>
        hooks.Calls =! expectedCalls

let runWorkflow (initialHooks: Hooks) (program: Program<'T>) =
    initialHooks, runProgram initialHooks program
```

### Example Test

```fsharp
open FsCheck
open FsCheck.Xunit

module MappingStatusShould =
    [<Property>]
    let ``be deactivated given related mapping is inactive (XDC)`` channelId deactivatedHotelId =
        let initialHooks =
            Hooks.empty
            |> Hooks.addMappingActivation
                (channelId, deactivatedHotelId)
                (MappingActivation.Deactivated, LinkStatus.NoneOrDeleted)

        PartnerWorkflow.getMappingStatus 
            (deactivatedHotelId, channelId, ChannelCategory.DistributionXdcChannel)
        |> runWorkflow initialHooks
        |> checkWorkflow (SucceededWithResult(Some MappingStatus.Deactivated))
```

## Limitations

Despite the improvements, this pattern still has limitations:

{% hint style="warning" %}

**Domain Coupling:** Instructions are separated by domain but joined back in the `Program` type. Workflows can still use instructions from other domains.

**No Architectural Enforcement:** We cannot perform stricter separation with each domain in its own F# project to achieve screaming architecture or vertical slice architecture.

**Manual Separation:** The distinction between commands and queries is made manually. We cannot use types to enforce type safety and reduce boilerplate code.

{% endhint %}

**How can we improve this pattern to achieve complete domain isolation?**

---
icon: brackets-curly
---

# Effectful Program

This page describes the `Program` V3 implementation inspired by algebraic effects, providing a flexible and type-safe way to handle effectful computations in F#.

## Implementation Context

This version of the `Program` is inspired by algebraic effects implementations in F#. While F# doesn't have native algebraic effects support, we can leverage **generics** and **object-oriented** capabilities to achieve similar benefits.

### F# Algebraic Effects Libraries

Two notable libraries explore algebraic effects, using **generics** and **object-oriented** capabilities of F#:

#### Nick Palladinos' [Eff](https://github.com/palladin/Eff) (2017)

- Hard to use, due to the lack of documentation
- Even harder to understand the implementation
- Pioneering work but not practical for production

#### Brian Berns' [AlgEff](https://github.com/brianberns/AlgEff) (2020)

- 👍 Benefits
  - Less hard to understand and use than the *Eff* repository
  - Based on the free monad, like our `program` V2
  - Very comprehensive, with lots of programming tips
- 🛑 Limits
  - Overkill and too complex for our needs: not a full algebraic effects library, but just an improved `program`
  - Based on class inheritance, programming element to avoid
  - Types defined with `and`, breaking the top-down regular order in F#

👉 This will be the main source.

## Program V3 Design Guidelines

Algebraic effects can be implemented in F# only with generics and object-oriented features, but the implementation should strive to **combine simplicity and type safety**.

### Generics Philosophy

**Generics** can be tricky, especially with constraints and many type parameters.

{% hint style="info" %}
Here, **simplicity trumps type safety**. We prefer clearer code over overly complex generic constraints.
{% endhint %}

### Design Principles

**Interfaces** are the key to abstracting instructions and achieving a truly generic program. We can still recover exhaustiveness checking for supported instructions by downcasting from the generic interface to the union type implementing it. This is a technique found in the *AlgEff* library.

**Interfaces** are the cornerstone of robust object-oriented design. First, interface inheritance is safe, unlike class hierarchies. Second, we can apply the **Interface Segregation Principle** (ISP): prefer short, highly cohesive interfaces, focused on calling code needs, over larger and less focused ones. This is the OO design closest to FP design, as an interface with a single method is essentially a named type wrapping a function. For generics, ISP can be applied to favor decomposing `I<T, U>` into `I1<T>` and `I2<U>` when it makes sense.

**Type aliases** are the second key building block, used to simplify how you define instructions with respect to generic type parameters, without resorting to class inheritance.

## Core Components

This version is composed of more components than previous versions. Each component is the **simplest possible**, designed to do one thing only, making it easier to understand. The difficulty is getting the full picture of how it all works together.

Whenever possible, related components are located near each other, declared top-down to follow the regular order in F#.

### Shopfoo.Effects

The "V3" `program` is located in the [`Shopfoo.Effects` project](https://github.com/rdeneau/shopfoo/tree/main/src/Shopfoo.Effects).

Here a simplified view at the solution level:

```txt
📂 src/
├──📂 Core/
│  ├──🗃️ Shopfoo.Common
│  ├──🗃️ Shopfoo.Domain.Types
│  └──🗃️ Shopfoo.Effects
│     ├──📄 Prelude.fs           👈 Effects, Instructions
│     ├──📄 Program.fs           👈 Program type, program CE
│     └──📄 Interpreter.fs       👈 Interpreter type
├──📂 Feat/
│  ├──🗃️ Shopfoo.Home            👈 Simple features, without workflows
│  └──🗃️ Shopfoo.Product         👈 Complex features, with domain workflows
└──📂 UI/
   ├──🗃️ Shopfoo.Client
   ├──🗃️ Shopfoo.Server
   └──🗃️ Shopfoo.Shared
```

### Program Type: Open to Any Effect

This version of the `Program` is a free monad variation handling any effect that is a functor by implementing the `IProgramEffect<'a>` generic interface:

```fsharp
// Identify an effect that can be inserted in a program.
// The `Map` method satisfies the Functor laws.
[<Interface>]
type IProgramEffect<'a> =
    abstract member Map: f: ('a -> 'b) -> IProgramEffect<'b>

type Program<'ret> =
    // Last step in a program, containing the returned value.
    | Stop of 'ret

    // One step in a program.
    | Effect of IProgramEffect<Program<'ret>>
```

**Key points:**

- `IProgramEffect<'a>` is an interface that any effect must implement.
- The `Map` method makes effects functorial – see [functor laws](https://dev.to/rdeneau/functional-patterns-for-f-computation-expressions-46c7#functor).
- `Program<'ret>` has two cases:
  - `Stop` for the terminal case with the returned value
  - `Effect` for one step containing an effect

### Program Computation Expression

The `ProgramBuilder` class is almost unchanged from V2. Only the `bind` function needs to call the effect's `Map` method:

```fsharp
[<AutoOpen>]
module ProgramBuilder =
    let rec private bind f program =
        match program with
        | Stop x -> f x
        | Effect effect -> effect.Map(bind f) |> Effect

    type ProgramBuilder() =
        member _.Return(x) = Stop x
        member _.Bind(px, f) = bind f px
        member _.ReturnFrom(px) = px
        member _.Zero() = Stop()

    let program = ProgramBuilder()
```

### Effect Holding Instructions

To complement `IProgramEffect<'a>`, we define another interface that links an effect with a set of instructions:

```fsharp
[<Interface>]
type IInterpretableEffect<'union> =
    abstract member Instruction: 'union
```

**Notes:**

- `'union` is usually a union type, but it's not mandatory.
- **Interpretable** because it will be used while interpreting the program.

### Instruction Class

Instructions are defined with a single sealed class `Instruction` replacing the V2 pattern of `Instruction of Arg * cont: (Ret -> 'a)`:

```fsharp
[<Sealed>]
type Instruction<'arg, 'ret, 'a>(name: string, arg: 'arg, cont: 'ret -> 'a) =
    member val Name = name
    member _.Map(f: 'a -> 'b) = Instruction(name, arg, cont >> f)
    member _.Run(runner) = let ret = runner arg in cont ret
```

**Properties and methods:**

- `Name`: Informative, usable for logging or debugging
- `arg`: Private argument(s) for this instruction
- `cont`: Continuation function, passing the result to the next instruction
- `Map`: Functor `map` operation—chains the continuation with the given function
- `Run`: Calls the `runner: 'arg -> 'ret` to get the result (see the `ret` value) and continues with it

{% hint style="info" %}
In the *Shopfoo* repository, the `Instruction` includes also a `RunAsync` method to support asynchronous scenarii – see [Prelude.fs#L84](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Prelude.fs#L84).
{% endhint %}

### Commands and Queries

Commands and queries are instructions but don't inherit from `Instruction` to avoid:

- Class inheritance complexity
- Complex 3 type parameters passing

Instead, they're defined through simple **type aliases** that work as constructors:

```fsharp
type Command<'arg, 'a>             = Instruction<'arg, Result<unit, Error>, 'a>
type Query<'arg, 'ret, 'a>         = Instruction<'arg, 'ret option,         'a>
type QueryFailable<'arg, 'ret, 'a> = Instruction<'arg, Result<'ret, Error>, 'a>
```

**Type conventions:**

- **Commands:** Return `Result<unit, Error>` (no output data, only success/error)
- **Queries:** Return `'ret option` (where `None` means not found)
- **QueryFailable:** Return `Result<'ret, Error>` (for queries that can fail with errors)

### Workflows

A domain workflow will be implemented as a class placed in its own file. This practice, which is common in C#, is not at all common in F#. Here, it offers the following benefits:

- The file tree displays the list of workflows supported by the domain. This brings us closer to "Screaming Architecture."
- The class implementing a workflow should implement a marker interface that identifies the domain. This way, the program interpreter—which we will discuss in more detail shortly—will be dedicated to that domain and will be able to deduce the list of supported instructions.

Let's analyze the interfaces that support this principle:

```fsharp
[<Interface>]
type IDomain =
    abstract member Name: string

[<Interface>]
type IDomainWorkflow<'dom when 'dom :> IDomain> =
    abstract member Domain: 'dom

[<Interface>]
type IProgramWorkflow<'arg, 'ret> =
    abstract member Run: 'arg -> Program<Result<'ret, Error>>
```

The design is structured around three interfaces:

- `IDomain` is almost a marker interface, requiring to implement the domain `Name`, mainly for observability purposes. For each, we will create a dedicated type, typically a single-case union `type MyDomain = MyDomain`, implementing `IDomain`—more details to come.
- The two other interfaces—`IDomainWorkflow<'dom>` and `IProgramWorkflow<'arg, 'ret>`—must be implemented by each domain workflow. They have been split in two to follow the *Interface Segregation Principles*, as explained in the design guidelines above.
  - `IDomainWorkflow<'dom>` is also a kind of marker interface, indicating the underlying `Domain`.
  - `IProgramWorkflow<'arg, 'ret>` introduces the `Run` method to implement by each workflow, using the `program` computation expression—hence the return type: `Program<Result<'ret, Error>>`.

{% hint style="info" %}
#### Note

In this design, there is a single type—`Error`—for the entire solution. To get separated error types by domain, you can replace the `Result` type by following the [Fault Report pattern](https://paul.blasuc.ci/posts/fault-report.html), another nice F# design mixing FP and OOP described by Paul Blasucci.

{% endhint %}

### Interpreter

The `Interpreter` is a sealed class, with a single instance per domain. Although using a regular F# module would have been possible, this class-based design offers the following advantages:

- A class fits nicely with the *Safe Clean Architecture* and its usage of *Dependency Injection*, especially when you need more dependencies like a logger.
- This "closure" over a domain would not be possible with a design based on an F# module.

Let's review the code:

```fsharp
[<Sealed>]
type Interpreter<'dom when 'dom :> IDomain>(domain: 'dom) =
    member private _.Instruction(instruction: Instruction<_, _, _>, pipeline: 'arg -> Async<_>) =
        instruction.RunAsync(pipeline)

    member this.Command(command: Command<_, _>, pipeline) =
        this.Instruction(command, pipeline)

    member this.Query(query: Query<_, _, _>, pipeline) =
        this.Instruction(query, pipeline)

    member this.QueryFailable(query: QueryFailable<_, _, _>, pipeline) =
        this.Instruction(query, pipeline)

    member _.Workflow<'arg, 'ret, 'effect, 'workflow
        when 'effect :> IProgramEffect<Program<Result<'ret, Error>>>
        and 'workflow :> IProgramWorkflow<'arg, 'ret>
        and 'workflow :> IDomainWorkflow<'dom>>
        runEffect
        =
        let rec loop program =
            match program with
            | Stop res -> async { return res }
            | Effect eff ->
                match eff with
                | :? 'effect as effect ->
                    async {
                        let! res = runEffect effect
                        return! loop res
                    }
                | _ -> failwithf $"Unsupported effect: %A{eff}"

        fun (workflow: IProgramWorkflow<'arg, 'ret>) (arg: 'arg) ->
            async {
                try
                    let program = workflow.Run(arg)
                    return! loop program
                with FirstException exn ->
                    return bug exn
            }
```

The `Interpreter` class provides two kind of methods, depending on what to interpret:

- The `Command`, `Query`, and `QueryFailable` methods are used to interpret a single instruction. Their main purpose is to allow to differentiate the type of instruction, their name reproducing the related type alias. Looking at the implementation, they are basically shortcuts to call the `RunAsync` of the given instruction.
- The `Workflow` method interprets a whole workflow from the current domain. It is defined in two steps:
  1. A recursive inner function that gives the workflow program return value, eventually looping through the program instruction by instruction, running the related asynchronous effect using the given `runEffect` parameter provided by the domain project.
  2. A returned lambda that runs the given `workflow` to build the related `program` to interpret asynchronously using the `loop` inner function, catching an eventual exception and wrapping it in a `Result.Error` containing the `Bug` case of the `Error` union type – see [bug helper function](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Domain.Types/Errors.fs#L148).

{% hint style="info" %}
In the *Shopfoo* repository, the actual `Interpreter` includes other elements related to observability, not indicated here for brevity sakes – see [Interpreter.fs#L23](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Interpreter.fs#L23).
{% endhint %}

## Conclusion

The V3 `program` brings the following benefits, compared to the V2:

- **Type Safety**
  - Effects are strongly typed through interfaces
  - Commands and queries are distinguished at the type level
- **Flexibility**
  - Open to any effect implementing `IProgramEffect<'a>`
  - Domain-specific effects can be created in isolation, without modifying core code
- **Simplicity**
  - Each component has a single responsibility
  - Type aliases reduce boilerplate
  - Regular top-down declaration order

It's time to put this mini-library into practice by implementing domain workflows. If parts of the V3 `program` still seem confusing, this should help clarify things.

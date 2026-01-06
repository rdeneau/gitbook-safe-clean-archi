---
icon: brackets-curly
---

# Effectful Program

This page describes the `Program` V3 implementation, inspired by algebraic effects, which provides a flexible and type-safe approach to handling effectful computations in F#.

## Implementation Context

This version of `Program` draws inspiration from algebraic effects implementations in F#. While F# lacks native algebraic effects support, we leverage **generics** and **object-oriented** capabilities to achieve similar benefits.

### F# Algebraic Effects Libraries

Two notable libraries explore algebraic effects, using **generics** and **object-oriented** capabilities of F#:

#### Nick Palladinos' [Eff](https://github.com/palladin/Eff) (2017)

- Difficult to use due to lack of documentation
- Implementation is challenging to understand
- Pioneering work, but not practical for production use

#### Brian Berns' [AlgEff](https://github.com/brianberns/AlgEff) (2020)

- 👍 Benefits
  - Easier to understand and use than the *Eff* library
  - Based on the free monad, similarly to our `program` V2
  - Comprehensive with numerous programming tips
- 🛑 Limitations
  - Overly complex for our needs: not a full algebraic effects library, but an improved `program` implementation
  - Relies on class inheritance, which we prefer to avoid
  - Uses `and` for type definitions, breaking F#'s conventional top-down declaration order

👉 This serves as our primary reference.

## Program V3 Design Guidelines

Algebraic effects in F# can only be implemented using generics and object-oriented features, but the implementation should **balance simplicity with type safety**.

### Generics Philosophy

**Generics** can become complex, especially when dealing with constraints and multiple type parameters.

{% hint style="info" %}
In this implementation, **simplicity takes precedence over type safety**. We favor clearer, more maintainable code over more complex generic constraints.
{% endhint %}

### Design Principles

**Interfaces** are essential for abstracting instructions and achieving a truly generic program. We can maintain exhaustiveness checking for supported instructions by downcasting from the generic interface to the implementing union type—a technique borrowed from the *AlgEff* library.

**Interfaces** form the cornerstone of robust object-oriented design. First, interface inheritance is safer than class hierarchies. Second, we apply the **Interface Segregation Principle** (ISP): favor short, highly cohesive interfaces focused on caller needs over larger, less focused ones. This OO approach closely resembles FP design, since a single-method interface is essentially a named type wrapping a function. For generics, ISP encourages decomposing `I<T, U>` into `I1<T>` and `I2<U>` when appropriate.

**Type aliases** serve as the second key building block, simplifying instruction definitions with respect to generic type parameters without relying on class inheritance.

## Core Components

This version comprises more components than previous versions. Each component is **as simple as possible**, designed with a single responsibility to enhance understanding. The challenge lies in grasping how all components work together.

Whenever possible, related components are co-located and declared top-down, following F#'s conventional order.

### Shopfoo code

The V3 `program` implementation resides in the [`Shopfoo.Effects` project](https://github.com/rdeneau/shopfoo/tree/main/src/Shopfoo.Effects).

Here's a simplified view of the solution structure:

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

This version of `Program` is a free monad variant that handles any effect implementing the `IProgramEffect<'a>` generic interface as a functor:

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

- `IProgramEffect<'a>`: Interface that all effects must implement
- `Map` method: Makes effects functorial (see [functor laws](https://dev.to/rdeneau/functional-patterns-for-f-computation-expressions-46c7#functor))
- `Program<'ret>` cases:
  - `Stop`: Terminal case containing the returned value
  - `Effect`: Single program step containing an effect

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

{% hint style="info" %}
The `Program.fs` file in the *Shopfoo* application ([source code](https://github.com/rdeneau/shopfoo/blob/96d8eb77072ec60ab2989fec96a2fa86b1867b34/src/Shopfoo.Effects/Program.fs)) contains additional elements to facilitate `program` composition. These will be explored when analyzing characteristic workflows.
{% endhint %}

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

- `Name`: Descriptive label useful for logging and debugging
- `arg`: Private argument(s) for this instruction
- `cont`: Continuation function that passes results to the next instruction
- `Map`: Functor `map` operation that chains the continuation with a given function
- `Run`: Invokes `runner: 'arg -> 'ret` to obtain the result (`ret` value) and continues execution

{% hint style="info" %}
The *Shopfoo* repository's `Instruction` also includes a `RunAsync` method for asynchronous scenarios—see [Prelude.fs#L84](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Prelude.fs#L84).
{% endhint %}

{% hint style="warning" %}
The signature of `Map` is not strict: a true `map` must preserve the type of the functor; only the type parameter (corresponding to the content of the functor) is mapped. This constraint cannot be written in F#, whereas it is possible in C#, except that it would make the code much more cumbersome. We must therefore accept this and be careful when implementing the method in order to comply with the definition of the functor.
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

Each domain workflow is implemented as a class in its own file. While uncommon in F#, this practice (standard in C#) provides the following benefits:

- The file tree reveals the workflows supported by each domain, approaching "Screaming Architecture".
- Each workflow class implements a marker interface identifying its domain, enabling the program interpreter (discussed below) to remain domain-specific and infer the list of supported instructions.

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

The design relies on three interfaces:

- `IDomain`: Nearly a marker interface, requiring implementation of the domain `Name` property primarily for observability. We create a dedicated type for each domain, typically a single-case union like `type MyDomain = MyDomain`, implementing `IDomain` (details follow).
- `IDomainWorkflow<'dom>` and `IProgramWorkflow<'arg, 'ret>`: Both must be implemented by each domain workflow. They're separated to follow the *Interface Segregation Principle*, as described in the design guidelines.
  - `IDomainWorkflow<'dom>`: Another marker interface indicating the underlying `Domain`
  - `IProgramWorkflow<'arg, 'ret>`: Introduces the `Run` method that each workflow must implement using the `program` computation expression, resulting in the return type `Program<Result<'ret, Error>>`

{% hint style="info" %}
#### Note

This design uses a single `Error` type across the entire solution. To achieve domain-specific error types, consider replacing `Result` with the [Fault Report pattern](https://paul.blasuc.ci/posts/fault-report.html), an elegant F# design combining FP and OOP principles, described by Paul Blasucci.
{% endhint %}

### Interpreter

The `Interpreter` is a sealed class with a single instance per domain. While an F# module could have been used, the class-based design provides these advantages:

- Better alignment with *Safe Clean Architecture* and *Dependency Injection*, particularly when additional dependencies (e.g., loggers) are required
- Domain-specific "closure" behavior that wouldn't be achievable with an F# module

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

The `Interpreter` class provides two categories of methods based on what's being interpreted:

- **Single instruction methods** (`Command`, `Query`, `QueryFailable`): These interpret individual instructions. Their primary purpose is type differentiation, with names mirroring their corresponding type aliases. They're essentially convenient wrappers that invoke `RunAsync` on the given instruction.
- **Workflow method**: Interprets an entire workflow from the current domain, implemented in two steps:
  1. A recursive inner function returning the workflow program's result, iterating through instructions one by one and executing the associated asynchronous effect via the `runEffect` parameter provided by the domain project
  2. A returned lambda that executes the given `workflow` to construct the `program`, which is then interpreted asynchronously using the `loop` inner function. Any exceptions are caught and wrapped in a `Result.Error` containing the `Bug` case of the `Error` union type (see [bug helper function](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Domain.Types/Errors.fs#L148))

{% hint style="info" %}
The *Shopfoo* repository's `Interpreter` includes additional observability features omitted here for brevity—see [Interpreter.fs#L23](https://github.com/rdeneau/shopfoo/blob/main/src/Shopfoo.Effects/Interpreter.fs#L23).
{% endhint %}

## Conclusion

The V3 `program` provides significant improvements over V2:

- **Type Safety**
  - Effects are strongly typed via interfaces
  - Commands and queries are distinguished at the type level
- **Flexibility**
  - Supports any effect implementing `IProgramEffect<'a>`
  - Domain-specific effects can be developed in isolation without modifying core code
- **Simplicity**
  - Single-responsibility components
  - Type aliases minimize boilerplate
  - Conventional top-down declaration order

Applying this framework to domain workflow implementation will further clarify any remaining aspects of the V3 `program`.

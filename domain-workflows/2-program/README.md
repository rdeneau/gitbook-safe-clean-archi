---
icon: brackets-curly
---

# Program

This page describes the V4 `program` implementation, based on the **Tagless Final** pattern and an **async reader** design. It resides entirely in the [`Shopfoo.Program`](https://github.com/rdeneau/shopfoo/tree/main/src/Shopfoo.Program) project.

## Solution Structure

```txt
📂 src/
├──📂 Core/
│  ├──🗃️ Shopfoo.Common
│  ├──🗃️ Shopfoo.Domain.Types
│  └──🗃️ Shopfoo.Program           👈 Program infrastructure
│     ├──📄 Program.fs             👈 Program type, program CE
│     ├──📄 Saga.fs                👈 Undo types and logic
│     ├──📄 Runner.fs              👈 Workflow runner, instruction preparer
│     ├──📄 Metrics.fs             👈 Observability
│     └──📄 Dependencies.fs        👈 DI registration
├──📂 Feat/
│  ├──🗃️ Shopfoo.Home              👈 Simple features, without workflows
│  └──🗃️ Shopfoo.Product           👈 Complex features, with domain workflows
└──📂 UI/
   ├──🗃️ Shopfoo.Client
   ├──🗃️ Shopfoo.Server
   └──🗃️ Shopfoo.Shared
```

## Program Type

The `Program` type is a **type alias** — a function that takes an instruction set and returns an asynchronous result:

```fsharp
/// Marker interface to identify the set of instructions for a program.
type IProgramInstructions = interface end

/// Type alias for the constraint `'ins when 'ins :> IProgramInstructions`.
type Instructions<'ins when 'ins :> IProgramInstructions> = 'ins

/// A program that, given a set of instructions ('ins), produces an async result.
/// This is the ReaderT monad where the environment is the instruction set.
type Program<'ins, 'ret when Instructions<'ins>> = 'ins -> Async<'ret>

/// Shorthand for a result that may fail with the domain Error type.
type Res<'t> = Result<'t, Error>
```

**Key points:**

- `IProgramInstructions`: Marker interface that all instruction sets must inherit.
- `Instructions<'ins>`: Type alias encapsulating the constraint, so it can be changed in one place.
- `Program<'ins, 'ret>`: A function from instructions to an async result. This replaces the V3 free monad ADT (`Stop | Effect`) with a direct function.
- `Res<'t>`: Shorthand alias for `Result<'t, Error>`, used throughout the codebase wherever a computation may fail with a domain error.

## Program Module

The `Program` module provides the standard functional combinators:

```fsharp
[<RequireQualifiedAccess>]
module Program =
    let retn (a: 'a) : Program<'ins, 'a> =
        fun _ -> async { return a }

    let bind f (prog: Program<'ins, 'a>) : Program<'ins, 'b> =
        fun ins ->
            async {
                let! a = prog ins
                return! f a ins
            }

    let map (f: 'a -> 'b) (prog: Program<'ins, 'a>) : Program<'ins, 'b> =
        fun ins ->
            async {
                let! x = prog ins
                return f x
            }

    let map2 (f: 'a -> 'b -> 'c)
             (progA: Program<'ins, 'a>)
             (progB: Program<'ins, 'b>)
             : Program<'ins, 'c> =
        fun ins ->
            async {
                let! childTaskA = Async.StartChild(progA ins)
                let! b = progB ins
                let! a = childTaskA
                return f a b
            }
```

The `map2` function is essential: it enables **parallel execution** of two independent programs by starting the first as a child async computation, running the second, and then awaiting the first. This is what powers the applicative `let! ... and! ...` syntax.

## Computation Expression

The `ProgramBuilder` class provides the `program` CE:

```fsharp
type ProgramBuilder() =
    member _.Return(a: 'a) : Program<'ins, 'a> = Program.retn a
    member _.ReturnFrom(prog: Program<'ins, 'a>) = prog
    member _.Bind(prog: Program<'ins, 'a>, f: 'a -> Program<'ins, 'b>) = Program.bind f prog

    // Applicative support for parallel execution
    member _.Bind2Return(progA, progB, f) =
        Program.map2 (fun a b -> f (a, b)) progA progB
    member this.MergeSources(progA, progB) =
        this.Bind2Return(progA, progB, id)

let program = ProgramBuilder()
```

**Applicative syntax:**

- `Bind2Return` and `MergeSources` are the CE methods that enable `let! ... and! ...`. When the compiler sees `let! a = exprA` followed by `and! b = exprB`, it calls `MergeSources` (which calls `map2`) to run both computations concurrently.
- This is impossible with `Bind` alone, which is inherently sequential.

### Result Bind Overloads

The CE provides multiple `Bind` overloads so that `Result` values integrate seamlessly with programs:

```fsharp
type ProgramBuilder with
    // Bind a Program<Result<_, _>> — unwrap the Ok track, short-circuit on Error
    member _.Bind(progR: Program<'ins, Result<_, _>>, f) = progR >>= (bindResult f)

    // Bind a plain Result (e.g., from a domain type smart constructor)
    member _.Bind(result: Result<_, _>, f) = result |> bindResult f

    // Overloads for specific error types, lifting them to the common Error type
    member inline x.Bind(result: Result<_, DataRelatedError>, f) = ...
    member inline x.Bind(result: Result<_, OperationNotAllowedError>, f) = ...
    member inline x.Bind(result: Result<_, GuardClauseError>, f) = ...
```

This design eliminates manual error-track management in workflows — the CE handles the lifting and short-circuiting automatically.

### Result Aggregation

The CE does **not** provide a `MergeSources` overload for `Result`. Parallel applicative syntax (`let! ... and! ...`) only applies to `Program` values; it cannot aggregate multiple `Result` values into a combined result, collecting all errors.

Result aggregation must be handled **case by case** in the program, using the `Result.zip` helper:

```fsharp
/// Combines two Results into a pair on the Ok track,
/// or merges both errors into the Errors case of the domain Error type.
let Result.zip : Result<'a, Error> -> Result<'b, Error> -> Result<'a * 'b, Error>
```

Typical usage — validating several independent values before proceeding:

```fsharp
program {
    let r1 = ProductName.create name      // Result<ProductName, Error>
    let r2 = ProductPrice.create price    // Result<ProductPrice, Error>
    let! name, price = Result.zip r1 r2   // short-circuits if either fails, collecting both errors
    ...
}
```

If more than two results need to be combined, `Result.zip` can be chained, or a dedicated `Result.zipN` helper can be introduced for the specific arity.

### Defining Programs from Instructions

The `DefineProgram<'ins>` static class provides a single method to define a program from a single instruction:

```fsharp
type DefineProgram<'ins when Instructions<'ins>> =
    static member inline instruction
        ([<InlineIfLambda>] work: 'ins -> Async<'ret>)
        : Program<'ins, 'ret> = work
```

This is an **identity function** — it exists purely for developer experience. Usage:

```fsharp
type private DefineProgram = DefineProgram<IProductInstructions>

let getPrices sku   = DefineProgram.instruction _.GetPrices(sku)
let savePrices p    = DefineProgram.instruction _.SavePrices(p)
let addProduct prod = DefineProgram.instruction _.AddProduct(prod)
```

The type alias `DefineProgram = DefineProgram<IProductInstructions>` fixes the instruction set, so each call only requires the shorthand lambda `_.Method(args)` — which triggers IntelliSense on the instruction interface. This is perhaps the most ergonomic part of the whole design.

## Saga Support (Undo)

The `Saga.fs` file provides the types and logic for undoing completed instructions when a workflow fails — implementing the **Saga pattern** for synchronous, in-process workflows (no message bus involved).

### Undo Types

```fsharp
type UndoFunc([<InlineIfLambda>] func: unit -> Async<Res<unit>>) =
    member _.Invoke() = func ()

[<RequireQualifiedAccess>]
type Undo =
    | None
    | Revert of UndoFunc      // Strict undo: DELETE to reverse an INSERT
    | Compensate of UndoFunc  // Loose undo: RefundPayment to compensate ChargePayment
```

Each command instruction can be marked with an undo strategy:

- `Undo.None`: Not undoable (e.g., sending a notification).
- `Undo.Revert`: Strict reversal — restores the initial state exactly.
- `Undo.Compensate`: Compensation — produces a new operation that logically offsets the original.

### Step Tracking

Every instruction execution is recorded as a `ProgramStep`:

```fsharp
type ProgramStep = { Instruction: InstructionMeta; Status: StepStatus }

type StepStatus =
    | RunDone
    | RunFailed of runError: Error
    | UndoDone
    | UndoFailed of undoError: Error
```

The saga maintains a **LIFO history** of steps. On failure, it walks the history in reverse and executes each command's undo function.

### Saga Finalization

The `Saga.finalize` function determines whether to undo and executes the undo phase:

```fsharp
let finalize (canUndo: CanUndo) (result: Res<'t>) (history: ProgramStep list) : Async<SagaState>
```

It handles several cases:

- **Success:** Status = `Done`, no undo needed.
- **Workflow cancelled:** Status = `Cancelled`, no undo (cancellation is intentional).
- **Failure but undo not allowed** (predicate returns false): Status = `Failed`, no undo.
- **Failure with undo allowed:** Walks steps in reverse, executes undo functions, collects any undo errors.

## Workflow Runner

The `Runner.fs` file defines the infrastructure for running workflows with instruction preparation, monitoring, and saga integration.

### Instruction Preparer

The `IInstructionPreparer<'ins>` interface wraps raw instruction functions with monitoring and undo tracking:

```fsharp
[<Interface>]
type IInstructionPreparer<'ins when Instructions<'ins>> =
    abstract member Query:
        work: Work<'arg, 'ret option> * getName: ('arg -> string) -> Work<'arg, 'ret option>
    abstract member Command:
        work: Work<'arg, Res<'ret>> * getName: ('arg -> string) -> IWorkCommandBuilder<'arg, 'ret>
```

For **queries**, it wraps the raw function with logging, timing, and step tracking.

For **commands**, it returns an `IWorkCommandBuilder<'arg, 'ret>` on which the caller then specifies the undo strategy:

```fsharp
[<Interface>]
type IWorkCommandBuilder<'arg, 'ret> =
    abstract member NotUndoable: unit -> Work<'arg, Res<'ret>>
    abstract member Reversible: undoFun: ('arg -> 'ret -> Async<Res<unit>>) -> Work<'arg, Res<'ret>>
    abstract member Compensatable: undoFun: ('arg -> 'ret -> Async<Res<unit>>) -> Work<'arg, Res<'ret>>
```

The reason this is a **separate interface** — rather than a single `Command` method that takes both `work` and `undoFun` — is **F# type inference**. F# infers generic type parameters left-to-right within an expression, but it does not propagate inferred types across the arguments of a single method call. If `work` and `undoFun` were both parameters of `Command`, the compiler would not know `'arg` and `'ret` when it typechecks `undoFun`, forcing the caller to annotate the lambda explicitly.

By splitting into two calls, `Command(work, name)` infers `'arg` and `'ret` from `work`, and the returned `IWorkCommandBuilder<'arg, 'ret>` carries those types. The `undoFun` lambda passed to `.Reversible(...)` or `.Compensatable(...)` is then fully typed — resulting in clean, annotation-free code:

```fsharp
// 'arg = Prices, 'ret = unit — inferred from pricesPipeline.AddPrices
member _.AddPrices =
    preparer
        .Command(pricesPipeline.AddPrices, "AddPrices")
        .Reversible(fun prices _ -> pricesPipeline.DeletePrices prices.SKU)
        //           ^^^^^^ typed as Prices — no annotation needed

// 'ret = PreviousValue<Prices> — inferred from pricesPipeline.SavePrices
member _.SavePrices =
    preparer
        .Command(pricesPipeline.SavePrices, "SavePrices")
        .Reversible(fun _ (PreviousValue initialPrices) ->
            async {
                let! res = pricesPipeline.SavePrices initialPrices
                return res |> Result.ignore
            })
        //      ^^^^^^^^^^^^^^^^^^^ pattern match works without annotation
```

The fluent style is a pleasant side-effect, common in C#, but the primary motivation is type inference ergonomics.

### Workflow Runner

The `IWorkflowRunner<'ins>` interface provides two execution modes:

```fsharp
[<Interface>]
type IWorkflowRunner<'ins when Instructions<'ins>> =
    abstract member Run:
        workflow: #IProgramWorkflow<'ins, 'arg, 'ret> ->
        arg: 'arg ->
        prepareInstructions: (IInstructionPreparer<'ins> -> 'ins) ->
            Async<Result<'ret, Error>>

    abstract member RunInSaga:
        workflow: #IProgramWorkflow<'ins, 'arg, 'ret> ->
        arg: 'arg ->
        prepareInstructions: (IInstructionPreparer<'ins> -> 'ins) ->
        undoPredicate: CanUndo ->
            Async<Result<'ret, Error> * SagaState>
```

- `Run`: Simple execution without undo support.
- `RunInSaga`: Execution with undo support — returns both the result and the saga state (history of all steps and their statuses).

The `prepareInstructions` parameter is a function that receives an `IInstructionPreparer` and returns the instruction set implementation. This is where each instruction is wired to its data-layer function and its undo strategy. This wiring happens in the domain's `Api` class.

## Dependency Injection

The `Dependencies.fs` file provides the DI registration:

```fsharp
type IServiceCollection with
    member services.AddProgram() =
        services
            .AddSingleton<IMetricsSender, MetricsLogger>()
            .AddSingleton<IWorkMonitors, WorkMonitors>()
            .AddSingleton<IWorkflowRunnerFactory, WorkflowRunnerFactory>()
```

The concrete types (`MetricsLogger`, `WorkMonitors`, `WorkflowRunnerFactory`) are defined inside a `module private Implementation` block, making them invisible outside the `Shopfoo.Program` assembly. Only the interfaces (`IMetricsSender`, `IWorkMonitors`, `IWorkflowRunnerFactory`) are public. This brings three benefits:

- **Encapsulation:** Implementation details cannot be referenced directly by consumers — they can only be reached through the interfaces.
- **Reduced API surface:** The public contract is limited to what callers actually need, preventing accidental coupling to internals.
- **Dependency inversion (SOLID):** Domain and feature projects depend on the abstractions, not the concrete types. Implementations can change or be swapped (e.g., for testing) without touching any call site.

The `IWorkflowRunnerFactory` creates `IWorkflowRunner<'ins>` instances for a given domain name. Each domain project calls `workflowRunnerFactory.Create(domainName)` to obtain its runner.

## Summary

| Component         | Responsibility                                                    |
| ----------------- | ----------------------------------------------------------------- |
| `Program.fs`      | Program type (async reader), CE builder with applicative support  |
| `Saga.fs`         | Undo types (`Undo`, `UndoFunc`), step tracking, saga finalization |
| `Runner.fs`       | Workflow runner, instruction preparer with monitoring and undo    |
| `Metrics.fs`      | Timing and metrics for instructions                               |
| `Dependencies.fs` | DI registration                                                   |

The V4 program achieves:

- **Radical simplicity:** Program is a function, not an ADT.
- **Parallel execution:** `let! ... and! ...` runs instructions concurrently via `map2`.
- **Undo support:** Saga pattern with reversible and compensatable commands.
- **Observability:** Logging, timing, and step tracking built into the runner infrastructure.
- **Domain isolation:** Each domain defines its own instruction interface and wires it independently.

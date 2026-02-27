---
icon: diamonds-4
---

# Algebraic Effects

**Algebraic Effects** represent the most advanced approach to managing effectful domain workflows in F#. This is version 3 of our `program` computation expression, achieving complete domain isolation and type safety. This page introduces the theoretical foundations of algebraic effects.

## The Core Principle: *What* vs *How*

Algebraic effects are based on a fundamental separation:

- **What:** Operation declarations (the effects we want to perform)
- **How:** Operation implementations (the handlers that execute effects)

This separation is more powerful and flexible than what we achieved with the *Free Monad* pattern.

## Historical Context

Algebraic effects are a relatively recent area of research compared to monads:

### Monads Timeline

- **1991:** Paper *"Computational Lambda-Calculus and Monads"*
- **1992:** Integration into Haskell

### Algebraic Effects Timeline

- **2009:** Paper *"Handlers of Algebraic Effects"*
- **2012:** Eff language created for research
- **2014-2022:** Development of Multicore OCaml with effect handlers and user-defined effects
- **2023:** OCaml 5.3 introduces `try ... with effect ...` [syntax](https://ocaml.org/manual/5.3/effects.html)

{% hint style="info" %}
Algebraic effects are a newer research domain than monads, with language support still evolving.
{% endhint %}

## Algebraic Effects vs Free Monad

Both patterns share the same goal: separating the "what" from the "how."

### Free Monad Approach

- **Ad hoc solution** built with dedicated types
- We saw this with our `Instructions` and `Program` types in Version 2
- Requires manual construction of the entire machinery
- Each instruction type needs its own `map` function

### Algebraic Effects Approach

- **Native language feature** in languages that support it
- Highly optimized by the runtime
- **Composable handlers** that allow capabilities difficult to model with Free monads:
  - Non-local control flow
  - Generators and coroutines
  - Resumable exceptions
  - Effect polymorphism

{% hint style="info" %}
In languages with native support, algebraic effects are both more powerful and more ergonomic than Free monads.
{% endhint %}

## Algebraic Effects vs Monad Transformers

Monad transformers (like `ReaderT`, `StateT`, `ExceptT`) are another approach to composing effects, but they have significant drawbacks.

### Monad Transformer Drawbacks

#### 1. Boilerplate & Complexity

Stacking transformers creates complex, verbose types:

```haskell
-- haskell
type MyApp = ReaderT Config (StateT AppState (ExceptT Error IO)) a
```

#### 2. Constant Lifting

Operations must be lifted from inner monads to outer layers:

```haskell
-- haskell
liftIO $ putStrLn "Hello"  -- Lift IO to the outer monad stack
lift $ lift $ someOperation -- Multiple lifts for deeply nested monads
```

#### 3. The n² Problem

For `n` monadic effects, you potentially need `n²` different monad transformer combinations, each with its own instance definitions.

#### 4. Rigid Stacks

The order of transformers matters and is fixed. Reordering requires changing types throughout your codebase.

### Algebraic Effects Solution

With algebraic effects, you work with a **set of capabilities** rather than a stack:

- A function declares that it needs both `Reader` and `State` effects
- A handler can handle both, or just one (letting the other propagate up the call stack)
- Effects compose naturally without explicit lifting
- The order of handlers is flexible

**Example concept (in a language with native support):**

```fsharp
// Hypothetical F# with native algebraic effects
let myWorkflow () : Effect<Reader Config, State AppState, Error> =
    effect {
        let! config = ask()           // Reader effect
        let! currentState = get()     // State effect
        do! put(newState)             // State effect
        // No lifting needed!
    }
```

## Key Differences Summary

| Aspect                  | Monad Transformers      | Algebraic Effects            |
| ----------------------- | ----------------------- | ---------------------------- |
| **Composition**         | Stack (ordered)         | Set (unordered)              |
| **Lifting**             | Manual, explicit        | Automatic                    |
| **Type complexity**     | Grows with stack depth  | Remains flat                 |
| **Handler flexibility** | Fixed stack order       | Handlers compose freely      |
| **Performance**         | Interpretation overhead | Native optimization possible |
| **Extensibility**       | n² problem              | Open set of effects          |

## V3 Implementation in F# (Historical)

Since F# lacks native algebraic effects, the V3 `program` was an attempt to emulate them using generics and object-oriented features. This section is kept for historical reference, as V4 replaced this approach with the simpler **Tagless Final** pattern.

### F# Algebraic Effects Libraries

Two notable libraries explored algebraic effects using **generics** and **object-oriented** capabilities of F#:

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

### V3 Design

The V3 `Program` was a free monad variant where effects implemented a functor interface:

```fsharp
[<Interface>]
type IProgramEffect<'a> =
    abstract member Map: f: ('a -> 'b) -> IProgramEffect<'b>

type Program<'ret> =
    | Stop of 'ret
    | Effect of IProgramEffect<Program<'ret>>
```

Each domain defined:

1. **Type aliases** for query/command instructions (e.g., `type GetPricesQuery<'a> = Query<SKU, Prices, 'a>`)
2. A **discriminated union** enumerating all instructions (`ProductInstruction<'a>`)
3. An **effect interface** combining `IProgramEffect<'a>` with `IInterpretableEffect<ProductInstruction<'a>>`
4. An **effect class per instruction** implementing the effect interface
5. **Helper functions** using `Program.effect` to create programs from instructions

An `Interpreter` class then recursively walked the `Program` tree, pattern matching on effects and dispatching to the data layer.

### V3 Limitations

While V3 achieved domain isolation and type safety, it had significant drawbacks:

- **Boilerplate:** Each instruction required a type alias, a union case, an effect class (4 lines), and a helper function—a 5-step recipe.
- **No parallel execution:** The `IProgramEffect<'a>` interface's `Map` method makes the type essentially a functor, but implementing `map2` (needed for `let! ... and! ...` applicative syntax) proved impossible due to F#'s strict variance checking on generics. Parallel execution of independent instructions was not achievable.
- **Complex interpreter:** The recursive `loop` function with downcasting (`match eff with | :? 'effect as effect ->`) was fragile and not extensible.
- **Many moving parts:** Effects, instructions, interpreters, factories—understanding how all components worked together was the main challenge.

These limitations motivated the move to V4, based on a radically simpler approach: the **Tagless Final** pattern.

## What's Next

The next page introduces the [Tagless Final](5-tagless-final.md) pattern (V4), which replaced this entire machinery with a single function type and an instruction interface—dramatically reducing complexity while enabling parallel instruction execution.

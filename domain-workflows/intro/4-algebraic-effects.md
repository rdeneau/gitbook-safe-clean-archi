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

## What's Next

Since F# doesn't have native algebraic effect support, we'll need to implement the pattern ourselves using F#'s generics and object-oriented capabilities. Our main source of inspiration will be Brian Berns' [AlgEff](https://github.com/brianberns/AlgEff) repository, which is very comprehensive, with lots of programming tips, but also with programming elements to avoid, such as class inheritance and interdependent types (instead of types declared top-down, the regular order in F#), and also too complex for our needs.

In the following sections, we'll see how to:

1. Design a `Program` type inspired by algebraic effects
2. Create effect interfaces for type safety
3. Implement domain-specific effect handlers
4. Achieve complete domain isolation with separate projects
5. Build interpreters that compose effect handlers

The implementation will be more complex than Free monads, but it will provide:

- Complete separation between domains
- Type-safe effect composition
- Screaming architecture / vertical slice architecture support
- Clear distinction between commands and queries

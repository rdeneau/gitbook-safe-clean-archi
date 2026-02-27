---
icon: presentation-screen
---

# Introduction

This section explores four progressive approaches to handling effectful domain workflows in F#, each building upon the limitations of the previous one. Understanding this evolution will help you choose the right pattern for managing dependencies and side effects in your functional applications.

## The Challenge

In object-oriented programming, the building blocks are objects and classes. When a class collaborates with other classes, these collaborators are called **dependencies**. Managing these dependencies effectively—especially when they involve side effects like database access, HTTP calls, or file I/O—is crucial for building testable, maintainable applications.

## Five Approaches

This introduction presents five progressive solutions to the dependency problem:

| Approach                                                        | Version      | Key Concept                       | Main Benefit                                | Main Limitation                             |
| --------------------------------------------------------------- | ------------ | --------------------------------- | ------------------------------------------- | ------------------------------------------- |
| [**Dependency Injection**](1-dependency-injection.md)           | –            | Pass dependencies as inputs       | Widely adopted, simple                      | Async leak, runtime configuration           |
| [**Dependency Interpretation**](2-dependency-interpretation.md) | `program` V1 | Abstract dependencies as data     | Testability, separation of what/how         | Monolithic instruction type                 |
| [**Free Monad**](3-free-monad.md)                               | `program` V2 | Separate instructions by domain   | Better organization, domain isolation       | All domains still coupled in `Program` type |
| [**Algebraic Effects**](4-algebraic-effects.md)                 | `program` V3 | Effect handlers with type safety  | Complete domain isolation, vertical slicing | No parallel execution, high boilerplate     |
| [**Tagless Final**](5-tagless-final.md)                         | `program` V4 | Async reader with instruction interface | Simplest, parallel execution, undo support | Requires discipline (no exhaustiveness) |

## Prerequisites

To get the most out of this introduction, you should be familiar with F# basics:

* Functional programming concepts: pure functions, side effects
* Types: discriminated unions, records
* Pattern matching
* Computation expressions—at least `async`

If not, you can consult my free e-book [F# Training](https://rdeneau.gitbook.io/fsharp-training/).

## How to Read This Introduction

Each page builds upon the previous one:

1. Start with **Dependency Injection** to understand the baseline approach and its limitations.
2. Progress through **Dependency Interpretation** to see how functional programming addresses these limitations.
3. Explore **Free Monad** to understand domain separation.
4. Review **Algebraic Effects** to understand the V3 attempt and its limitations.
5. Conclude with **Tagless Final** to see the current, simplest approach (V4) that powers the rest of this guide.

You can also jump directly to a specific approach if you're already familiar with the earlier patterns.

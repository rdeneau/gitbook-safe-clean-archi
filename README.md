---
icon: hand-wave
---

# Welcome

> _This book describes in details a particular way to write a full-stack application in the .NET ecosystem._

Developing a full-stack application is quite common. But you hit immediately a dilemma: the usual technologies for the front-end—Angular, React, Vue to name a few—and the back-end—ASP.NET mostly—don't fit together without frictions.

The main friction comes from the language difference: JavaScript/TypeScript versus C#. Even if they have a lot in common, switching from one to the other requires mental juggling, and you can't share files between the front and the back.

The attempts to develop the front in C# did not convince me—it don't feel natural for a front developer—putting apart Blazor as web assemblies are not considered in this discussion.

Here comes the [SAFE Stack](https://safe-stack.github.io/) and its variation the [SAFEr.Template](https://github.com/Dzoukr/SAFEr.Template) to the rescue to let you write your full-stack applications in F#. 💖

## F\#

The choice of F# as the common language between the front and back ends is a wise one. Its core, designed around principles derived from functional programming, ensures secure development. Its strong typing allows for easy [Domain Modeling](https://pragprog.com/titles/swdddf/domain-modeling-made-functional/) directly at the type level, whereas this is so difficult and verbose in C# and object-oriented programming. This is achieved without compromising the developer experience: its compact syntax and type inference make it easy to write code, with multiple possibilities for the code to read like natural language.

## Front development

_The power of the F# language makes the web development safer with ease and without compromise._

The front development with the [Feliz](https://fable-hub.github.io/Feliz/) DSL is very similar to writing HTML. Page design is not left behind, with libraries such as [Feliz.DaisyUI](https://dzoukr.github.io/Feliz.DaisyUI/#/) offering a better developer experience thanks to auto-completion, which is a welcome replacement for the commonly used “stringly typing”—even though IDEs and linters can fill this gap.

State management can be enhanced with [Elmish](https://elmish.github.io/elmish/), offering a better division of responsibilities thanks to the Model-View-Update (MVU) pattern. Elmish can operate at different levels, interconnected like Russian nesting dolls. But this leads to a heavy, omniscient root model. This inconvenience can be overcome thanks to [Feliz.UseElmish](https://fable-hub.github.io/Feliz/ecosystem/Hooks/Feliz.UseElmish), which allows the pages to be isolated from each other.

## Back development

Concerning the backend, you are on familiar ground with ASP.NET and its powerful hosting model. [Giraffe](https://giraffe.wiki) is then used to facilitate route configuration and interfacing with [Fable.Remoting](https://zaid-ajaj.github.io/Fable.Remoting/#/). The latter makes client-server communication so simple.

However, there are industry standards that are widely applied for ASP.NET development in C# but rarely used in F# despite their benefits. I am referring in particular to object-oriented designs such as dependency injection and SOLID principles, which can be used to strengthen the architecture. In combination with other concerns such as balancing high cohesion and low coupling, separation of concerns, it leads to the application of standardized architectures such as Clean Architecture and Vertical-Slice Architecture.

Taking all these considerations into account and assembling all the pieces of the puzzle into a coherent and fully documented whole is the goal of the ❝ _Safe Clean Architecture_ ❞ described in this book.

\---

1! seul anneau pour les controlés tous :&#x20;

Hence the need for a thorough documentation—consisting in this book—

and a small but well designed application showcasing, with a look and feel (+ multi-domain) that makes it not appearing as a toy example.


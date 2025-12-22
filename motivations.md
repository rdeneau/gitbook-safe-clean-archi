---
icon: compass
---

# Motivations

Developing a full-stack application is quite common. But you hit immediately a dilemma: the usual technologies for the front-end—Angular, React, Vue to name a few—and the back-end—ASP.NET mostly—don't fit together without frictions.

The main friction comes from the language difference: JavaScript/TypeScript versus C#. Even though they are cousins in the C-family, switching from one to the other requires mental juggling Similarly, communication between the client and the server requires non-trivial machinery and/or type conversions between DTOs. The attempts to develop the front in C# did not convince me—it don't feel natural for a front developer—putting apart Blazor as web assemblies are not considered in this discussion.

Imagine writing the front and back end in the same language, sharing types and even helpers, and client-server communication based on the simple definition of a contract in the form of a record type. All of this is possible with F# as the language and the [SAFE Stack](https://safe-stack.github.io/)—or its variation the [SAFEr.Template](https://github.com/Dzoukr/SAFEr.Template). 💖

## F\#

The choice of F# as the common language between the front and back ends is a wise one. Its core, designed around principles derived from functional programming, ensures secure development. Its strong typing allows for easy [Domain Modeling](https://pragprog.com/titles/swdddf/domain-modeling-made-functional/) directly at the type level, whereas this is so difficult and verbose in C# and object-oriented programming.

This is achieved without compromising the developer experience: its terse syntax and the type inference make it easy to write code, with multiple possibilities for the code to read like natural language. 🏆

## Front development

> _The power of the F# language makes the web development safer, with ease, and with barely no compromises._

The front development with the [Feliz](https://fable-hub.github.io/Feliz/) DSL is very similar to writing HTML. Page design is not left behind, with libraries such as [Feliz.DaisyUI](https://dzoukr.github.io/Feliz.DaisyUI/#/) offering a better developer experience thanks to auto-completion, which is a welcome replacement for the commonly used “stringly typing”—even though IDEs and linters can fill this gap.

State management can be enhanced with [Elmish](https://elmish.github.io/elmish/), offering a better division of responsibilities thanks to the Model-View-Update (MVU) pattern. Elmish can operate at different levels, interconnected like Russian nesting dolls. But this leads to a heavy, omniscient root model. This inconvenience can be overcome thanks to [Feliz.UseElmish](https://fable-hub.github.io/Feliz/ecosystem/Hooks/Feliz.UseElmish), which allows the pages to be isolated from each other.

## Back development

Concerning the backend, you are on familiar ground with ASP.NET and its powerful hosting model. [Giraffe](https://giraffe.wiki) is then used to facilitate route configuration and interfacing with [Fable.Remoting](https://zaid-ajaj.github.io/Fable.Remoting/#/). The latter makes client-server communication so simple.

## Architecture

Architecture is somewhat neglected in guides on F#. I have seen mention of an architecture based on the composition of functions from different application layers. Although this approach is simple, elegant, and very functional in spirit, it has its limitations.

There are industry standards that are widely applied for ASP.NET development in C# but rarely used in F# despite their benefits. I am referring in particular to object-oriented designs such as dependency injection and SOLID principles, which can be used to strengthen the architecture. In combination with other concerns such as balancing high cohesion and low coupling, and the separation of concerns, it leads to the application of standardized architectures such as Clean Architecture and Vertical-Slice Architecture.

## Safe Clean Architecture

It can be beneficial to apply standards used in C# to F# projects to facilitate adoption and onboarding. Two reasons support this idea:

* Many F# developers I know also work with C# and are familiar with these standards.
* We are struggling to find people who can work with F#. Even though F# is much loved by the developers who use it, it is clear that its adoption is difficult due to a lack of support from Microsoft and because it requires a change in programming style.

This architecture stems from the idea of combining everything we have just mentioned:

* Use F# and the SAFE stack to write full-stack application.
* Apply standards at the architecture level.

Hence the name ❝ Safe Clean Architecture ❞. 👋

## Sweat spot

F#, with its multi-paradigm and pragmatic approach, is versatile and open. It is not as “pure” from a functional programming point of view as Haskell. It is not as much a subject of research as its big brother OCaml. Nevertheless, it allows you to design fairly advanced things.

As proof, Safe Clean Architecture offers the possibility to code **domain workflows** in a pure and functional way. The pattern is inspired by the algebraic effects offered by OCaml, for example (see [Effect handlers](https://ocaml.org/manual/5.4/effects.html). However, its implementation in F# is derived from free monads, extended thanks to the support of object-oriented programming elements.

## Working example

An application called **Shopfoo**<sup>(1)</sup> complements this book. It is small but carefully designed and developed so that nothing is overlooked and it can serve not only as an example to illustrate the book but also as a working basis for your applications, even if it is not “production-ready.”

It is a mini back office for managing a book sales website. The business covers several areas: Catalog, Sales, Purchases, Warehouse. The graphical interface honors the concept of _Task-Based UI_<sup>(2)</sup> and uses almost all of the DaisyUI components to look more professional than a toy example. Behind the scenes, the code is clean and consistent on both the front and back ends.

## Documentation

The final objective of this book relates to documentation. Each library has its own documentation, with varying levels of detail and different UIs. A wealth of literature covers the concepts and patterns.

This book not only offers a unique entry point to external resources selected for their quality or usefulness, but also aims at being comprehensive and self-sufficient.

## Conclusion

Taking all these considerations into account and assembling all the pieces of the puzzle into a coherent and fully documented whole is the goal of the ❝ _Safe Clean Architecture_ ❞ described in this book.

***

<sup>**(1)**</sup>**&#x20;Shopfoo links:**

* GitHub repository: [https://github.com/rdeneau/shopfoo/](https://github.com/rdeneau/shopfoo/)
* Playground: [https://shopfoo-ggdqerf6brb9gxcb.francecentral-01.azurewebsites.net/](https://shopfoo-ggdqerf6brb9gxcb.francecentral-01.azurewebsites.net/)

<sup>**(2)**</sup> [Task based UI](https://youtu.be/DjZepWrAKzM), a YouTube video by Derek Comartin.

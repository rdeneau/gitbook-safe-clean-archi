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

## Sweat spot

F#, with its multi-paradigm and pragmatic approach, is versatile and open. It is not as “pure” from a functional programming point of view as Haskell. It is not as much a subject of research as its big brother OCaml. Nevertheless, it allows you to design fairly advanced things.

As proof, Safe Clean Architecture offers the possibility to code domain workflows in a pure and functional way. The pattern is inspired by the algebraic effects offered by OCaml, for example (see [Effect handlers](https://ocaml.org/manual/5.4/effects.html). However, its implementation in F# is derived from free monads, extended thanks to the support of object-oriented programming elements.

## Working example

An application called **Shopfoo**<sup>(1)</sup> complements this book. It is small but carefully designed and developed so that nothing is overlooked and it can serve not only as an example to illustrate the book but also as a working basis for your applications, even if it is not “production-ready.”

It is a mini back office for managing a book sales website. The business covers several areas: Catalog, Sales, Purchases, Warehouse. The graphical interface honors the concept of _Task-Based UI_<sup>(2)</sup> and uses almost all of the DaisyUI components to look more professional than a toy example. Behind the scenes, the code is clean and consistent on both the front and back ends.

## Documentation

The final objective of this book relates to documentation. Each library has its own documentation, with varying levels of detail and different UIs. A wealth of literature covers the concepts and patterns.

This book not only offers a unique entry point to external resources selected for their quality or usefulness, but also aims at being comprehensive and self-sufficient.

## Conclusion

Taking all these considerations into account and assembling all the pieces of the puzzle into a coherent and fully documented whole is the goal of the ❝ _Safe Clean Architecture_ ❞ described in this book.

{% hint style="info" %}
Use the [status.md](status.md "mention") page to check the progress of the book and app.
{% endhint %}

***

<sup>**(1)**</sup>**&#x20;Shopfoo links:**

* GitHub repository: [https://github.com/rdeneau/shopfoo/](https://github.com/rdeneau/shopfoo/)
* Playground: [https://shopfoo-ggdqerf6brb9gxcb.francecentral-01.azurewebsites.net/](https://shopfoo-ggdqerf6brb9gxcb.francecentral-01.azurewebsites.net/)

<sup>**(2)**</sup> [Task based UI](https://youtu.be/DjZepWrAKzM), a YouTube video by Derek Comartin.


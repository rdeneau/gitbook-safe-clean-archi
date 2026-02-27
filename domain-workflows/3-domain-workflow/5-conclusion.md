---
icon: clipboard-list
---

# Conclusion

We have just seen how _Safe Clean Architecture_ works with regard to features—in the sense of use cases, illustrated by practical examples taken from the demo application, _Shopfoo_.

* **Program:** The V4 `program`, based on the Tagless Final pattern, reduces the infrastructure to a single function type (`'ins -> Async<'ret>`) and a computation expression. It supports parallel execution via the applicative `let! ... and! ...` syntax and integrates undo/saga capabilities without modifying the core program type.
* **Instructions:** Each domain defines a single instruction interface inheriting `IProgramInstructions`. Adding a new instruction is a one-line interface member plus a one-line helper function—a dramatic reduction from V3's 5-step recipe.
* **Workflows:** Each workflow has its own file, making the file tree a map of supported features. Writing a workflow involves composing instructions using the `program` CE. Validation, error lifting, and parallel execution patterns are built into the CE's `Bind` overloads and applicative support.
* **Saga / Undo:** The saga pattern enables automatic rollback of completed commands when a workflow fails, with three strategies—`Reversible`, `Compensatable`, and `NotUndoable`. Workflow cancellation is supported as a first-class concept, distinct from failure. All of this operates in-process without a message bus.
* **Data:** The _Data_ layer represents the application-driven exterior—the right side of the hexagon in _Ports and Adapters Architecture_ terminology. This layer abstracts access to databases and external APIs, each isolated in their own module or folder.
* **API:** The `Api` class wires instructions to data-layer functions with undo strategies, then runs workflows through the saga runner. It is internal, abstracted by the API contract, and registered via dependency injection.

Since workflows are defined before the _Data_ layer in the F# compilation order, the only way they can interact with it is through instructions. This makes workflows **pure**—effects are encoded in the type system and revealed only at the moment of execution—and independent of other layers, complying with _Clean Architecture_. This functional approach offers a different type of abstraction from the interfaces used in the object-oriented approach.

This architecture makes extensive use of F# capabilities: declaration order, computation expressions, interface-based polymorphism, and a combination of FP and OOP paradigms that few languages allow on the .NET platform.

{% hint style="info" %}
The migration from the V3 `program` to V4 was done without touching any workflow. This illustrates a key benefit of computation expressions: workflows are written against the CE syntax (`let!`, `return`…), not against the underlying implementation. When the `program` CE's internals change—here from V3 to V4—the call sites remain untouched as long as the CE contract is preserved. In other words, the CE acts as an abstraction barrier, decoupling _what_ a workflow expresses from _how_ it is executed.
{% endhint %}

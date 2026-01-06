---
icon: clipboard-list
---

# Conclusion

We have just seen how _Safe Clean Architecture_ works with regard to features—in the sense of use cases, illustrated by practical examples taken from the demo application, _Shopfoo_.

* **Projects:** Features are grouped by project that does not reference each other to ensure their isolation. Depending on the complexity of the features per project, you can choose to implement them in the form of domain workflows based on the latest evolution of the program called "Effectful."
* **Workflows:** Each workflow has its own file so that it appears in the tree structure. Setting up a workflow mainly involves writing its program, based on the computation expression, the associated helpers, and a set of instructions specific to the project. To define each instruction, simply follow the five-step recipe, from the aliases of commands and queries to the helpers used to call the instructions in the programs.
* **Data:** The _Data_ layer represents the application-driven exterior—the right side of the hexagon in _Ports and Adapters Architecture_ terminology. This layer abstracts access to databases and external APIs, each isolated from the others in their own module or folder, and that can be organized using the _Entities / Mappers / Client / Pipeline_ segmentation.
* **API:** The last layer—in the order of definition in the F# project—is the one that defines the project's entry points. The `Api` class is perfectly encapsulated, as it is internal and abstracted by the API contract, and can be used by dependency injection.

Last detail: Since workflows are defined before the _Data_ layer, the only way they can interact with it is through instructions. This makes workflows pure—effects are encoded n the type system and revealed only at the moment of interpretation—and independent of other layers, thus complying with _Clean Architecture_. This functional approach offers a different type of abstraction from the interfaces used in the object-oriented approach.

This architecture makes extensive use of the F# capabilities, both those specific to it—such as declaration order and computation expressions—and those derived from programming paradigms, FP and OOP, in a combination that few languages allow, only F# on the .NET platform.

Finally, this architecture remains flexible: you choose which features to enhance the architecture for.

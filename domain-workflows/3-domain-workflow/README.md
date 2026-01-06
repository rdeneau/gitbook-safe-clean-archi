---
icon: rectangle-history
---

# Domain Project

The [Shopfoo.Product](https://shopfoo.product) project implements domain workflows using the [effectful-program.md](../effectful-program.md "mention").

Here is a simplified view from the solution explorer:

```txt
📂 src/
├──📂 Core/
│  ├──🗃️ Shopfoo.Common
│  ├──🗃️ Shopfoo.Domain.Types
│  └──🗃️ Shopfoo.Effects
├──📂 Feat/
│  ├──🗃️ Shopfoo.Home
│  └──🗃️ Shopfoo.Product 👈👈
│     ├──📂 Workflows/
│     │  ├──📄 Types.fs
│     │  ├──📄 Instructions.fs
│     │  ├──📄 AdjustStock.fs
│     │  ├──📄 MarkAsSoldOut.fs
│     │  └──📄 ...
│     ├──📂 Data/
│     └──📄 Api.fs
└──📂 UI/
   ├──🗃️ Shopfoo.Client
   ├──🗃️ Shopfoo.Server
   └──🗃️ Shopfoo.Shared
```

We will examine each part separately:

* [instructions.md](instructions.md "mention") describes how we can define the instructions called by domain workflows.
* [workflows.md](workflows.md "mention") presents how to write domain workflows by analyzing three typical use cases.
* [data.md](data.md "mention") indicates a way to organize the data access layer, whether it is to access a database or call an external API.
* [api.md](api.md "mention") describes how to define project entry points for higher layers, abstracting from lower layers—Workflows and Data.

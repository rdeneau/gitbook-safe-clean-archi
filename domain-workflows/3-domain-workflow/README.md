---
icon: rectangle-history
---

# Domain Project

The [Shopfoo.Product](https://shopfoo.product) project implements domain workflows using the [Broken link](/broken/pages/N2UTkhfeV9wjnLvndr4D "mention").

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

* [Broken link](/broken/pages/RGcqz7wRZ7LGyjP7YBbx "mention") describes how we can define the instructions called by domain workflows.
* [2-workflows.md](2-workflows.md "mention") presents how to write domain workflows by analyzing three typical use cases.
* [3-data.md](3-data.md "mention") indicates a way to organize the data access layer, whether it is to access a database or call an external API.
* [4-api.md](4-api.md "mention") describes how to define project entry points for higher layers, abstracting from lower layers—Workflows and Data.

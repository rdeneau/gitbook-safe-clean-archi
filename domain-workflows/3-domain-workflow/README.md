---
icon: rectangle-history
---

# Domain Project

The [Shopfoo.Product](https://github.com/rdeneau/shopfoo/tree/main/src/Shopfoo.Product) project implements domain workflows using the V4 [Program](../2-program/).

Here is a simplified view from the solution explorer:

```txt
📂 src/
├──📂 Core/
│  ├──🗃️ Shopfoo.Common
│  ├──🗃️ Shopfoo.Domain.Types
│  └──🗃️ Shopfoo.Program
├──📂 Feat/
│  ├──🗃️ Shopfoo.Home
│  └──🗃️ Shopfoo.Product 👈👈
│     ├──📂 Model/
│     │  ├──📄 Product.fs       👈 Validation logic
│     │  ├──📄 Prices.fs
│     │  └──📄 Stock.fs
│     ├──📂 Workflows/
│     │  ├──📄 Prelude.fs       👈 Instructions + helpers
│     │  ├──📄 AddProduct.fs
│     │  ├──📄 DetermineStock.fs
│     │  ├──📄 MarkAsSoldOut.fs
│     │  ├──📄 RemoveListPrice.fs
│     │  ├──📄 SavePrices.fs
│     │  ├──📄 SaveProduct.fs
│     │  └──📄 ...
│     ├──📂 Data/
│     ├──📄 Api.fs
│     ├──📄 DependencyInjection.fs
│     └──📄 Manifest.fs
└──📂 UI/
   ├──🗃️ Shopfoo.Client
   ├──🗃️ Shopfoo.Server
   └──🗃️ Shopfoo.Shared
```

We will examine each part separately:

* [Domain Model](0-domain-model.md) explains how the domain model is split between shared types (`Domain.Types`) and aggregates (`Model/`) following DDD principles.
* [Instructions](1-instructions.md) describes how the instruction interface is defined, along with the ergonomic helpers for composing programs.
* [Workflows](2-workflows.md) presents how to write domain workflows by analyzing typical use cases, including parallel execution and workflow cancellation with undo (saga pattern).
* [Data](3-data.md) indicates a way to organize the data access layer, whether it is to access a database or call an external API.
* [Api](4-api.md) describes how to define the project entry point, wire instructions to the data layer with undo strategies, and run workflows through the saga runner.

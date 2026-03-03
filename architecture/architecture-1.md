---
icon: diagram-successor
---

# Overview

Implementing use cases involves a significant number of layers, each described in the following pages. The path—the call chain—starts from the front-end, traverses every layer, and reaches external APIs or storage (databases, etc. — simplified as in-memory repositories in the _Shopfoo_ demo app).

This path differs slightly depending on whether the use case relies on a domain workflow. Let's illustrate this difference with two examples: the first example shows a Query without a workflow; the second illustrates a Command where a dedicated domain workflow is involved.

## Query example: Search Books

```mermaid
graph TD
    subgraph CLIENT ["UI/Client"]
        A["<em>@Pages/Product/Index/Page.fs</em><br/><code>ProductIndexView</code>:<br/><code>Msg.SearchBooks >> dispatch</code>"]
        -->B["<code>update</code> function:<br/><code>Cmd.searchBooks (env.FullContext.PrepareRequest { SearchTerm = searchTerm })</code>"]
        -->C["<code>Cmd</code> module:<br/><code>cmder.ofApiRequest { Call = fun api → api.Catalog.SearchBooks request }</code>"]
        -->D["<em>@Remoting.fs</em><br/><code>Cmder</code> type:<br/><code>Cmd.OfAsync.either</code>"]
    end

    subgraph SERVER ["UI/Server"]
        E["<em>@Remoting/Catalog/SearchBooksHandler.fs</em><br/><code>member this.Handle _ request user</code>"]
    end

    subgraph PRODUCT ["Feat/Product"]
        F["<em>@Api.fs</em> | <code>Api</code> class:<br/><code>openLibraryPipeline.SearchBooks(searchTerm)</code>"]
        -->G["<em>@Data/OpenLibrary.fs</em><br/><code>OpenLibraryPipeline</code> class:<br/><code>openLibraryClient.SearchBooksAsync(searchTerm)</code>"]
        -->H["<code>OpenLibraryClient</code><br/><code>GET openlibrary.org/search.json?q={term}&limit=10&language=eng</code>"]
    end

    subgraph EXT ["[External] Open Library API"]
        I["<code>search.json</code>"]
    end

    D --> E --> F
    H --> I
```

## Command example: Receive Supply

```mermaid
graph TD
    subgraph CLIENT ["UI/Client"]
        A["<em>@Pages/Product/Details/ReceiveSupply.fs</em><br/><code>ReceiveSupplyForm</code> component:<br/><code>Msg.ReceiveSupply Start >> dispatch</code>"]
        -->B["<code>update</code> function:<br/><code>Cmd.receiveSupply (fullContext.PrepareRequest input)</code>"]
        -->C["<code>Cmd</code> module:<br/><code>cmder.ofApiRequest { Call = fun api → api.Prices.ReceiveSupply request }</code>"]
        -->D["<em>@Remoting.fs</em><br/><code>Cmder</code> type:<br/><code>Cmd.OfAsync.either</code>"]
    end

    subgraph SERVER ["UI/Server"]
        E["<em>@Remoting/Prices/ReceiveSupplyHandler.fs</em><br/><code>member _.Handle _ input user</code>:<br/><code>api.Product.ReceiveSupply input</code>"]
    end

    subgraph PRODUCT ["Feat/Product"]
        F["<em>@Api.fs</em> | <code>Api</code> class:<br/><code>runWorkflow ReceiveSupplyWorkflow.Instance input</code>"]
        -->G["<code>workflowRunner.RunInSaga workflow arg</code>"]
        -->H["<em>@Workflows/ReceiveSupply.fs</em><br/><code>ReceiveSupplyWorkflow.Run input</code>:<br/><code>Program.addStockEvent stockEvent</code>"]
        -->I["<em>@Api.fs</em> | <code>prepareInstructions</code>:<br/><code>AddStockEvent ==> warehousePipeline.AddStockEvent</code>"]
        -->J["<em>@Data/Warehouse.fs</em><br/><code>WarehousePipeline</code> class:<br/><code>repository.AddStockEvent stockEvent</code>"]
        -->K["<code>StockEventRepository</code>:<br/><code>FakeRepository.Add stockEvent</code>"]
    end

    D --> E --> F
```

***

The following pages detail the architecture along two axes:

* [Solution Organisation](1-solution-orga.md) — physical and logical layout of the solution, project dependency graph, projects purpose, and the UI layer (Client, Remoting API, Security).
* [Principles](2-principles.md) — architecture principles (Modular Monolith, Clean / Hexagonal / Vertical Slice / Screaming Architecture) and design principles (Abstractions, DIP, Encapsulation, Dependency Injection).

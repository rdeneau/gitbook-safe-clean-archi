---
icon: webhook
---

# Api

The `Api.fs` file defines the domain project entry point and corresponds to the _Application_ layer in _Clean Architecture_ terminology. It contains three elements:

* The public interface `IProductApi`&#x20;
* The internal class `Api`
* The module `DependencyInjection`

## IProductApi contract

`IProductApi`  is a public interface defining the API contract of the domain project.

```fsharp
[<Interface>]
type IProductApi =
    abstract member GetProducts: (unit -> Async<Product list>)
    abstract member GetProduct: (SKU -> Async<Product option>)
    abstract member SaveProduct: (Product -> Async<Result<unit, Error>>)

    abstract member GetPrices: (SKU -> Async<Prices option>)
    abstract member SavePrices: (Prices -> Async<Result<unit, Error>>)
    abstract member MarkAsSoldOut: (SKU -> Async<Result<unit, Error>>)
    abstract member RemoveListPrice: (SKU -> Async<Result<unit, Error>>)

    abstract member AdjustStock: (Stock -> Async<Result<unit, Error>>)
    abstract member DetermineStock: (SKU -> Async<Result<Stock, Error>>)
    abstract member GetSales: (SKU -> Async<Sale list option>)
```

{% hint style="info" %}
## Note

The most attentive readers will have noticed that  members are defined using parentheses, which makes them properties that return a function rather than methods.

This reveals an implementation detail: we will define these members by partial application of functions. This remains a minor leak, acceptable in our case where we don't need to name the parameters and insofar as we use this interface from F# code—it would be more troublesome with C#.
{% endhint %}

## Api implementation

The `Api` class serves as the concrete domain project entry point, residing in&#x20;

```fsharp
type internal Api(interpreterFactory: IInterpreterFactory) =
    let interpret = interpreterFactory.Create(ProductDomain)
    // ...
```

This internal class is accessible to the `Shopfoo.Server` project via dependency injection in conjunction with the `IProductApi` interface, which defines the API contract.

The class depends on `IInterpreterFactory`, which creates the interpreter for the current domain: `ProductDomain`. The interpreter is named `interpret` so method calls read like English: `interpret.Command`, `interpret.Workflow`.

```fsharp
    // ...
    let runEffect (productEffect: IProductEffect<_>) =
        match productEffect.Instruction with
        | GetPrices query -> interpret.Query(query, Prices.Client.getPrices)
        | GetSales query -> interpret.Query(query, Sales.Client.getSales)
        | GetStockEvents query -> interpret.Query(query, Warehouse.Client.getStockEvents)
        | SavePrices command -> interpret.Command(command, Prices.Client.savePrices)
        | SaveProduct command -> interpret.Command(command, Catalog.Client.saveProduct)

    let interpretWorkflow (workflow: ProductWorkflow<'arg, 'ret>) args =
        interpret.Workflow runEffect workflow args
    // ...
```

The interpreter enables definition of two key functions:

* `runEffect`:
  * Pattern matches on the union type defining domain instructions
  * Interpreting each instruction involves calling the method corresponding to the instruction type and passing the underlying Data layer function
* `interpretWorkflow`: Though it appears to be a simple pass-through, it subtly serves to:
  * Enforce accepted workflow types: only those from the current _Product_ domain via the `ProductWorkflow` type
  * Name generic type parameters for convenience: `ProductWorkflow<'arg, 'ret>`
  * Apply the `runEffect` parameter to the `interpret.Workflow` method

```fsharp
    // ...
    interface IProductApi with
        member val GetProducts = Catalog.Client.getProducts // (1)
        member val GetProduct = Catalog.Client.getProduct // (1)
        member val SaveProduct = interpretWorkflow SaveProductWorkflow.Instance // (2)

        member val GetPrices = Prices.Client.getPrices // (1)
        member val SavePrices = interpretWorkflow SavePricesWorkflow.Instance // (2)
        member val MarkAsSoldOut = interpretWorkflow MarkAsSoldOutWorkflow.Instance // (2)
        member val RemoveListPrice = interpretWorkflow RemoveListPriceWorkflow.Instance // (2)

        member val AdjustStock = Warehouse.Client.adjustStock // (1)
        member val DetermineStock = interpretWorkflow DetermineStockWorkflow.Instance // (2)
        member val GetSales = Sales.Client.getSales // (1)
```

Finally, the class implements the API contract. Each endpoint is defined based on the underlying feature type:

1. Direct Data layer call
2. Workflow interpretation

## Dependency Injection

The last part of the file contains a module defining an extension method to configure dependency injection for these project:

```fsharp
module DependencyInjection =
    open Microsoft.Extensions.DependencyInjection

    type IServiceCollection with
        member services.AddProductApi() = // ↩
            services.AddSingleton<IProductApi, Api>()
```

In the case of our demo application _Shopfoo_, the method only performs a single type registration. This is already useful for encapsulation because it allows the `Api` class to be made internal. In a larger project, with a _Data_ layer that itself requires dependency injection—typically elements from the configuration—we would have more types to configure in this method.

---
icon: syringe
---

# Dependency Injection

Dependency Injection (DI) is the de facto state of the art in C# for managing dependencies. While it's an object-oriented pattern, understanding it provides essential context for the functional alternatives we'll explore later.

## Introduction

**Context:** In object-oriented programming, the building blocks are objects, more specifically **classes** in C#.

**Definition:** When a class collaborates with other classes, these classes are called **dependencies**.

**Problem:** When dependencies are static or when the class instantiates its own dependencies, it becomes a black box—the dependencies are not visible from the caller. This makes the class difficult to unit test.

**Solution:** The class takes its dependencies as inputs, making them explicit and substitutable.

## Types of Dependency Injection

There are three types of dependency injection, each differing in **location** (where dependencies are passed) and **scope** (where they can be used):

### Constructor Injection

Dependencies are defined as constructor parameters.

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly ILogger _logger;

    public OrderService(IOrderRepository repository, ILogger<OrderService> logger)
    {
        _repository = repository;
        _logger = logger;
    }
}
```

- ✅ **Benefit:** Dependencies can be used throughout the whole class
- ⚠️ **Limit:** Too many constructor parameters may indicate a class with too many responsibilities

### Method Injection

Dependencies are defined as parameters of public methods.

```csharp
public class OrderService
{
    public void ProcessOrder(Order order, IPaymentGateway paymentGateway)
    {
        // Use paymentGateway only in this method
    }
}
```

- ✅ **Benefit:** More explicit—we know exactly which dependencies each method needs.
- ⚠️ **Limit:** Boilerplate when repeating the same dependency across different methods.

### Property Injection

Dependencies are defined as mutable properties, usually with an initial value to avoid null references.

```csharp
public class OrderService
{
    public ILogger Logger { get; set; } = new NullLogger();
}
```

- ✅ **Benefit:** Models optional dependencies, initially handled with a [Null Object pattern](https://en.wikipedia.org/wiki/Null_object_pattern). The caller can activate features by passing real implementations.
- ⚠️ **Limit:** Mutability introduces potential issues.

## Dependency Injection Systems

Dependency injection requires an external system to construct the classes. There are two kinds of DI systems:

### Pure DI

Constructing dependencies manually in the application root, also known as [Pure DI](https://blog.ploeh.dk/2014/06/10/pure-di/) (formerly called "Poor man's DI").

```csharp
// Composition root
var logger = new ConsoleLogger();
var repository = new SqlOrderRepository(connectionString);
var service = new OrderService(repository, logger);
```

### DI Container

Using a container to manage dependency resolution and lifecycle, such as `Microsoft.Extensions.DependencyInjection.IServiceCollection`.

```csharp
services.AddSingleton<ILogger, ConsoleLogger>();
services.AddScoped<IOrderRepository, SqlOrderRepository>();
services.AddTransient<OrderService>();
```

## Inversion of Control (IoC)

DI containers are also called **IoC containers**. The **Inversion of Control** means inverting the flow of control in your application.

It's also known as the **Hollywood Principle**: "Don't call us, we'll call you."

## Service Locator Anti-Pattern

{% hint style="warning" %}
The **Service Locator** pattern—a central registry to obtain service instances—is considered an anti-pattern because it **hides a class's dependencies**.
{% endhint %}

**❌ Don't do this:**

```csharp
public class OrderService
{
    public void ProcessOrder(Order order)
    {
        var repository = ServiceLocator.Get<IOrderRepository>(); // Hidden dependency!
    }
}
```

**❌ Don't do this:**

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IServiceCollection services) // Hidden underlying dependency!
    {
        _repository = services.GetRequired<IOrderRepository>();
    }
}
```

**✅ Do this instead:**

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository) // Explicit dependency
    {
        _repository = repository;
    }
}
```

## Benefits of Dependency Injection

### Separation of Concerns

Clear separation between **constructing** dependencies and **using** them.

### Loose Coupling

When dependencies are abstracted behind interfaces, classes are loosely coupled to concrete implementations.

### Testability

Dependencies can be mocked in unit tests, enabling true isolation and fast, reliable tests.

### Dependency Lifecycle Management

DI containers provide a clean way to manage different lifecycles:

- **Singleton:** A single instance shared across the application (not the same as a static `MyClass.Instance`)
- **Scoped:** One instance per request/scope (e.g., Entity Framework's `DbContext` per HTTP request in ASP.NET)
- **Transient:** A new instance every time it's requested

## Limitations of Dependency Injection

Despite its benefits, DI has several limitations:

### Indirection

Like any abstraction, DI creates indirection between the caller and the actual implementation.

### Runtime Configuration

Real dependencies are only known at **runtime**. The compiler cannot detect dependency misconfiguration, leading to potential runtime errors.

### Testability Challenges

While DI improves testability in general, it can be difficult to test the composition root itself. Can you get an instance of any constructed object for testing?

### Async Leak

When any dependency is asynchronous, the entire call chain must become asynchronous too. This "async leak" propagates throughout your codebase.

## Builders and Factories

An intermediate approach to dependency management:

- Inject a [Builder](https://refactoring.guru/design-patterns/builder) or an [Abstract Factory](https://refactoring.guru/design-patterns/abstract-factory) instead of the dependency itself.
- The class controls **what** dependencies to build and **when** to build them.

```csharp
public class OrderService
{
    private readonly IRepositoryFactory _factory;

    public OrderService(IRepositoryFactory factory)
    {
        _factory = factory;
    }

    public void ProcessOrder(Order order)
    {
        // Control when to create the dependency
        var repository = _factory.CreateRepository();
        // ...
    }
}
```

This gives more control over dependency instantiation while maintaining testability.

## Next Steps

Now that we understand dependency injection and its limitations, let's explore how functional programming addresses these challenges with **Dependency Interpretation**.

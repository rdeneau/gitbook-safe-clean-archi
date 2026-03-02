# Principles

## Architecture Principles

The following principles are either embedded in the code design, or checked using architecture tests.

### Modular Monolith

A modular monolith structures the application into independent modules with well-defined boundaries, split based on logical boundaries. Modules are loosely coupled and communicate through a public API.

The application exposes modules located in the `src/Feat/` folder.

{% hint style="success" %}
**Architecture rule:** Domain project should not reference other domain projects.
{% endhint %}

### Clean Architecture

The architecture maps to Clean Architecture layers:

| Layer              | Path                                              |
| ------------------ | ------------------------------------------------- |
| **Presentation**   | `src/UI/Server/`                                  |
| **Application**    | `src/Feat/Xxx/Workflows/`                         |
| **Domain**         | `src/Core/Domain.Types/`and `src/Feat/Xxx/Model/` |
| **Infrastructure** | `src/Feat/Xxx/Data/`                              |

### Hexagonal Architecture

The Clean Architecture is de facto compatible with the Hexagonal Architecture: the hexagon surrounds the Application and Domain layers. It distinguishes dependencies that _drive_ the hexagon (left side) from those _driven by_ it (right side).

**Left side:** The `UI/Server` project drives domains through their `I{Domain}Api` (ports) and adapts them to the Remoting API. Tests can exercise the `I{Domain}Api`, mocking its dependencies.

**Right side:** The Data/Infrastructure layer, with two levels of "ports and adapters":

1. Application Workflows define their right ports as **Instructions**. The Workflow Runner acts as the adapter, driving the Data Pipelines.
2. Data Pipelines can expose their dependencies as interfaces (`IXxxApi`), implemented by concrete Clients, following the dependency inversion principle.

### Vertical Slice Architecture

Instead of organizing code by technical layers, Vertical Slice Architecture organizes it by business features. The domain projects in `src/Feat/` are self-contained, including almost all layers: Application, Domain, Infrastructure — `Workflows/` and `Data/` folders in the code.

### Screaming Architecture

The system communicates its purpose through its structure:

- **Domain projects** contain a `Workflows/` folder where each workflow (use case) is in a dedicated file.
- **Remoting API** folders contain a handler per file, exposing the capabilities consumed by the Client.

{% hint style="success" %}
**Architecture rule:** Workflows should be in their dedicated file, named without the `Workflow` suffix.
{% endhint %}

{% hint style="success" %}
**Architecture rule:** Remoting API request handlers should be sealed and in their dedicated file.
{% endhint %}

## Design Principles

The following design principles support the architecture principles at a lower level. Their main purpose is to increase modularity by reducing coupling.

### Abstractions

Abstraction hides implementation complexity (the "how") behind a simplified, essential interface (the "what"). In OOP, an interface is the most common form; in FP, it's a function type.

**Benefits:** Decoupling, Encapsulation, Stability, Testability, Transitivity cut (dependency firewall).

**Warnings:** Beware of leaky abstractions or abstractions at the wrong level. A bad abstraction costs more than no abstraction at all. More types involved means more indirections and potentially harder navigation.

### Dependency Inversion (DIP)

1. High-level modules should not import from low-level modules. Both should depend on abstractions.
2. Abstractions should not depend on details. Details should depend on abstractions.

{% hint style="success" %}
**Architecture rule:** Domain types should not depend on domain projects.
{% endhint %}

{% hint style="success" %}
**Architecture rule:** Workflows should not depend on Data types. (Enforced by F# compilation order: `Workflows/` is declared before `Data/` in the `.fsproj`.)
{% endhint %}

{% hint style="success" %}
**Architecture rule:** Domain projects should not reference the `UI/Server` project.
{% endhint %}

The abstraction between Workflows and Data are the **program instructions** — see [Domain workflows](../domain-workflows/1-introduction/README.md).

### Encapsulation

Limits direct access to internal state and behaviour. Achieved mainly via `private` (inside projects) and `internal` (between projects) keywords.

**Encapsulation in the domain projects:**

- `UI/Server` should access only the `I{Domain}Api` and the `DependencyInjection` helpers.
- Test projects can access internal members using `InternalsVisibleTo` entries in `.fsproj` files.

{% hint style="success" %}
**Architecture rule:** Domain workflows should be `internal`.
{% endhint %}

{% hint style="success" %}
**Architecture rules for Data components:**

- Clients: `internal`
- Client Settings: public (needed for DI)
- Entities (DTOs): public (to avoid serialization issues)
- Mappers: `internal`
- Pipelines: `internal`
- Data Entities should not be used outside of their respective namespace.
{% endhint %}

### Dependency Injection

DI achieves the Inversion of Control ("Don't call us, we call you!"). Dependencies appear in the type definition:

- **C# way:** Constructor parameters — e.g. `UI/Server/Remoting/{Page}/{Request}Handler` depends on `FeatApi`.
- **F# way:** Function parameters — e.g. `Feat/{Domain}/Data/{Api}/{Api}Pipeline` depends on `{Api}Client(s)`.
- **Program:** Abstracted as instructions in the program-based domain workflows.

#### DI Container

The DI container handles object instantiation, life cycle (transient, scoped, singleton), and dependency graphs. Each layer is responsible for configuring the DI of its types, exposing `IServiceCollection` extension methods. The top-level method chains the lower-layer registrations.

## Architecture Tests

The architecture rules listed above are enforced by tests located in `tests/Shopfoo.Feat.Tests/ArchitectureTests.fs` and `tests/Shopfoo.Server.Tests/ArchitectureTests.fs`:

```text
Feat
└── FeatArchitectureTests
    ├── Domain types should not depend on feat projects
    ├── Feat data clients should be internal
    ├── Feat data DTOs should be public to prevent serialization issues
    ├── Feat data mappers should be internal
    ├── Feat data pipelines should be internal
    ├── Feat project should not reference other feat projects
    ├── Feat project should not reference the Server project
    ├── Workflow class name should end with Workflow
    ├── Workflows should be in their dedicated file, named without the Workflow suffix
    ├── Workflows should be sealed and internal classes
    ├── Workflows should not depend on data DTOs
    └── Workflows should not depend on Data types

UI
└── ServerArchitectureTests
    ├── Remoting API request handlers should be sealed and in their dedicated file
    ├── Server project should not access data DTOs, public just to prevent serialization issues
    ├── Server project should not access Feat data layer
    └── Server project should not access Feat internal elements using InternalsVisibleTo
```

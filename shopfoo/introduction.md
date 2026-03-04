---
icon: play
---

# Introduction

_❝ Shopfoo ❞_ is a mini application that accompanies this book. It is live and available following this [link](https://shopfoo-ggdqerf6brb9gxcb.francecentral-01.azurewebsites.net/) so you can play around with it and understand its use cases. Its source code is available on [GitHub](https://github.com/rdeneau/shopfoo) to provide a concrete and complete codebase, created with as much care as if it were a real application.

{% hint style="info" %}
The name **Shopfoo** is a reference to the [chop suey](https://en.wikipedia.org/wiki/Chop_suey) dish and the [song](https://en.wikipedia.org/wiki/Chop_Suey!) by System of a Down 🤘.
{% endhint %}

The only pieces missing to be production-ready are those that would prevent me from staying on a free plan on Azure, namely the database and an observability stack (logs, metrics, distributed traces).

## Application Overview

Shopfoo is an **admin console** (a.k.a. back-office) for a retail store selling two types of items: **books** and **other products**.

### Catalog & Data Sources

- **Books** are fetched from the [OpenLibrary API](https://openlibrary.org/developers/api).
- **Other products** come from the [FakeStore API](https://fakestoreapi.com).

Each item has pricing information: a **retail price** and an optional **list price** (e.g. the manufacturer's recommended retail price).

### Cache & Seeding

The application relies on an in-memory cache. On startup, a seeding phase populates the cache with around fifteen books. Products are then added and updated progressively as the user interacts with the application.

### Event Simulation

Shopfoo lets users simulate events that would happen automatically in a real e-commerce platform connected to:

- a **front-end storefront** (sales),
- a **warehouse** handling stock and order fulfilment,
- a **supplier system** for restocking.

From any product page, users can record:

- **Sales** — simulating purchases made on the storefront,
- **Stock arrivals** — simulating deliveries from a supplier,
- **Stock adjustments** — simulating inventory corrections after a warehouse count.

### Extranet Features

Beyond product management, Shopfoo also demonstrates classic back-office features:

- **User authentication** — login/logout flow,
- **User rights management** — role-based access control,
- **Language switching** — internationalisation (i18n),
- **Theme switching** — visual theme customisation.

## Technical stack

The solution is based on the [SAFEr.Template](https://github.com/Dzoukr/SAFEr.Template). It's written in F# on both Client and Server sides:

- Client:
  - [Fable 4](https://fable.io) F#-to-JavaScript transpiler
  - SPA: [React 19](https://react.dev) under the hood
  - HTML DSL: [Feliz 2.9](https://fable-hub.github.io/Feliz/2.9.0)
  - ELM architecture: [Elmish](https://elmish.github.io/elmish/)
    - MVU loop per page using `React.useElmish` from [Feliz.UseElmish](https://fable-hub.github.io/Feliz/ecosystem/Hooks/Feliz.UseElmish)
    - `FullContext` object stored in the root view and shared to page views
  - Design system: [Feliz.DaisyUI](https://dzoukr.github.io/Feliz.DaisyUI/#/) built on [DaisyUI](https://daisyui.com) and [tailwindcss](https://tailwindcss.com)
  - Build: [Vite.js](https://vite.dev) (instead of webpack)
  - Routing: navigation between pages using [Feliz.Router](https://fable-hub.github.io/Feliz/ecosystem/Components/Feliz.Router)
- Server:
  - [ASP.NET Core](https://www.asp.net/core/overview/aspnet-vnext)
  - [Giraffe](https://giraffe.wiki) as a functional overlay
- Client-Server:
  - [Fable.Remoting](https://zaid-ajaj.github.io/Fable.Remoting/#/) supporting the "Remoting API", with endpoints grouped between `Home` and `Product`
  - Shared `ApiError` type, hiding the `Error` domain type
  - Custom helpers for the calls to the Remoting API:
    - Types: `ApiResult<'a> = Result<'a, ApiError>`, `ApiCall<'a> = Start | Done of ApiResult<'a>`
    - Objects: `fullContext.PrepareRequest(...) : Cmder` → `cmder.ofApiRequest(ApiRequestArgs) : Cmd<Msg>`, abstracting the Elmish `Cmd.OfAsync.either`
  - Translations:
    - Grouped by pages, loaded on demand and cached on the Client side
    - Friendly and strongly-typed syntax for the views: e.g. `translations.Home.Theme.Garden`, `translations.Product.Discount discount.Value`

## What's next

The following pages of this chapter walk through how the application works and describe its different pages with their features.

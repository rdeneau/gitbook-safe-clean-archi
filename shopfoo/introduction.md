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

It demonstrates several back-office features: user authentication, role-based access control, internationalisation, theme switching, product catalogue management, and event simulation.

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

The following pages walk through how the application works and describe its features:

- [General Features](general.md) — login, default page, About and Admin pages
- [Navigation & UI](navigation.md) — breadcrumb, menu, language and theme selectors
- [Products](products.md) — catalog, data sources, cache, filtering and search
- [Product Management](management.md) — task-based UI, editing, pricing, stock and sales

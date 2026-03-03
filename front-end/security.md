---
icon: shield-user
---

# Security 🚧

{% hint style="warning" %}
🚧 This page is under construction.
{% endhint %}

## User rights management

Shopfoo manages user rights through a claims-based model defined in `Domain.Types/Security.fs`.

### Domain model

A `User` is either `Anonymous` or `LoggedIn` with a name and a set of `Claims`. Claims map a `Feat` (feature area) to an `Access` level:

```fsharp
type Access = View | Edit

type Feat = About | Admin | Catalog | Sales | Warehouse

type Claims = Map<Feat, Access>

type User =
    | Anonymous
    | LoggedIn of userName: string * claims: Claims
```

The `User` type exposes helpers to check access:

```fsharp
member user.CanAccess feat = ...
member user.AccessTo feat = ...  // Returns Access option

// Active patterns for pattern matching
let (|UserCanAccess|_|) feat (user: User) = ...
let (|UserCanNotAccess|_|) feat (user: User) = ...
```

### Authentication flow

The Extranet hosting page passes its initial state in the `__INIT_STATE__` JavaScript global variable, containing information about the connected user — both as a plain JS object (`AuthorizedUser`) and encoded in a JWT (`AuthToken`).

The `AuthToken` is passed with every Remoting API request, verified server-side, and decoded to the `User`.

### Server-side authorization

API handlers inherit from `SecureRequestHandler`, which receives the decoded `User` after token verification. The `authorizeHandler` function checks the token and required claims before delegating to the handler:

```fsharp
let authorizeHandler (claims: Claims) (handler: SecureRequestHandler<...>) request =
    async {
        match checkToken claims request.Token with
        | Error authError -> return Error(ServerError.AuthError authError)
        | Ok authorizedUser -> return! handler.Handle request.Lang request.Body authorizedUser
    }
```

### Client-side access control

On the client side, the `AppView` component checks user access before rendering pages. Unauthorized access redirects to a "Not Found" page:

```fsharp
// View.fs — page routing with access check
let pageToDisplayInline, featAccessToCheck =
    match model.Page, fullContext.User with
    | Page.ProductIndex _, User.LoggedIn _ -> model.Page, Some Feat.Catalog
    | Page.Admin, User.LoggedIn _ -> model.Page, Some Feat.Admin
    | Page.ProductIndex _, User.Anonymous -> Page.Login, None
    | ...

React.useEffect (fun () ->
    match featAccessToCheck with
    | Some feat when not (fullContext.User.CanAccess feat) ->
        Router.navigatePage (Page.CurrentNotFound())
    | _ -> ()
)
```

Individual components also use `CanAccess` to conditionally render features:

```fsharp
// Product/Details/Page.fs
let hasActions =
    match fullContext.User, sku.Type with
    | (UserCanAccess Feat.Sales | UserCanAccess Feat.Warehouse), (SKUType.FSID _ | SKUType.ISBN _) -> true
    | _ -> false
```

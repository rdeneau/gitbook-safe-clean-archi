---
icon: sidebar
---

# Navigation & UI

The **navigation bar** sits at the top of every page and is split into two areas:

* **Left** — an interactive breadcrumb trail showing the current location in the application hierarchy.
* **Right** — dropdown menus (user, language, theme) followed by navigation links (Admin, About).

## Breadcrumb

A **breadcrumb trail** is displayed at the top of each page to show the user's current location within the application hierarchy.

<table><thead><tr><th width="183.4000244140625">Url</th><th width="202.79998779296875">Logged in</th><th>Breadcrumb</th></tr></thead><tbody><tr><td>Any</td><td>No</td><td>Shopfoo > Login</td></tr><tr><td>/about</td><td>Any</td><td>Shopfoo > About</td></tr><tr><td>/admin</td><td>Yes, as Administrator</td><td>Shopfoo > Admin</td></tr><tr><td>/</td><td>Yes</td><td>Shopfoo > Products</td></tr><tr><td>/products</td><td>Yes</td><td>Shopfoo > Products</td></tr><tr><td>/bazaar</td><td>Yes</td><td>Shopfoo > Products > Bazaar</td></tr><tr><td>/bazaar/{SKU}</td><td>Yes</td><td>Shopfoo > Products > Bazaar > {SKU}</td></tr><tr><td>/books</td><td>Yes</td><td>Shopfoo > Products > Books</td></tr><tr><td>/books/{SKU}</td><td>Yes</td><td>Shopfoo > Products > Books > {SKU}</td></tr></tbody></table>

Each breadcrumb segment can act as a **navigation link** to move back up the hierarchy — except when the current URL is already the target, in which case the segment is rendered as plain text (no self-link):

* **Shopfoo** links to `/` (root), unless the current page is already `/`.
* **Products** links to `/products`, unless the current page is already `/products`.

{% hint style="info" %}
See the [Front-end > Navigation](../front-end/navigation.md) chapter for the client-side routing and breadcrumb implementation.
{% endhint %}

## User menu

The **user menu** displays the name of the currently logged-in persona. It provides a **logout** action to return to the login page.

![User menu](../.gitbook/assets/user-menu.png)

## Language menu

The **language menu** lets users switch the application's display language at runtime.

<details open>

<summary><span data-gb-custom-inline data-tag="emoji" data-code="1f5a5">🖥️</span> <strong>Demo</strong></summary>

![Lang menu](../.gitbook/assets/lang-menu.gif)

</details>

The Latin option is intentionally unimplemented. It demonstrates the error case where translations fail to load — a scenario that can occur in production when using an external translation API.

<details open>

<summary><span data-gb-custom-inline data-tag="emoji" data-code="1f5a5">🖥️</span> <strong>Demo</strong></summary>

![Latin](../.gitbook/assets/latin.gif)

</details>

{% hint style="info" %}
Translations are loaded on demand and cached on the client side. See the [Translations](../front-end/translations.md) chapter for the i18n architecture and strongly-typed translation helpers.
{% endhint %}

## Theme menu

The **theme menu** lets users choose a visual theme for the application. Eight themes are available — a curated selection of dark and light themes from [DaisyUI](https://daisyui.com) — applied dynamically without a page reload:

<details open>

<summary><span data-gb-custom-inline data-tag="emoji" data-code="1f5a5">🖥️</span> <strong>Demo</strong></summary>

![Theme menu](../.gitbook/assets/theme-menu.gif)

</details>

## Navigation links

The right end of the navigation bar contains two direct links:

* **Admin** — visible and accessible only to users whose claims include Admin access.
* **About** — always visible, accessible to everyone including anonymous users.

{% hint style="info" %}
The [General Features](general.md#demo) page includes a demo showing the Admin and About pages in context.
{% endhint %}

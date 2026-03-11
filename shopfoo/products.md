# Products

This page describes how Shopfoo fetches, caches, and displays its product catalog, and how users can search and filter products.

{% hint style="info" %}
All display state — active filters, search term, sort column and direction — is reflected in the URL. Any combination can be bookmarked or shared as a direct link.
{% endhint %}

## Provider choice

Shopfoo sells two types of products, each sourced from a different external API:

| Provider                                                  | Type         | Categories                           |
| --------------------------------------------------------- | ------------ | ------------------------------------ |
| [FakeStore API](https://fakestoreapi.com)                 | **🏪 Bazaar** | 👗 Clothing, 🔌 Electronics, 💍 Jewelry |
| [OpenLibrary API](https://openlibrary.org/developers/api) | **📘 Books**  | 📚 Books                              |

## Cache & Seeding

The application relies on an **in-memory cache** — there is no persistent database. On startup, a **seeding phase** populates the cache with around fifteen books. Products are then added and updated **progressively** as the user interacts with the application: browsing a product for the first time fetches it from the external API and stores it in the cache.

The provider is determined by the product type and is transparent to the user.

## Table display

Products are listed in a **sortable table**. The table header is **sticky**: it remains visible at the top of the page while scrolling down through a long list.

Each row shows the key product attributes:

| Column      | 🏪 Bazaar | 📘 Books | Notes                            |
| ----------- | :-------: | :------: | -------------------------------- |
| #           |    ✅     |    ✅    | Row number                       |
| SKU         |    ✅     |    ✅    |                                  |
| Category    |    ✅     |    —     |                                  |
| Name        |    ✅     |    ✅    | Books: title + optional subtitle |
| Authors     |    —      |    ✅    |                                  |
| Tags        |    —      |    ✅    |                                  |
| Description |    ✅     |    ✅    |                                  |

### Bazaar

![Bazaar](./img/bazaar.png)

### Books

![Books](./img/books.png)

### Truncation

Long text cells (**Name** and **Description**) are truncated with an ellipsis and capped at 2 lines by default. On mouse hover, the row expands to reveal up to 3 lines. For books specifically, the **Name** column also changes layout on hover: the two-line `Title ↵ Subtitle` view collapses into a single `Title: Subtitle` line.

Example:

```txt
┌────────┬──────────────────────────────────┬───────────────────────────────────────┐
│ State  │ Name                             │ Description                           │
├────────┼──────────────────────────────────┼───────────────────────────────────────┤
│ Normal │ Clean Code                       │ Even bad code can function. But if    │
│        │ A Handbook of Agile Software…    │ code isn't clean, it can bring a…     │
├────────┼──────────────────────────────────┼───────────────────────────────────────┤
│ Hover  │ Clean Code: A Handbook of Agile  │ Even bad code can function. But if    │
│        │ Software Craftsmanship           │ code isn't clean, it can bring a      │
│        │                                  │ development organization to its…      │
└────────┴──────────────────────────────────┴───────────────────────────────────────┘
```

### Sorting

All columns except **Description** are sortable. Clicking a column header cycles through ascending and descending order. An icon in the header indicates the current sort state:

| Icon | Color | Meaning                        |
| ---- | ----- | ------------------------------ |
| ⏶⏷   | Grey  | Sortable, not currently sorted |
| ⏶    | Green | Sorted ascending               |
| ⏷    | Green | Sorted descending              |

![Sorted by SKU in ascendent order](./img/sort-by-sku-asc.png)

## Filter & search

The product page has its own **toolbar** above the table, organized in three parts:

**1. Product type switcher** — selects between 🏪 Bazaar and 📘 Books. The total number of products of the selected type is displayed next to the label, independently of any active filters.

**2. Attribute filters** — faceted filters that vary by product type:

| Type      | Filters                                                              |
| --------- | -------------------------------------------------------------------- |
| 🏪 Bazaar | One filter with 3 positions: 👗 Clothing, 🔌 Electronics, 💍 Jewelry |
| 📘 Books  | Two dropdown filters: Authors and Tags                               |

Each filter option shows the number of matching products.

When a filter is active, the matching values in the corresponding table column are highlighted — governed by the same **Highlight matches** toggle as the text search.

**3. Text search** — a free-text input that searches across all table columns (except the row number `#`). Only rows containing the search term are displayed. Two options complement the search:

- **Case-sensitive** — toggles case sensitivity (disabled by default).
- **Highlight matches** — found occurrences are highlighted in the results (enabled by default).

**Demo:**

![Filters](./img/filters.gif)

## OpenLibrary search

When browsing 📘 **Books** with an active text search term, a 🔍 **search button** appears in the toolbar. Clicking it triggers a query to the OpenLibrary API using the current search term, fetching up to **30 results**. Those results are then re-filtered locally by the same text search to produce the final list, which is merged directly into the table alongside the books already in the cache. Books loaded this way are identifiable by the ✨ emoji prefixed to their title.

![OpenLibrary search](./img/openlib-search.gif)

From there, a selected book can be permanently added to the in-memory cache — this is covered in detail in the [Product Management](management.md) page.

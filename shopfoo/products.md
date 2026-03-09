# Products

This page describes how Shopfoo fetches, caches, and displays its product catalogue, and how users can search and filter products.

## Cache & Seeding

The application relies on an **in-memory cache** — there is no persistent database. On startup, a **seeding phase** populates the cache with around fifteen books. Products are then added and updated **progressively** as the user interacts with the application: browsing a product for the first time fetches it from the external API and stores it in the cache.

## Provider choice

Shopfoo sells two types of products, each sourced from a different external API:

| Type | Source |
|------|--------|
| **Books** | [OpenLibrary API](https://openlibrary.org/developers/api) |
| **Other products** | [FakeStore API](https://fakestoreapi.com) |

The provider is determined by the product type and is transparent to the user.

## Table display

Products are listed in a **sortable table**. Each row shows the key product attributes. Clicking a column header sorts the list by that column.

**Columns common to all products:** name, retail price, list price (MSRP), stock quantity.

**Book-specific columns:** subtitle, author(s), publication year, ISBN.

📸 _Screenshot: product table — books_

📸 _Screenshot: product table — other products_

## Book specifics

Books have dedicated fields not present on other products:

- **Subtitle**
- **Author(s)**
- **Publication year**
- **ISBN**

These fields are displayed in the table and on the book detail page.

## Pricing

Each product has two price fields:

- **Retail price** — the price charged to the customer.
- **List price** (MSRP) — the manufacturer's or publisher's recommended retail price. Optional.

## Filter & search

### Inline pre-filtering

The table provides an **inline filter** row that lets users type directly into each column to narrow down the displayed results in real time.

📸 _Screenshot: product table with active inline filter_

### OpenLibrary search fallback

When the inline pre-filter is applied to the **books** list and returns **no results** from the cache, Shopfoo automatically queries the **OpenLibrary API** to search for matching books. Any books found are added to the cache and displayed in the table, allowing users to discover and load new books on demand.

📸 _Screenshot: OpenLibrary search results loaded into the table_

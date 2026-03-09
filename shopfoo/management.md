# Product Management

This page describes how products are managed in Shopfoo: editing product information, managing pricing, and simulating the business events that affect stock and sales.

## Business areas involved

Product management in Shopfoo spans four business areas:

| Area | Responsibility |
|------|---------------|
| **Catalog** | Product information and pricing |
| **Sales** | Customer orders and revenue |
| **Purchases** | Supplier orders and restocking |
| **Warehouse** | Stock levels and inventory corrections |

## Task-Based UI approach

Rather than a single generic edit form, Shopfoo uses a **Task-Based UI**: each user intention maps to a dedicated, focused command. This avoids the anaemic CRUD screens typical of back-offices and makes the business intent explicit.

{% hint style="info" %}
For an introduction to Task-Based UIs, see Derek Comartin's video [Task-Based UI](https://www.youtube.com/watch?v=BgRMHpqxVKA).
{% endhint %}

📸 _Screenshot: product page showing the available task commands_

## Product editing

The **Edit product** task allows users to modify the descriptive information of a product (name, subtitle, author, etc.). This maps to the **Catalog** business area.

📸 _Screenshot: product edit form_

## Pricing management

The **Update pricing** task manages the two price fields independently:

- **Retail price** — the price charged to the customer.
- **List price** (MSRP) — the manufacturer's recommended retail price.

This is a separate task from general editing because pricing decisions often involve different roles or approval workflows.

## Purchase entry

The **Record purchase** task simulates a **stock arrival from a supplier**. It represents goods received from a supplier, affecting both the **Purchases** area (supplier order) and the **Warehouse** area (stock increase).

Users enter the quantity received. The stock level is updated accordingly.

📸 _Screenshot: purchase entry form (stock arrival)_

## Sales entry

The **Record sale** task simulates a **customer purchase** on the storefront. It affects the **Sales** area (revenue) and the **Warehouse** area (stock decrease).

Users enter the quantity sold. The stock level is decremented accordingly.

📸 _Screenshot: sales entry form_

## Stock adjustment

The **Adjust stock** task simulates an **inventory correction** following a warehouse count. It falls under the **Warehouse** area and allows entering a positive or negative quantity delta to reconcile the recorded stock with the physical count.

📸 _Screenshot: stock adjustment form_

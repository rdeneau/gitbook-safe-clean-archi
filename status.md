---
description: Indicates the progress status of the book and its companion app, Shopfoo.
icon: location-check
---

# Status

## 2026-11-18

* _Shopfoo_: Migrate to .NET 10, Fable 5, and Feliz v3 — the improvement planned on [2026-03-12](#2026-03-12) — and to the `.slnx` solution format
* _Shopfoo_: [Release 1.4.2](https://github.com/rdeneau/shopfoo/releases/tag/v1.4.2) deployed

## 2026-04-29

* _GitBook_: New [Outreach](appendices/outreach/README.md) article — [What if your pattern matching could call functions?](appendices/outreach/active-patterns.md) — covering F# Active Patterns illustrated with Shopfoo examples

## 2026-04-14

* _Shopfoo_: Apply `[<CallerMemberName>]` in `IInstructionPreparer` extension methods
* _GitBook_: Update [Program Runner](domain-workflows/2-program/#auto-deriving-instruction-names-with-callermembername) and [Api](domain-workflows/3-domain-workflow/4-api.md)

## 2026-03-30

* _GitBook_: New [Outreach](appendices/outreach/README.md) section in Appendices — lists LinkedIn posts promoting the GitBook

## 2026-03-25

* _GitBook_: New [Pattern Variations](domain-workflows/2-program/pattern-variations.md) addendum — compares Free Monad and Tagless Final implementation variations (John Azariah's approaches, gitbook V3/V3bis/V4), covering instruction typing, parallelism, combinators, and turnkey vs generic design
* _GitBook_: Update [Algebraic Effects](domain-workflows/1-introduction/4-algebraic-effects.md) page — note that V3bis variant enables parallelism in the Free Monad

## 2026-03-12

* ✅ GitBook complete
* ✅ Shopfoo complete, [release 1.4.1](https://github.com/rdeneau/shopfoo/releases/tag/v1.4.1) deployed \
  💡 Eventual improvements:
  * [ ] Integrate and document Playwright
  * [ ] Migrate to .NET 10, Fable 5 (currently in [RC](https://fable.io/blog/2026/2026-02-27-Fable_5_release_candidate.html)), Feliz v3

## 2026-02-10

* _Shopfoo_: Start implementing the [Saga support](domain-workflows/2-program/#saga-support-undo) execution mode for the `Program`, enabling undo of completed instructions when a workflow fails

## 2026-02-07

* _Shopfoo_: Start migrating the `Program` (used in [domain workflows](domain-workflows/2-program/)) to v4—TagLess Final pattern—to support parallel instruction execution
* The last version of _Shopfoo_ code with Program v3 is available via the [`program-v3`](https://github.com/rdeneau/shopfoo/tree/program-v3/) tag

## 2025-12-31

* Write [Motivations](motivations.md)
* Publish _Shopfoo_ [version 1.2](https://github.com/rdeneau/shopfoo/releases/tag/v1.2.0): complete any feature "under construction" (displayed with the 🚧 emoji). There are still some features to implement - see [README.md#features](https://github.com/rdeneau/shopfoo/tree/main#features)

## 2025-12-20

* Announcing the start of writing this book, as an entry to the [F# Advent Calendar in English 2025](https://sergeytihon.com/2025/11/03/f-advent-calendar-in-english-2025/) – Thank you, Sergey Tihon 🙏
* _Shopfoo_ is functional and deployed for the playground. Some features are missing and the code can be improved. Still, the code can be explore in preview if needed.

## Changelog

{% hint style="info" %}
This section is auto-generated. It displays the last 10 commits of both repositories.
{% endhint %}

<details>

<summary><i class="fa-book-open">:book-open:</i>  GitBook</summary>

* \[Nov 19, 2026] docs: 📝 add .NET 10 migration entry to status
* \[Oct 26, 2026] feat: 🔄️ [GITBOOK-21] Fix Shopfoo chapter link in the Welcome page
* \[Apr 29, 2026] feat: 🔄️ [GITBOOK-20] fix outreach internal links
* \[Apr 29, 2026] feat: ✨ add active-patterns.md outreach article
* \[Apr 17, 2026] feat: ✨ new outreach article / architecture - add dates
* \[Apr 16, 2026] fix: ✏️ wording around bounded contexts and vertical slices
* \[Apr 14, 2026] feat: ♻️ improve instruction preparation: make their name optional, inferred with CallerMemberName attribute
* \[Apr 08, 2026] feat: ✨ TanStack Query outreach link
* \[Mar 30, 2026] feat: ✨ appendices/outreach page
* \[Mar 25, 2026] feat: 🔄️ [GITBOOK-19] fix addendum

</details>

<details>

<summary><i class="fa-tablet-screen">:tablet-screen:</i>  Shopfoo</summary>

* \[Nov 18, 2026] test: ✅ fix flaky filter books by tag test
* \[Nov 18, 2026] chore: 🏷️ release 1.4.2 [skip ci]
* \[Nov 18, 2026] fix: 🐛 shorten a seed description over the 512-character limit
* \[Nov 18, 2026] fix: 🔒️ pin patched ASP.NET Core and Pkcs packages
* \[Nov 18, 2026] test: ✅ sort books by tags on the app's flat key
* \[Nov 18, 2026] fix: 🐛 vendor a remoting client patched for Fable 5
* \[Nov 18, 2026] chore: ⬆️ upgrade to Fable 5.27 and Feliz 3
* \[Jul 08, 2026] chore: update Fable compiler + Feliz
* \[Nov 18, 2026] chore: 🔧 migrate the solution to slnx
* \[Nov 18, 2026] test: ✅ fix flaky search books by subtitle test

</details>

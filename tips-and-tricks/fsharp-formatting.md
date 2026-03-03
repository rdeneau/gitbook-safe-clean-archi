# F# code formatting with Fantomas

🏷️ `#Tooling` `#CodeStyle`

[Fantomas](https://fsprojects.github.io/fantomas/) is the standard F# code formatter. It enforces a uniform style across the entire codebase based on the [Microsoft F# style guide](https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/formatting). Install it as a .NET local tool (`dotnet tool install fantomas`) and run it with `dotnet fantomas <paths>`.

## Running Fantomas

In *Shopfoo*, as a single-contributor project where formatting discipline is maintained manually, I run Fantomas on demand:

- **From the IDE** — JetBrains Rider has built-in Fantomas integration (reformat on save or on demand).
- **From the command line** — `dotnet fantomas src tests` formats the entire codebase. Specifying `src` and `tests` directories avoids reformatting code from NuGet packages (Fable packages often ship F# source files that should not be touched).

{% hint style="info" %}
For team projects, you can add a CI step that fails the build when code is not formatted: `dotnet fantomas --check src tests`. This is effective for enforcing consistency but can be annoying when it blocks a hotfix. Not recommended for repositories where you need to push and deploy quickly.
{% endhint %}

## Configuration via `.editorconfig`

The Microsoft style guide leaves room for personal preferences. Fantomas reads its configuration from the standard `.editorconfig` file, using keys prefixed with `fsharp_`. Here is the configuration used in Shopfoo:

```ini
[*.{fs,fsx}]
max_line_length = 150
fsharp_max_infix_operator_expression = 100
fsharp_max_dot_get_expression_width = 100
fsharp_max_function_binding_width = 120
fsharp_max_value_binding_width = 120
fsharp_multiline_bracket_style = stroustrup
fsharp_multi_line_lambda_closing_newline = true
fsharp_bar_before_discriminated_union_declaration = false
fsharp_keep_max_number_of_blank_lines = 1
fsharp_record_multiline_formatter = number_of_items
fsharp_max_record_number_of_items = 2
fsharp_array_or_list_multiline_formatter = number_of_items
fsharp_max_array_or_list_number_of_items = 2
```

The full list of configuration keys is documented in the [Fantomas configuration reference](https://fsprojects.github.io/fantomas/docs/end-users/Configuration.html). The following sections explain the rationale behind each group of settings.

### Bracket style: Stroustrup

`fsharp_multiline_bracket_style = stroustrup`

Fantomas supports three bracket styles for multiline records, lists, and arrays:

| Style                           | Opening bracket                    | Closing bracket                        |
| ------------------------------- | ---------------------------------- | -------------------------------------- |
| **Cramped** (default before v6) | Same line as declaration           | Same line as last field                |
| **Aligned**                     | Own line, aligned with declaration | Own line, aligned with opening bracket |
| **Stroustrup**                  | Same line as declaration           | Own line, aligned with declaration     |

Stroustrup is a good middle ground: more readable than Cramped (which can bury the closing bracket), less verbose than Aligned (which adds an extra line for the opening bracket).

```fsharp
// Stroustrup — record type
type FullContext = {
    Lang: Lang
    User: User
    Token: AuthToken option
    Translations: AppTranslations
}

// Stroustrup — record value
static member Default: FullContext = {
    Lang = Lang.English
    User = User.Anonymous
    Token = None
    Translations = AppTranslations()
}

// Stroustrup — list in function call
Cmd.batch [
    Cmd.navigatePage currentPage
    Cmd.ofMsg (Msg.ThemeChanged defaultTheme)
]
```

### Line length and expression widths

```ini
max_line_length = 150
fsharp_max_function_binding_width = 120
fsharp_max_value_binding_width = 120
fsharp_max_infix_operator_expression = 100
fsharp_max_dot_get_expression_width = 100
```

Fantomas breaks expressions into multiple lines when they exceed these thresholds. The goal is to keep **groups of similar lines formatted uniformly**: if one line in a `match` expression wraps while its siblings stay on one line, the visual inconsistency hinders readability.

Using generous limits (150 for the overall line, 120 for bindings) reduces unwanted line breaks. Expression types that read better across multiple lines (infix operators, dot chains) have a lower threshold of 100.

### Item-count-based multiline for records and lists

```ini
fsharp_record_multiline_formatter = number_of_items
fsharp_max_record_number_of_items = 2
fsharp_array_or_list_multiline_formatter = number_of_items
fsharp_max_array_or_list_number_of_items = 2
```

Rather than relying on character width alone, these settings switch to multiline based on the **number of items**. A record or list with more than 2 items is always formatted multiline, regardless of how short the items are. This produces a more predictable layout.

## The `// ↩` trick: forcing multiline formatting

Even with carefully tuned thresholds, Fantomas sometimes collapses an expression onto a single line where the multiline form is more readable. A trailing comment prevents this, because Fantomas never joins a line that ends with a comment to the next line.

When a meaningful comment fits naturally, use that. When no comment makes sense, the convention in *Shopfoo* is to use a single `↩` character — the visual noise is minimal and the intent is clear:

```fsharp
// Without // ↩, Fantomas would write:
| Msg.Logout -> { model with ... }, Cmd.navigatePage Page.Login

// With // ↩, the two parts stay on separate lines, to spot easily the Cmd:
| Msg.Logout ->
    { model with Model.FullContext.User = User.Anonymous }, // ↩
    Cmd.navigatePage Page.Login
```

Another common case is a short `match` arm body:

```fsharp
// Without // ↩, Fantomas would collapse to one line:
| Msg.FillTranslations translations -> { model with ... }, Cmd.none

// With // ↩, the body is on its own line:
| Msg.FillTranslations translations -> // ↩
    { model with FullContext = model.FullContext.FillTranslations(translations) }, Cmd.none
```

{% hint style="tip" %}
In Elmish `update` functions, `Cmd.none` can stay at the end of the line — there is nothing to notice. But effective commands (`Cmd.navigatePage`, `Cmd.batch`, `Cmd.ofMsg`...) carry important side effects and should stand out visually. Adding `// ↩` ensures they appear on their own line, making them easy to spot during code review.
{% endhint %}

### `// ↩` in Feliz views

The `fsharp_max_array_or_list_number_of_items = 2` setting has a side effect in Feliz code: a component with only 1 or 2 props can be collapsed to a single line by Fantomas. This is not idiomatic for UI code, where each prop typically goes on its own line:

```fsharp
// Without // ↩, Fantomas produces:
Daisy.fieldsetLabel [ prop.key "current-stock-label"; prop.text translations.Product.StockBeforeInventory ]

// With // ↩, one prop per line:
Daisy.fieldsetLabel [ // ↩
    prop.key "current-stock-label"
    prop.text translations.Product.StockBeforeInventory
]
```

The same applies to `prop.children` with 2 children, `Html.span` with 2 props, etc. This is the main drawback of the item-count-based setting — `// ↩` comments are more frequent in Feliz view code. The trade-off is acceptable because the alternative (`number_of_items = 1`, forcing every single-element list to be multiline) would be too aggressive elsewhere.

### `// ↩` to keep groups of similar lines uniform

Despite generous thresholds, Fantomas may still wrap some lines in a group while leaving others on a single line — typically when one binding is slightly longer than its siblings. This inconsistency hurts readability because the eye expects similar structures to look the same.

The fix is to add `// ↩` on the shorter lines to force them multiline too, so that every binding in the group follows the same shape. Consider the `Claims` module from `Users.fs`:

```fsharp
module private Claims =
    let catalogEditor: Claims =
        guest // ↩
        |> Map.add Feat.Catalog Access.Edit
        |> Map.add Feat.Sales Access.View
        |> Map.add Feat.Warehouse Access.View

    let sales: Claims =
        guest // ↩
        |> Map.add Feat.Sales Access.Edit
        |> Map.add Feat.Warehouse Access.Edit

    let productManager: Claims =
        guest // ↩
        |> Map.add Feat.Catalog Access.Edit
        |> Map.add Feat.Sales Access.Edit
        |> Map.add Feat.Warehouse Access.Edit

    let admin: Claims =
        productManager // ↩
        |> Map.add Feat.Admin Access.Edit
```

Without `// ↩`, Fantomas would collapse the shorter bindings `let sales` and `let admin` onto fewer lines, while `catalogEditor` and `productManager` would remain multiline — breaking the visual rhythm — see below. With `// ↩`, every binding starts with the base value on its own line followed by the pipeline, making the progressive enrichment pattern immediately apparent.

```fsharp
module private Claims =
    let catalogEditor: Claims =
        guest
        |> Map.add Feat.Catalog Access.Edit
        |> Map.add Feat.Sales Access.View
        |> Map.add Feat.Warehouse Access.View

    let sales: Claims = guest |> Map.add Feat.Sales Access.Edit |> Map.add Feat.Warehouse Access.Edit

    let productManager: Claims =
        guest
        |> Map.add Feat.Catalog Access.Edit
        |> Map.add Feat.Sales Access.Edit
        |> Map.add Feat.Warehouse Access.Edit

    let admin: Claims = productManager |> Map.add Feat.Admin Access.Edit
```

### Other settings

- `fsharp_multi_line_lambda_closing_newline = true`: Places the closing `)` of a multiline lambda on its own line, visually separating the lambda body from the surrounding code.
- `fsharp_bar_before_discriminated_union_declaration = false`: Omits the leading `|` on the first case. Slightly more compact and consistent with record syntax.
- `fsharp_keep_max_number_of_blank_lines = 1`: Prevents accumulation of blank lines. A single blank line is enough to separate logical sections.

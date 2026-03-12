---
icon: code-pull-request
---

# Versioning

Shopfoo uses [Semantic Release](https://semantic-release.gitbook.io/) to automate versioning and changelog generation. The version is determined from commit messages following the [Conventional Commits](https://www.conventionalcommits.org/) specification, computed during CI, embedded into the deployed application, and displayed in its [About page](../shopfoo/general.md#about-page).

## Overview

The versioning pipeline runs in GitHub Actions and follows this flow:

1. A developer triggers the release workflow manually.
2. _Semantic Release_ analyzes commit messages since the last release and determines the next version number.
3. The version and release date are written into `package.json`.
4. A changelog entry is generated in `CHANGELOG.md`.
5. Both files are committed and a GitHub release is created.
6. The application is built and deployed to Azure.

The version is then visible in the application's About page, imported directly from `package.json` at build time.

## Commit conventions and release rules

Commit messages follow the _Conventional Commits_ specification, written with [Commitji](https://github.com/rdeneau/commitji) — a dotnet tool that adds a [Gitmoji](https://gitmoji.dev/)-inspired emoji between the prefix and the description (e.g. `feat: ✨ add stock adjustment`). The emoji is purely decorative and does not affect versioning — only the prefix matters.

Semantic Release uses the `conventionalcommits` preset to map commit types to version bumps. The rules are defined in `.releaserc.json`:

| Commit type  | Version bump |
| ------------ | ------------ |
| `breaking`   | **Major**    |
| `feat`       | Minor        |
| `fix`        | Patch        |
| `perf`       | Patch        |
| `refactor`   | Patch        |
| `test`       | _No release_ |
| `chore`      | _No release_ |
| `docs`       | _No release_ |
| `revert`     | _No release_ |
| `tidy`       | _No release_ |
| `wip`        | _No release_ |

For example, a commit message like `feat: add stock adjustment` triggers a minor version bump, while `fix: correct price calculation` triggers a patch bump.

## Semantic Release configuration

The `.releaserc.json` file orchestrates six plugins in order:

```json
{
  "branches": ["main"],
  "plugins": [
    ["@semantic-release/commit-analyzer", { "preset": "conventionalcommits", "releaseRules": [...] }],
    ["@semantic-release/release-notes-generator", { "preset": "conventionalcommits" }],
    ["@semantic-release/changelog", { "changelogFile": "CHANGELOG.md" }],
    ["@semantic-release/npm", { "npmPublish": false }],
    ["@semantic-release/exec", { "prepareCmd": "node update-release-date.js ${nextRelease.version}" }],
    ["@semantic-release/git", { "assets": ["package.json", "CHANGELOG.md"], "message": "chore: 🏷️ release ${nextRelease.version} [skip ci]" }],
    "@semantic-release/github"
  ]
}
```

Each plugin has a specific role:

- **commit-analyzer** — Determines the version bump from commit messages.
- **release-notes-generator** — Generates structured release notes from commits.
- **changelog** — Appends the release notes to `CHANGELOG.md`.
- **npm** — Updates the `version` field in `package.json` (publishing is disabled since this is a private package).
- **exec** — Runs `update-release-date.js` to stamp the current date into `package.json`.
- **git** — Commits the updated `package.json` and `CHANGELOG.md` with a `[skip ci]` marker to avoid retriggering the pipeline.
- **github** — Creates a GitHub release with the generated notes.

## Release date script

The `update-release-date.js` script is called by the `exec` plugin during the release. It writes today's date (in `YYYY-MM-DD` format) into a custom `releaseDate` field in `package.json`:

```javascript
const today = new Date();
const releaseDate = today.toISOString().split('T')[0];
packageJson.releaseDate = releaseDate;
writeFileSync(packageJsonPath, JSON.stringify(packageJson, null, 2) + '\n', 'utf-8');
```

After a release, `package.json` contains both the version and the release date:

```json
{
  "version": "1.2.0",
  "releaseDate": "2025-12-30"
}
```

## Version in the application

The F# client imports metadata directly from `package.json` using Fable's `[<ImportMember>]` attribute:

```fsharp
[<RequireQualifiedAccess>]
module Shopfoo.Client.Package

open Fable.Core

[<ImportMember("../../package.json")>]
let version: string = jsNative

[<ImportMember("../../package.json")>]
let releaseDate: string = jsNative

[<ImportMember("../../package.json")>]
let author: string = jsNative

[<ImportMember("../../package.json")>]
let homepage: string = jsNative

[<ImportMember("../../package.json")>]
let repository: {| url: string |} = jsNative
```

Vite resolves the `import` from `package.json` at build time, so the values are inlined into the JavaScript bundle. There is no runtime fetch — the version is baked into the compiled output.

The About page displays this information using DaisyUI badges:

```fsharp
prop.text $"🏷️ Version %s{Package.version} (%s{Package.releaseDate})"
```

This renders as something like: **🏷️ Version 1.2.0 (2025-12-30)**.

{% hint style="info" %}
See the [About page](../shopfoo/general.md#about-page) section in _General Features_ for a screenshot and a description of the information displayed.
{% endhint %}

## GitHub Actions workflow

The release pipeline is defined in `.github/workflows/release.yml` and consists of three jobs:

### Release

Runs Semantic Release via the `cycjimmy/semantic-release-action` action. It outputs two values consumed by downstream jobs:

- `new-release-published` — Whether a new version was created.
- `new-release-version` — The version string (e.g. `1.2.0`).

The checkout uses `fetch-depth: 0` so that Semantic Release has access to the full commit history for analysis.

### Build

Runs only if a new release was published (or if `force_deploy` is enabled). It checks out the code at the release tag, sets up .NET 9 and Node.js 22, then runs `dotnet run Publish` — the FAKE build target that compiles the server, transpiles the F# client via Fable, and bundles the front-end via Vite. The output is uploaded as a `shopfoo-app` artifact.

### Deploy

Downloads the build artifact and deploys it to an Azure Web App.

### Workflow inputs

The workflow is triggered manually (`workflow_dispatch`) with two optional inputs:

- **`dry-run`** — Runs Semantic Release without actually publishing. Useful for previewing what version would be created.
- **`force_deploy`** — Deploys from the latest existing release tag, bypassing the need for a new release. Useful for redeployments after infrastructure changes.

## Changelog

The `CHANGELOG.md` file is generated automatically by the `@semantic-release/changelog` plugin. Each release entry includes:

- A SemVer comparison link to the GitHub diff (e.g. `v1.1.0...v1.2.0`).
- The release date.
- Commits grouped by type (Features, Bug Fixes, etc.).
- Links to individual commits on GitHub.

```markdown
## [1.2.0](https://github.com/rdeneau/shopfoo/compare/v1.1.0...v1.2.0) (2025-12-30)

### Features

* ✨ adjust stock after inventory (9e6d0f4)
* ✨ fetch stock ; verifyZeroStock in MarkAsSoldOut workflow (a13beb9)
```

## Data flow summary

The full versioning lifecycle:

1. **Commit** — Developers write commits following _Conventional Commits_ with [Commitji](https://github.com/rdeneau/commitji) (`feat: ✨ add feature`, `fix: 🐛 fix bug`, etc.).
2. **Release trigger** — A maintainer manually triggers the release workflow in GitHub Actions.
3. **Version calculation** — _Semantic Release_ analyzes commits since the last tag and determines the next SemVer version.
4. **File updates** — `package.json` gets the new `version` and `releaseDate`; `CHANGELOG.md` gets a new entry.
5. **Git commit and tag** — The updated files are committed with `[skip ci]` and a Git tag is created.
6. **GitHub release** — A GitHub release is published with auto-generated notes.
7. **Build** — Fable transpiles the F# client; Vite bundles the front-end, inlining the version from `package.json`.
8. **Deploy** — The built artifact is deployed to Azure Web App.
9. **Display** — The _About_ page shows the version and release date imported from `package.json`.

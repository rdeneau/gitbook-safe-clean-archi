open System
open System.IO
open FsHttp

// Define types to match GitHub API response
// See https://docs.github.com/en/rest/commits/commits?apiVersion=2022-11-28#list-commits
type CommitAuthor = { date: DateTime; email: string; name: string }
type Commit = { message: string; author: CommitAuthor }
type CommitEnvelope = { commit: Commit }

type Repository = { token: string; owner: string; name: string; limit: int }

let [<Literal>] ChangelogStartMarker = "<!-- CHANGELOG:START -->"
let [<Literal>] ChangelogEndMarker = "<!-- CHANGELOG:END -->"
let [<Literal>] Limit = 10

let fetchCommits (repo: Repository) =
    http {
        GET $"https://api.github.com/repos/%s{repo.owner}/%s{repo.name}/commits?per_page=%i{repo.limit}"
        Authorization $"token %s{repo.token}"
        UserAgent $"%s{repo.owner}-%s{repo.name}-ChangelogGenerator"
        Accept "application/vnd.github.v3+json"
    }
    |> Request.send
    |> Response.deserializeJson<CommitEnvelope list>

let isUpdateStatusMdCommitMessage (message: string) =
    message.StartsWith("docs")
    && message.EndsWith("[skip ci]")
    && (message.Contains("update changelog.md") || message.Contains("update status.md"))

let formatCommit (commit: Commit) =
    let date = commit.author.date.ToString("MMM dd, yyyy")
    let message = commit.message.Split('\n').[0] // First line of commit message
    if message |> isUpdateStatusMdCommitMessage then ""
    else $"* \\[{date}] {message}"

let changelogSection repo title =
    let commits =
        fetchCommits repo
        |> Seq.map _.commit
        |> Seq.map formatCommit
        |> Seq.filter (fun s -> s <> "")
        |> Seq.truncate Limit

    [
        $"### %s{title}"
        ""
        yield! commits
    ]
    |> String.concat "\n"

let hintInfo =
    [
        """{% hint style="info" %}"""
        $"This section is auto-generated from a GitHub action. It displays the last %i{Limit} commits of both repositories."
        "{% endhint %}"
    ]
    |> String.concat "\n"

let updateChangelogFile ghToken =
    let statusFilePath = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "status.md")
    let statusContent = File.ReadAllText(statusFilePath)

    let repo name limit =
        { token = ghToken; owner = "rdeneau"; name = name; limit = limit }

    let gitbookChangelog =
        "📖 GitBook"
        |> changelogSection (repo "gitbook-safe-clean-archi" (2 * Limit))

    let shopfooChangelog =
        "👉 Shopfoo ![GitHub Release](https://img.shields.io/github/v/release/rdeneau/shopfoo?label=VERSION)"
        |> changelogSection (repo "shopfoo" Limit)

    let newChangelog =
        [
            hintInfo
            ""
            gitbookChangelog
            ""
            shopfooChangelog
        ]
        |> String.concat "\n"

    let startIndex = statusContent.IndexOf(ChangelogStartMarker) + ChangelogStartMarker.Length
    let endIndex = statusContent.IndexOf(ChangelogEndMarker)

    let newStatusContent =
        let before = statusContent.Substring(0, startIndex)
        let after = statusContent.Substring(endIndex)
        $"{before}\n{newChangelog}\n{after}"

    File.WriteAllText(statusFilePath, newStatusContent)

[<EntryPoint>]
let main argv =
    let ghToken =
        if argv.Length > 0 then argv.[0]
        else Environment.GetEnvironmentVariable("GH_TOKEN")

    if String.IsNullOrEmpty(ghToken) then
        eprintfn "Error: GitHub token not provided. Pass as an argument or set GH_TOKEN environment variable."
        1 // error
    else
        updateChangelogFile ghToken
        0 // success
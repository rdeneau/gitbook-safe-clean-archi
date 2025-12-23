open System
open System.IO
open FsHttp

// Define types to match GitHub API response
// See https://docs.github.com/en/rest/commits/commits?apiVersion=2022-11-28#list-commits
type CommitAuthor = { date: DateTime; email: string; name: string }
type Commit = { message: string; author: CommitAuthor }
type CommitEnvelope = { commit: Commit }

let [<Literal>] ChangelogStartMarker = "<!-- CHANGELOG:START -->"
let [<Literal>] ChangelogEndMarker = "<!-- CHANGELOG:END -->"
let [<Literal>] Limit = 10

let fetchCommits ghToken owner repo limit =
    http {
        GET $"https://api.github.com/repos/%s{owner}/%s{repo}/commits?per_page=%i{limit}"
        Authorization $"token %s{ghToken}"
        UserAgent $"%s{owner}-%s{repo}-ChangelogGenerator"
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

let changelogSection ghToken title owner repo limit =
    let commits =
        fetchCommits ghToken owner repo limit
        |> Seq.map _.commit
        |> Seq.map formatCommit
        |> Seq.filter (fun s -> s <> "")
        |> Seq.truncate Limit

    [
        $"### {title}"
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

    let gitbookChangelog = changelogSection ghToken "📖 GitBook" "rdeneau" "gitbook-safe-clean-archi" (2 * Limit)
    let shopfooChangelog = changelogSection ghToken "👉 Shopfoo" "rdeneau" "shopfoo" Limit

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
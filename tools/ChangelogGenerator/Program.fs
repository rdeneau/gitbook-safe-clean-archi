open System
open System.IO
open FsHttp

type CommitAuthor = { date: DateTime }
type CommitDetails = { message: string; author: CommitAuthor }
type Commit = { commit: CommitDetails }

let [<Literal>] HARD_LIMIT = 20
let [<Literal>] SOFT_LIMIT = 5

let fetchCommits ghToken owner repo limit =
    http {
        GET $"https://api.github.com/repos/%s{owner}/%s{repo}/commits?per_page=%i{limit}"
        Authorization $"token %s{ghToken}"
        UserAgent $"%s{owner}-%s{repo}-ChangelogGenerator"
        Accept "application/vnd.github.v3+json"
    }
    |> Request.send
    //|> Response.toFormattedText |> printf "Response: %s"
    |> Response.deserializeJson<Commit list>

let isUpdateStatusMdCommitMessage (message: string) =
    message.StartsWith("docs")
    && message.EndsWith("[skip ci]")
    && (message.Contains("update changelog.md") || message.Contains("update status.md"))

let formatCommit (commit: Commit) =
    let date = commit.commit.author.date.ToString("MMM dd, yyyy")
    let message = commit.commit.message.Split('\n').[0] // First line of commit message
    if message |> isUpdateStatusMdCommitMessage then ""
    else $"* \\[{date}] {message}"

let generateChangelogSection ghToken title owner repo limit =
    let commits =
        fetchCommits ghToken owner repo limit
        |> List.map formatCommit
        |> List.filter (fun s -> s <> "")
        |> List.truncate SOFT_LIMIT

    [
        $"### {title}"
        ""
        yield! commits
    ]
    |> String.concat "\n"

[<EntryPoint>]
let main argv =
    let ghToken =
        if argv.Length > 0 then argv.[0]
        else Environment.GetEnvironmentVariable("GH_TOKEN")

    if String.IsNullOrEmpty(ghToken) then
        printfn "Error: GitHub token not provided. Pass as an argument or set GH_TOKEN environment variable."
        1 // Return error code
    else
        let statusFilePath = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "status.md")
        let statusContent = File.ReadAllText(statusFilePath)

        let changelogStartMarker = "<!-- CHANGELOG:START -->"
        let changelogEndMarker = "<!-- CHANGELOG:END -->"

        let startIndex = statusContent.IndexOf(changelogStartMarker) + changelogStartMarker.Length
        let endIndex = statusContent.IndexOf(changelogEndMarker)

        let gitbookChangelog = generateChangelogSection ghToken "📖 GitBook" "rdeneau" "gitbook-safe-clean-archi" HARD_LIMIT
        let shopfooChangelog = generateChangelogSection ghToken "👉 Shopfoo" "rdeneau" "shopfoo" SOFT_LIMIT

        let newChangelog =
            [ gitbookChangelog; shopfooChangelog ]
            |> String.concat "\n\n"

        let newStatusContent =
            let before = statusContent.Substring(0, startIndex)
            let after = statusContent.Substring(endIndex)
            $"{before}\n{newChangelog}\n{after}"

        File.WriteAllText(statusFilePath, newStatusContent)
        0 // Return success code

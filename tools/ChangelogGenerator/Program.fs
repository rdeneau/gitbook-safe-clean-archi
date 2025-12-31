open System
open System.IO
open FsHttp

// Define types to match GitHub API response
// See https://docs.github.com/en/rest/commits/commits?apiVersion=2022-11-28#list-commits
type CommitAuthor = { date: DateTime; email: string; name: string }
type Commit = { message: string; author: CommitAuthor }
type CommitEnvelope = { commit: Commit }

type Repository = { token: string; owner: string; name: string; limit: int }

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

let changelogLines (repo: Repository) =
    fetchCommits repo
    |> Seq.map _.commit
    |> Seq.map formatCommit
    |> Seq.filter (fun s -> s <> "")
    |> Seq.truncate Limit
    |> Seq.toList

let replaceExpandableSectionContent (sectionName: string) (newLines: string list) (statusContent: string) =
    let tryFindIndexOf (substring: string) (startIndex: int) =
        let index = statusContent.IndexOf(substring, startIndex, StringComparison.Ordinal)
        if index < startIndex then None else Some index

    let orFailWith errorMessage num =
        match num with
        | Some n -> n
        | None -> failwith errorMessage

    let sectionTitleEnd = $"%s{sectionName}</summary>"

    let sectionTitleEndIndex =
        tryFindIndexOf sectionTitleEnd 0
        |> orFailWith $"Could not find section '%s{sectionName}' in status.md"

    let contentStartIndex =
        sectionTitleEndIndex + sectionTitleEnd.Length

    let detailsEndIndex =
        tryFindIndexOf "</details>" contentStartIndex
        |> orFailWith $"Could not find </details> closing tag for section '%s{sectionName}'"

    let replacementContent =
        let content = newLines |> String.concat "\n"
        if String.IsNullOrWhiteSpace(content) then "\n\n" else $"\n\n{content}\n\n"

    let before = statusContent.Substring(0, contentStartIndex)
    let after = statusContent.Substring(detailsEndIndex)
    $"{before}{replacementContent}{after}"

let updateChangelogFile ghToken =
    let statusFilePath = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "status.md")
    let statusContent = File.ReadAllText(statusFilePath)

    let repo name limit =
        { token = ghToken; owner = "rdeneau"; name = name; limit = limit }

    let gitbookLines = changelogLines (repo "gitbook-safe-clean-archi" (2 * Limit)) // Fetch twice more commits for GitBook to compensate for filtered ones.
    let shopfooLines = changelogLines (repo "shopfoo" Limit)

    let newStatusContent =
        statusContent
        |> replaceExpandableSectionContent "GitBook" gitbookLines
        |> replaceExpandableSectionContent "Shopfoo" shopfooLines

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
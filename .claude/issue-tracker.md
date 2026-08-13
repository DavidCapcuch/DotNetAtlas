# Issue tracker: GitHub

Issues and PRDs live as GitHub issues in `DavidCapcuch/DotNetAtlas`. Use the `gh` CLI for all
operations; it infers the repo from `git remote -v` when run inside a clone.

- **Create**: `gh issue create --title "..." --body "..."` — heredoc for multi-line bodies.
- **Read**: `gh issue view <number> --comments`.
- **List**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`, with `--label` / `--state` filters.
- **Default page size is 30** — `gh issue list` / `gh search` cap there unless you pass `--limit <n>`. Raise it before concluding a label, issue or PR is absent.
- **Comment**: `gh issue comment <number> --body "..."`
- **Labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

## Triage labels

The skills' canonical triage roles map 1:1 onto this repo's label strings, used as-is with no
remapping: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`.
`gh label list --limit 200` is the live set.

## Working an issue

**Correcting a `ready-for-agent` issue: edit the body, don't just comment.** A dispatched agent
reads the body top-down and acts on it; a correction sitting in a comment is one it may never
reach. Use `gh issue edit <number> --body-file <file>` for anything that changes *what gets built*
— scope, constraints, acceptance criteria. Comments are for the audit trail and for humans.

**PRs are not a request surface** — triage covers issues only. Nothing arrives here as an external
contribution, so a PR is always implementation of an already-filed issue, never a request to be
judged. GitHub shares one number space across issues and PRs, so a bare `#42` may be either —
resolve with `gh pr view 42`, falling back to `gh issue view 42`.

When a skill says "publish to the issue tracker", create a GitHub issue. When it says "fetch the
relevant ticket", run `gh issue view <number> --comments`.

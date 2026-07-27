# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`, filtering comments by `jq` and also fetching labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Default page size is 30**: `gh issue list` / `gh search` return at most 30 results unless you pass `--limit <n>` — raise it before concluding a label / issue / PR is absent.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Correcting a `ready-for-agent` issue: edit the body, don't just comment.** A dispatched agent reads the body top-down and acts on it; a correction sitting in a comment is one it may never reach. Use `gh issue edit <number> --body-file <file>` for anything that changes *what gets built* — scope, constraints, acceptance criteria. Comments are for the audit trail and for humans: say what changed and why there, but land the change itself in the body.
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v` — `gh` does this automatically when run inside a clone.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

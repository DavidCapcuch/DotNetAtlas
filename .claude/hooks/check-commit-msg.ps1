# PreToolUse hook — blocks `git commit` when the subject line is not a Conventional Commit.
# Mirrors the repo's CI convention (pr-conventional-commit-validation.yml validates the PR title;
# this keeps every commit Claude authors conformant locally, before it is ever created).
#
# Fail-OPEN by design: only blocks when it can see a clearly non-conforming literal subject.
# When the message is built dynamically (-F file, $()-substitution, editor) it allows the commit.
$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }

$cmd = $payload.tool_input.command
if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }

# Only inspect `git commit` invocations.
if ($cmd -notmatch '(^|[\s;&|(])git\s+commit(\s|$)') { exit 0 }

# Best-effort extraction of the commit subject (first line of the message).
# Heredoc / here-string forms are checked first so `-m "$(cat <<'EOF' ...)"` resolves to the
# real first body line rather than the literal `$(...)` wrapper.
$subject = $null
if     ($cmd -match "@'\s*\r?\n\s*([^\r\n]+)")                                { $subject = $matches[1] }  # PowerShell here-string @'...'@
elseif ($cmd -match "<<-?\s*'?[A-Za-z_][A-Za-z0-9_]*'?\s*\r?\n\s*([^\r\n]+)") { $subject = $matches[1] }  # bash heredoc
elseif ($cmd -match '--message[= ]+"([^"]*)"')                               { $subject = $matches[1] }
elseif ($cmd -match "--message[= ]+'([^']*)'")                               { $subject = $matches[1] }
elseif ($cmd -match '(?:^|\s)-[A-Za-z]*m\s+"([^"]*)"')                       { $subject = $matches[1] }  # -m / -am "..."
elseif ($cmd -match "(?:^|\s)-[A-Za-z]*m\s+'([^']*)'")                       { $subject = $matches[1] }  # -m / -am '...'

# Could not see a literal subject, or it is dynamically built -> do not judge.
if ([string]::IsNullOrWhiteSpace($subject)) { exit 0 }
if ($subject -match '\$\(') { exit 0 }

$pattern = '^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([^)]+\))?!?: .+'
if ($subject -notmatch $pattern) {
    [Console]::Error.WriteLine("Blocked: commit subject is not a Conventional Commit.")
    [Console]::Error.WriteLine("  subject : $subject")
    [Console]::Error.WriteLine("  expected: <type>(<scope>): <description>")
    [Console]::Error.WriteLine("  example : feat(bff): add basket mutation endpoints")
    [Console]::Error.WriteLine("  types   : feat fix docs style refactor perf test build ci chore revert")
    exit 2
}
exit 0

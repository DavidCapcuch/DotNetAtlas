# Stop hook — formats only the C# files changed in this turn, batched into a single run.
# Pre-empts the CI auto-format commit (pr-enforce-format.yml) so the format gate is green
# before you ever commit. No-ops cheaply when nothing C# changed.
#
# Perf note: on this ~90-project solution `dotnet format` carries real MSBuild load cost.
# If per-turn latency bites, scope it to the changed file's owning .csproj, or move this to
# a manual step.
$ErrorActionPreference = 'SilentlyContinue'

# C# files changed vs HEAD (staged + unstaged) plus new untracked ones.
$changed = @(git diff HEAD --name-only --diff-filter=ACMR -- '*.cs' 2>$null) +
           @(git ls-files --others --exclude-standard -- '*.cs' 2>$null)
$changed = $changed | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
if (-not $changed) { exit 0 }

dotnet format whitespace --no-restore --include $changed *> $null
dotnet format style      --no-restore --include $changed *> $null
exit 0

# CI runbook — known-benign transients

Operational notes for failures that look alarming in CI output but are not actionable. Add new entries as they're characterised.

## MSB4018 — `Platform.ReliableMessaging.Outbox.Core.deps.json` file-lock

**Symptom (Windows runners only, exit code 0):**

```
warning MSB4018: The "GenerateDepsFile" task failed unexpectedly.
System.IO.IOException: The process cannot access the file
'…\Platform.ReliableMessaging.Outbox.Core.deps.json' because it is being
used by another process.
```

**Cause:** `dotnet build -m` parallel branches race on the deps.json read/write. One branch reads the file while another rewrites it.

**Status:** benign. Exit code is 0; the build output is correct.

**Action:** **do not retry**. The warning is cosmetic. If it ever appears on Linux runners or starts breaking exit codes, serialise the restore/build phases in CI (drop `-m` or run `dotnet restore` and `dotnet build --no-restore` as separate sequential steps) instead of retrying.

**Origin:** `docs/implementation-prompts/session-summaries/basket-closeout.md:205-213` — first characterised during the Basket Wave 1 closeout. Tracked under issue #122 (closed).

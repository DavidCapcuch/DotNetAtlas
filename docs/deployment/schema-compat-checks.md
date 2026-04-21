# Schema compatibility checks in production pipelines

**Status:** documentation (reference pattern, not implemented in this repo)

The reference solution intentionally does **not** include a CI gate that spins up Kafka + Schema Registry in order to verify proposed Avro schemas against a freshly-built `main` baseline. This page explains the production-grade pattern we would expect instead, so readers of the reference solution understand why the CI is lean.

## Why not CI-gate in-repo

An earlier iteration of this repo ran `confluentinc/cp-kafka:7.5.0` + `confluentinc/cp-schema-registry:7.5.0` as GitHub Actions `services:` on every PR touching `platform/Platform.SchemaRegistry.Contracts/Avro/**/*.avsc`. It:

- Checked out both `main` (baseline) and `HEAD` (proposed)
- Registered each baseline `.avsc` against a fresh SR with per-subject compatibility modes (`FULL_TRANSITIVE` for records whose name ends `Command`, `FORWARD_TRANSITIVE` otherwise — see [ADR-0007](../adr/0007-avro-compatibility-modes.md))
- Posted proposed `.avsc` files to `POST /subjects/<subject>/versions` and treated `409` as an incompatibility

That approach is accurate but **simulates**, rather than *uses*, the real source of truth. A schema is "already deployed" only when it exists in an actual Schema Registry in an actual environment — not when it exists on `main`. Baseline drift between `main` and the real DEV registry (manually-evolved schema, emergency hotfix, vendor-added field) would render the gate either falsely-red or falsely-green.

## The production pattern

In a deployed system, each environment runs its own Schema Registry cluster (DEV, STAGE, PRD). Each subject has a pinned compatibility mode set by ADR-0007:

| Record name ends with | Compatibility mode |
| - | - |
| `Command` | `FULL_TRANSITIVE` (bidirectional producer/consumer) |
| anything else (events, state snapshots) | `FORWARD_TRANSITIVE` (producers ahead) |

The CD pipeline verifies compatibility by POSTing the proposed schema to each environment's **existing** registry using the `/compatibility/subjects/{subject}/versions/{version}` endpoint. Failure blocks promotion to that environment.

### Sketch (bash, runs inside CD stage)

```bash
set -euo pipefail

SR="$1"          # e.g. https://schema-registry.dev.example.com
AVRO_ROOT="platform/Platform.SchemaRegistry.Contracts/Avro"

while IFS= read -r -d '' file; do
  subject=$(jq -r '.namespace + "." + .name' < "$file")
  body=$(jq -Rs '{schema: ., schemaType: "AVRO"}' < "$file")

  code=$(curl -sS -o /tmp/resp -w '%{http_code}' \
    -H "Content-Type: application/vnd.schemaregistry.v1+json" \
    -X POST \
    --data "$body" \
    "$SR/compatibility/subjects/$subject/versions/latest")

  case "$code" in
    200) is_compat=$(jq -r '.is_compatible' /tmp/resp)
         [ "$is_compat" = "true" ] || { echo "::error::$subject incompatible in $SR"; exit 1; } ;;
    404) echo "::notice::$subject is new in $SR (first registration in CD is fine)" ;;
    *)   echo "::error::SR returned HTTP $code for $subject: $(cat /tmp/resp)"; exit 1 ;;
  esac
done < <(find "$AVRO_ROOT" -name '*.avsc' -type f -print0)
```

The same shell runs three times in the CD pipeline, once per environment.

### What the gate gives you

- **Accurate baseline**: the deployed registry *is* the source of truth. No "is `main` drifted from DEV?" class of bug.
- **No Kafka / SR bootstrap cost**: a `curl` per subject per env, seconds total.
- **Environment isolation**: PRD promotion can be blocked while STAGE passes, which reveals operational drift (someone registered a schema by hand against PRD but not STAGE).

### What still belongs in this repo

- The `.avsc` files themselves as the *proposed* contract — the CD gate reads them from the git ref it's deploying.
- Human + code review during PR (the ADR-0007 naming convention is easy to eyeball: does the record name end `Command`? does the new field have a default?).
- Unit tests that build an Avro record from the schema and round-trip it through `Platform.Avro.UniversalSerDes`, catching parse-level mistakes locally without any registry.

## References

- [ADR-0007 — Avro Schema Compatibility Modes](../adr/0007-avro-compatibility-modes.md)
- Confluent Schema Registry REST API — `POST /compatibility/subjects/{subject}/versions/{version}` — [docs.confluent.io/platform/current/schema-registry/develop/api.html](https://docs.confluent.io/platform/current/schema-registry/develop/api.html)

# Avro contracts

Event-driven messaging contracts are Avro schemas in `platform/Platform.SchemaRegistry.Contracts`.
The C# bindings (`.cs` files next to each `.avsc`) are generated — **never hand-edit them.**

## Regenerating

```powershell
platform/Platform.SchemaRegistry.Contracts/generate-avro.ps1 <path-to-schema.avsc>
```

Run it after every `.avsc` edit, and **commit the `.avsc` and the regenerated `.cs` together**.

- The script wraps `dotnet avrogen` (`Apache.Avro.Tools`) and restores the pinned manifest
  (`.config/dotnet-tools.json`) first, so every machine generates with the same version — no global
  install required.
- **It *moves* the `.avsc` you pass in**, landing it beside the generated `.cs` under `Avro/`. Pass a
  schema from outside that directory and it will not be where you left it.
- Regeneration rewrites every nested type, so unrelated siblings come back with EOL-only churn.
  Restore those (`git checkout --`) and commit only the `.avsc` plus the records that actually
  changed.

Schema naming, namespaces and evolution rules: `docs/bc-design/conventions.md` § 1.2 and § 3.

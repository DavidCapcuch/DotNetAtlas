-- Wave-0 DB bootstrap (#213).
-- Runs only on first initialization of the postgres data volume (postgres image executes
-- every *.sql in /docker-entrypoint-initdb.d alphabetically). Mounted after
-- src/keycloak/init-db.sql (filename prefix `15-` vs `10-` in docker-compose.yaml volumes).
--
-- Without these CREATE DATABASEs the outbox-relay-* containers fail silently on first connect
-- — Npgsql raises a "database does not exist" error, the worker logs it once and the container
-- enters its restart loop without applying migrations. Catalog M7 closeout noted this had been
-- broken since the compose stack landed; this script is the minimal fix.
--
-- EF Core schema migrations remain a manual `dotnet ef database update` step per BC (or
-- `dotnet ef migrations script | psql ...` against the targeted DB) — automating that is
-- tracked separately. This script's job is strictly the CREATE DATABASE so the relay can
-- connect, which is the gating step that was silently failing.
--
-- Idempotent via the same `\gexec` pattern used by 10-keycloak.sql — re-running on a stale
-- volume is a no-op rather than a duplicate-database error.

SELECT 'CREATE DATABASE "Catalog"'      WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Catalog')\gexec
SELECT 'CREATE DATABASE "Basket"'       WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Basket')\gexec
SELECT 'CREATE DATABASE "Inventory"'    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Inventory')\gexec
SELECT 'CREATE DATABASE "Ordering"'     WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Ordering')\gexec
SELECT 'CREATE DATABASE "Invoicing"'    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Invoicing')\gexec
SELECT 'CREATE DATABASE "Payments"'     WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Payments')\gexec
SELECT 'CREATE DATABASE "Notifications"' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Notifications')\gexec
SELECT 'CREATE DATABASE "Weather"'      WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'Weather')\gexec

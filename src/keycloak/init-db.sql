-- Runs only on first initialization of the postgres data volume.
-- The official postgres image executes every *.sql in /docker-entrypoint-initdb.d alphabetically.
SELECT 'CREATE DATABASE keycloak'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'keycloak')\gexec

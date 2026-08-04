-- Migracja 027: tabela śledząca, które pliki z db/migrations/ zostały już zastosowane.
-- Od tej wersji PdmSystem.Api sam sprawdza tę tabelę przy KAŻDYM starcie i automatycznie
-- stosuje nowe migracje (dołączone do programu jako embedded resources — zob.
-- MigrationRunner.cs) — więc aktualizacja już zainstalowanego programu (Docker/Linux/
-- Windows) sprowadza się do podmiany plików i restartu, bez ręcznego odpalania psql.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/027_schema_migrations_tracking.sql

BEGIN;

CREATE TABLE IF NOT EXISTS schema_migrations (
    filename   TEXT PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

GRANT SELECT, INSERT ON schema_migrations TO pdm_user;

COMMIT;

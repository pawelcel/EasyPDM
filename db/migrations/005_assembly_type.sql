-- Migracja 005: nowy typ elementu — Złożenie (assembly).
-- Złożenie to kontener bez własnego pliku (jak Folder), ale traktowany jak
-- pozycja BOM — dostaje automatycznie kolejny item_number tak samo jak Część,
-- i może mieć te same opcjonalne właściwości (materiał/masa/rodzaj).
-- Wymaga uprawnień właściciela tabeli items — uruchom jako superuser/postgres:
-- Uruchom: psql -h localhost -U postgres -d pdm -f db/migrations/005_assembly_type.sql

BEGIN;

ALTER TABLE items DROP CONSTRAINT items_item_type_check;
ALTER TABLE items ADD CONSTRAINT items_item_type_check
    CHECK (item_type IN ('folder', 'part', 'file', 'assembly'));

COMMIT;

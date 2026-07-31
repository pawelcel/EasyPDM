-- Migracja 004: możliwość "usunięcia ze struktury" elementu, który nie ma rodzica.
-- Element bez rodzica nie ma żadnej relacji do odpięcia (item_relations tu nic nie da),
-- więc dla elementów najwyższego poziomu "usuń ze struktury" oznacza: zostań w projekcie
-- (rekord i tak nadal istnieje), ale przestań być widoczny jako korzeń w drzewku, dopóki
-- ktoś znów Cię tam nie podepnie (przez "Istniejący element").
-- Wymaga uprawnień właściciela tabeli items — uruchom jako superuser/postgres:
-- Uruchom: psql -h localhost -U postgres -d pdm -f db/migrations/004_show_in_tree.sql

BEGIN;

ALTER TABLE items ADD COLUMN IF NOT EXISTS show_in_tree BOOLEAN NOT NULL DEFAULT true;

COMMIT;

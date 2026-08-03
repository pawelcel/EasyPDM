-- Migracja 020: Właściciel Części/Złożenia + blokada edycji.
-- Właścicielem elementu jest na początku osoba, która go utworzyła (ustawiane w kodzie
-- aplikacji przy tworzeniu/duplikowaniu — nie tutaj). Dopóki element jest zablokowany
-- (owner_locked = true), tylko owner_id może go edytować — NAWET administrator nie omija
-- tej blokady. Każdy może zablokować zwolniony element (stając się przy tym jego nowym
-- właścicielem) i tylko aktualny właściciel może go z powrotem zwolnić.
-- Istniejące (sprzed tej migracji) elementy nie mają znanego twórcy, więc startują
-- zwolnione i bez właściciela — pierwsza osoba, która je zablokuje, staje się właścicielem.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/020_item_owner.sql

BEGIN;

ALTER TABLE items ADD COLUMN owner_id UUID REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE items ADD COLUMN owner_locked BOOLEAN NOT NULL DEFAULT false;

COMMIT;

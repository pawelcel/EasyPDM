-- Migracja 036: unikalność L.p. (position) w obrębie jednego rodzica w item_relations.
--
-- PATCH /api/items/{parentId}/children/{childId}/position robił "sprawdź wolny numer,
-- potem UPDATE" jako dwa osobne zapytania bez żadnego ograniczenia w bazie -- dwie
-- niemal jednoczesne zmiany mogły obie przejść sprawdzenie i nadać dwóm różnym
-- podelementom ten sam numer L.p. Ograniczenie DEFERRABLE INITIALLY DEFERRED (sprawdzane
-- dopiero przy COMMIT, nie po każdym UPDATE z osobna) jest konieczne, żeby nie zepsuć
-- .../children/reorder, który w JEDNEJ transakcji przenumerowuje całe zestawienie 1..N —
-- bez odroczenia tymczasowe, przejściowe kolizje numerów w trakcie tej pętli (zanim
-- wszystkie wiersze dostaną docelowe, już unikalne numery) przerywałyby transakcję.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/036_item_relations_position_unique.sql

BEGIN;

-- Zanim dodamy ograniczenie unikalności: gdyby wcześniejszy wyścig (naprawiany tą migracją)
-- już zdążył gdzieś nadać dwóm podelementom ten sam numer, trzeba to najpierw naprawić --
-- inaczej ADD CONSTRAINT niżej zawiodłoby na już istniejących danych. Przenumerowuje
-- WSZYSTKIE wiersze na 1..N w ramach każdego rodzica (ta sama kolejność co obecne
-- position, ze stabilnym rozstrzygnięciem remisów po child_id) -- dla już poprawnych baz
-- to no-op (końcowe numery wychodzą identyczne), dla ewentualnie skolidowanych -- naprawia je.
WITH renumbered AS (
    SELECT parent_id, child_id,
           ROW_NUMBER() OVER (PARTITION BY parent_id ORDER BY position, child_id) AS rn
    FROM item_relations
)
UPDATE item_relations ir
SET position = renumbered.rn
FROM renumbered
WHERE ir.parent_id = renumbered.parent_id AND ir.child_id = renumbered.child_id;

ALTER TABLE item_relations
    ADD CONSTRAINT item_relations_parent_position_unique
    UNIQUE (parent_id, position) DEFERRABLE INITIALLY DEFERRED;

COMMIT;

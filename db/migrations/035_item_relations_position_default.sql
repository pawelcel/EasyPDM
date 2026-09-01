-- Migracja 035: brakujący DEFAULT na item_relations.position.
--
-- 010_item_relations_position.sql dodał kolumnę i ustawił ją NOT NULL, ale nigdy nie
-- dodał DEFAULT — db/schema.sql (stan dla świeżej instalacji) ma "DEFAULT 1", więc baza
-- zakładana od zera i baza aktualizowana przez kolejne migracje kończyły się w RÓŻNYM
-- stanie: świeża instalacja ma domyślną wartość, a zaktualizowana - nie. Ten sam wzorzec
-- co 016_root_position.sql (items.root_position), który taki DEFAULT poprawnie dodał.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/035_item_relations_position_default.sql

BEGIN;

ALTER TABLE item_relations ALTER COLUMN position SET DEFAULT 1;

COMMIT;

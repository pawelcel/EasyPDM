-- Pozwala elementom (Częściom/Złożeniom/plikom) istnieć bez żadnego projektu --
-- odpięcie od struktury projektu (ręczne "Usuń ze struktury" w aplikacji webowej,
-- albo automatyczna synchronizacja BOM w makrach CAD) przenosi element w stan
-- "bez projektu" zamiast wrzucać go do korzenia bieżącego projektu. Taki element
-- pozostaje w pełni widoczny i znajdywalny przez globalne wyszukiwanie ("Cała baza"),
-- tylko nie należy do żadnego konkretnego projektu.
-- Uruchom: psql -U pdm_user -d pdm -f db/migrations/032_nullable_item_project.sql

BEGIN;

ALTER TABLE items ALTER COLUMN project_id DROP NOT NULL;

COMMIT;

-- Migracja 042: nowy status "Anulowana" (cancelled) -- część/złożenie WYDANE, które
-- okazało się zbędne. Wybieralny tylko ze statusu "wydany" (patrz ItemEndpoints.cs,
-- tabela przejść), z możliwością cofnięcia do "w_pracy" (tak jak wydany -> w_pracy, też
-- podbija revision_number).
-- Uruchom: psql -U pdm_user -d pdm -f 042_status_anulowana.sql

BEGIN;

ALTER TABLE items DROP CONSTRAINT items_status_check;
ALTER TABLE items ADD CONSTRAINT items_status_check
    CHECK (status IN ('w_pracy', 'sprawdzany', 'wydany', 'anulowana'));

COMMIT;

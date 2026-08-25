-- Migracja 030: nowa rola załącznika "cad" — odróżnia plik CAD wgrany przez makro
-- (SolidWorks/FreeCAD, oryginalny SLDPRT/SLDASM/FCStd) od zwykłych, ręcznie dodanych
-- załączników (np. PDF-y niebędące rysunkiem, dowolne dokumenty). Bez tego oba typy
-- lądowały nierozróżnialnie w tej samej, ogólnej liście "Załączniki" — brak sposobu, żeby
-- w interfejsie webowym pokazać osobno "to jest model CAD" od "to jest coś innego".
-- W odróżnieniu od "pdf"/"step" (jeden załącznik na rolę, nowy zastępuje poprzedni),
-- "cad" może mieć WIELE załączników na element -- każda rewizja wysłana makrem dostaje
-- unikalną nazwę pliku (numer (nazwa).REWIZJA.rozszerzenie), więc kolejne przesłania nie
-- nadpisują poprzednich (historia rewizji zostaje widoczna jako osobne pliki).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/030_attachment_cad_role.sql

BEGIN;

ALTER TABLE item_attachments
    DROP CONSTRAINT IF EXISTS item_attachments_preview_role_check;

ALTER TABLE item_attachments
    ADD CONSTRAINT item_attachments_preview_role_check
    CHECK (preview_role IN ('pdf', 'step', 'cad'));

COMMIT;

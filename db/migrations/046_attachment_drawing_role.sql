-- Migracja 046: nowa rola załącznika "drawing" — plik rysunku technicznego SolidWorks
-- (.SLDDRW), wgrywany przez makro OBOK własnego pliku CAD (rola "cad") Części/Złożenia,
-- którego dotyczy. Tak jak "cad" (w odróżnieniu od jednosloto­wych "pdf"/"step"), "drawing"
-- może mieć WIELE załączników na element -- jeden na rewizję, ta sama konwencja unikalnej
-- nazwy pliku co przy "cad", więc kolejne przesłania nie nadpisują poprzednich.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/046_attachment_drawing_role.sql

BEGIN;

ALTER TABLE item_attachments
    DROP CONSTRAINT IF EXISTS item_attachments_preview_role_check;

ALTER TABLE item_attachments
    ADD CONSTRAINT item_attachments_preview_role_check
    CHECK (preview_role IN ('pdf', 'step', 'cad', 'drawing'));

COMMIT;

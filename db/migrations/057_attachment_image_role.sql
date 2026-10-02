-- Migracja 057: nowa rola załącznika "image" — zrzut modelu zrobiony przez makro CAD w
-- chwili wysyłki, PNG. Zastępuje renderowanie STEP-a w przeglądarce: podgląd był i tak
-- nieruchomy (jedno renderer.render(), bez obracania), a kosztował pobranie bryły,
-- teselację przez OpenCascade w WebAssembly i liczenie krawędzi dla każdej bryły -- przy
-- KAŻDYM otwarciu elementu, u każdego użytkownika. Obrazek robi to raz, na maszynie, która
-- i tak ma model otwarty.
--
-- Jednoslotowa jak "pdf"/"step" (nie jak wielokrotne "cad"/"drawing"): zrzut przedstawia
-- bieżącą postać modelu, więc nowy zastępuje poprzedni -- zob. ReplaceExistingRoleAttachmentAsync.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/057_attachment_image_role.sql

BEGIN;

ALTER TABLE item_attachments
    DROP CONSTRAINT IF EXISTS item_attachments_preview_role_check;

ALTER TABLE item_attachments
    ADD CONSTRAINT item_attachments_preview_role_check
    CHECK (preview_role IN ('pdf', 'step', 'cad', 'drawing', 'image'));

COMMIT;

BEGIN;

-- Czy nazwa elementu wchodzi w skład jego "nazwy rekordu".
--
-- Dotąd zawsze wchodziła: element numer 5 o nazwie "płyta" to "C0005(płyta)", i taką nazwę
-- makro CAD nadaje plikowi na dysku. Po wyłączeniu tej opcji nazwa rekordu to samo
-- "C0005", a plik nazywa się "C0005.A.sldprt" -- przydatne tam, gdzie nazwa elementu bywa
-- długa albo zmienna, a identyfikuje go wyłącznie numer.
--
-- Zamrażane NA ELEMENCIE przy jego tworzeniu, tak samo i z tego samego powodu co
-- item_number_prefix i item_number_digits: plik na dysku nosi tę nazwę i nikt jej wstecz
-- nie przepisze. NULL = nazwa wchodzi (dotychczasowe zachowanie wszystkich istniejących
-- elementów).
ALTER TABLE items
    ADD COLUMN IF NOT EXISTS item_number_with_name BOOLEAN;

COMMENT ON COLUMN items.item_number_with_name IS
    'Czy nazwa elementu wchodzi w skład nazwy rekordu, zamrożone przy tworzeniu wg system_state.item_number_with_name. NULL = wchodzi.';

-- Ustawienie globalne: co dostaną elementy tworzone od teraz. Domyślnie true, czyli bez
-- zmiany zachowania po aktualizacji.
ALTER TABLE system_state
    ADD COLUMN IF NOT EXISTS item_number_with_name BOOLEAN NOT NULL DEFAULT true;

INSERT INTO system_state (id) VALUES (true) ON CONFLICT (id) DO NOTHING;

COMMIT;

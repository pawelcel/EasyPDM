BEGIN;

-- Dopełnienie zerami ZAMRAŻANE NA ELEMENCIE, dokładnie tak jak item_number_prefix.
--
-- Pierwotnie (migracja 054) było to wyłącznie format wyświetlania, liczony przy renderowaniu
-- z globalnego system_state.item_number_digits -- czyli działał WSTECZ. To okazało się złe:
-- makro CAD zapisuje plik na dysku pod nazwą "NUMER (nazwa).REWIZJA.ext", więc element
-- zapisany jako "5 (Wspornik).A" zaczynał po włączeniu dopełniania pokazywać się jako "0005",
-- podczas gdy plik na dysku -- i załącznik już wgrany do bazy -- dalej nazywał się "5".
-- Nazwa pliku i numer elementu muszą być TYM SAMYM, inaczej cały mechanizm traci sens.
--
-- NULL = bez dopełniania, czyli dotychczasowy wygląd wszystkich elementów utworzonych
-- wcześniej. Nie przeliczamy ich wstecz z tego samego powodu, dla którego nie przeliczamy
-- prefiksu: ich pliki już istnieją.
ALTER TABLE items
    ADD COLUMN IF NOT EXISTS item_number_digits INTEGER;

COMMENT ON COLUMN items.item_number_digits IS
    'Minimalna liczba cyfr numeru, zamrożona przy tworzeniu elementu wg system_state.item_number_digits. NULL = bez dopełniania.';

COMMIT;

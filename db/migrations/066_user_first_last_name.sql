BEGIN;

-- Imię i nazwisko użytkownika -- osobno od wyświetlanej nazwy (display_name), która bywa
-- dowolna ("Administrator", "Kowalski - konstrukcja"). Będą trafiać do tabelki rysunkowej
-- (opracował/sprawdził), gdzie potrzebne jest prawdziwe imię i nazwisko. Nowe konto podaje je
-- obowiązkowo (pilnuje API); istniejące konta dostają je przy edycji w Ustawienia ->
-- Użytkownicy, stąd kolumny dopuszczają NULL.
ALTER TABLE users ADD COLUMN IF NOT EXISTS first_name TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS last_name TEXT;

COMMIT;

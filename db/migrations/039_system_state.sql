-- Migracja 039: trwały znacznik stanu serwera, którego celowo NIE dotyka "Wyczyść bazę"
-- (Ustawienia -> Magazyn plików -> Danger zone -- ta operacja czyści tylko projects/
-- materials/manufacturers/clients). Na razie jedno pole: czy przykładowy projekt startowy
-- (EnsureSampleProjectAsync w Program.cs) już był rozstrzygnięty -- bez tego, po
-- "Wyczyść bazę -> Projekty" na świeżo zainstalowanej instancji, kolejny restart procesu
-- widziałby pustą tabelę projects i ponownie zasiewał przykładowy projekt, mimo że admin
-- świadomie wyczyścił bazę przed prawdziwą pracą.
-- Uruchom: psql -U pdm_user -d pdm -f 039_system_state.sql

BEGIN;

CREATE TABLE system_state (
    id                     BOOLEAN PRIMARY KEY DEFAULT true CHECK (id),
    sample_project_seeded  BOOLEAN NOT NULL DEFAULT false
);
GRANT SELECT, INSERT, UPDATE ON system_state TO pdm_user;

COMMIT;

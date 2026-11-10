BEGIN;

-- Ta sama nazwa obróbki może wystąpić kilka razy z różnym opisem: "Hartowanie" do 50 HRC i
-- "Hartowanie" indukcyjne to ten sam proces, ale inna specyfikacja. Migracja 061 wymagała
-- unikalnej nazwy; element rozróżnia teraz obróbki po identyfikatorze (properties.processes
-- trzyma {id, name, description}), więc nazwa nie musi już być kluczem.
DROP INDEX IF EXISTS processes_name_lower_idx;

COMMIT;

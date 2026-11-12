BEGIN;

-- Obróbki wybrane w elementach przed wprowadzeniem języków dokumentacji (0.8) mają w
-- properties.processes tylko nazwę i opis, bez kopii tłumaczeń (texts), więc w projekcie z
-- innym głównym językiem pokazują się dalej w jednym. Tutaj dostają tłumaczenia z katalogu —
-- ale TYLKO elementy w statusie "w_pracy". Wydane (i sprawdzane, anulowane) zostają nietknięte:
-- ich zapis to specyfikacja, z którą je wydano, a nowa rewizja i tak przejdzie przez "W pracy".
--
-- Dotyczy wpisów, które mają id obróbki istniejącej w katalogu i nie mają jeszcze texts; zwykłe
-- napisy (zapis sprzed wariantów) i obróbki usunięte z katalogu zostają jak były. Kolejność listy
-- jest zachowana. Na świeżej bazie nie ma czego zmieniać.
UPDATE items i
SET properties = jsonb_set(i.properties, '{processes}', (
    SELECT jsonb_agg(
        CASE
            WHEN jsonb_typeof(e) = 'object'
                 AND jsonb_typeof(e -> 'id') = 'number'
                 AND NOT (e ? 'texts')
                 AND EXISTS (SELECT 1 FROM process_texts t WHERE t.process_id = (e ->> 'id')::int)
            THEN e || jsonb_build_object('texts', (
                SELECT jsonb_agg(jsonb_build_object('languageId', t.language_id, 'name', t.name, 'description', t.description)
                                 ORDER BY t.language_id)
                FROM process_texts t WHERE t.process_id = (e ->> 'id')::int))
            ELSE e
        END
        ORDER BY x.ord)
    FROM jsonb_array_elements(i.properties -> 'processes') WITH ORDINALITY AS x(e, ord)))
WHERE i.status = 'w_pracy'
  AND jsonb_typeof(i.properties -> 'processes') = 'array'
  AND jsonb_array_length(i.properties -> 'processes') > 0;

COMMIT;

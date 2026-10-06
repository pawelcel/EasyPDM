// Układa listę postępu w drzewo: złożenie, pod nim (z wcięciem) jego podzłożenia i części.
//
// Kolejność WYŚWIETLANIA jest tu niezależna od kolejności PRACY. Wysyłka idzie od liści (złożenia
// nie da się podpiąć, zanim nie istnieją jego części), a pobieranie od góry — ale odhaczanie
// działa po kluczu, nie po miejscu na liście, więc lista może stać w kolejności drzewa, a ptaszki
// i tak trafią we właściwe pozycje.
//
// Liczone RAZ, tutaj, a nie w każdym makrze z osobna — ta sama zasada co przy liczeniu postępu:
// trzy CAD-y nie mają szans pokazać tego samego drzewa na trzy różne sposoby. Makra przysyłają
// tylko pary rodzic–dziecko, które i tak trzymają w pamięci do podpinania BOM.
static class ProgressTree
{
    public static List<TransferProgressEntry> Arrange(
        List<TransferProgressEntry> entries, IEnumerable<(string Parent, string Child)> edges)
    {
        var byKey = entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

        // Dzieci w kolejności, w jakiej przyszły relacje — u makr to kolejność z drzewa CAD,
        // przy pobieraniu kolejność pozycji w BOM.
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var isChild = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (parent, child) in edges)
        {
            // Relacje do czegoś, czego nie ma na liście (albo element sam w sobie), nie mają gdzie
            // się podpiąć — pomijamy je zamiast wywracać całą listę.
            if (parent == child || !byKey.ContainsKey(parent) || !byKey.ContainsKey(child))
                continue;
            if (!children.TryGetValue(parent, out var list))
                children[parent] = list = new List<string>();
            if (!list.Contains(child))
                list.Add(child);
            isChild.Add(child);
        }

        // Bez żadnej relacji (starsze makro, pojedynczy plik) lista zostaje dokładnie taka, jak
        // przyszła — płaska, jak dotąd.
        if (children.Count == 0)
            return entries;

        var arranged = new List<TransferProgressEntry>(entries.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);

        void Place(string key, int depth)
        {
            // Część użyta w kilku złożeniach jest wysyłana raz, więc i na liście stoi raz: pod
            // PIERWSZYM złożeniem, w którym występuje (decyzja użytkownika). Kolejne wystąpienia
            // są pomijane.
            if (!placed.Add(key))
                return;
            var entry = byKey[key];
            entry.Depth = depth;
            arranged.Add(entry);
            if (children.TryGetValue(key, out var list))
                foreach (var child in list)
                    Place(child, depth + 1);
        }

        // Korzenie — to, co nie jest niczyim dzieckiem — w kolejności zgłoszenia.
        foreach (var entry in entries)
            if (!isChild.Contains(entry.Key))
                Place(entry.Key, 0);

        // Zabezpieczenie: cokolwiek nieosiągalne z korzeni (cykl w danych) i tak trafia na listę,
        // płasko na końcu. Lista bez którejś pozycji kłamałaby przy liczniku "3 z 7".
        foreach (var entry in entries)
            Place(entry.Key, 0);

        return arranged;
    }
}

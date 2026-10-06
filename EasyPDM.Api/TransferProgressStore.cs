using System.Collections.Concurrent;

// Postęp wysyłki/pobierania pokazywany w aplikacji webowej jako lista plików odhaczana w
// trakcie pracy makra CAD. Czysto w pamięci procesu, bez tabeli w bazie -- tak samo jak
// CreateTicketStore i z tego samego powodu: to stan życia rzędu sekund-minut, nikogo nie
// interesuje po fakcie i nie musi przeżyć restartu serwera (po restarcie lista po prostu
// zniknie, a makro i tak pracuje dalej -- postęp jest informacją, nie częścią operacji).
//
// Kluczem jest UŻYTKOWNIK, nie losowy identyfikator sesji. Dzięki temu przeglądarka pyta po
// prostu "co teraz robi moje makro?" (GET /api/progress) i nie musi skądkolwiek poznać
// identyfikatora -- działa też wtedy, gdy kartę otwarto wcześniej, niezależnie od biletu.
// Jeden bieg makra na użytkownika w danej chwili jest założeniem bezpiecznym: człowiek
// klika "Upload" w jednym CAD-zie naraz, a nowy bieg po prostu zastępuje poprzedni.
//
// Celowo NIE ma tu WebSocketów ani SSE: makro już odpytuje serwer co 2 s (WaitForTicket),
// a aplikacja webowa odpytuje o powiadomienia -- ten sam wzorzec wystarcza i nie dokłada
// nowego rodzaju połączenia do utrzymania.
class TransferProgressStore
{
    // Dłużej niż bilet (30 min), bo wysyłka dużego złożenia z eksportem STEP potrafi trwać.
    // Sprzątanie i tak następuje głównie przez Finish() wołane przez makro na koniec.
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

    // Jak długo PO zakończeniu biegu lista jest jeszcze wydawana. Krótko, bo jej zadaniem
    // jest pokazać komplet ptaszków temu, kto właśnie patrzył -- a nie witać listą sprzed
    // godziny każdego, kto później otworzy aplikację. Bez tego zakończony bieg wisiał do
    // MaxAge i pojawiał się w świeżo otwartej karcie (wyłapane testem na żywo).
    private static readonly TimeSpan FinishedLinger = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, TransferProgress> _byUser = new();

    // Makro zgłasza CAŁĄ listę zanim cokolwiek wyśle -- przy wysyłce wie ją z góry
    // (DiscoverComponentTree przechodzi drzewo przed pierwszym uploadem), przy pobieraniu
    // bierze ją z GET /items/{id}/descendants. Bez pełnej listy od początku pasek postępu
    // kłamałby: "3 z 3" zamieniałoby się w "3 z 9", gdy znajdzie się kolejny poziom.
    public void Start(Guid userId, string kind, IReadOnlyList<TransferProgressEntry> entries)
    {
        Sweep();
        _byUser[userId] = new TransferProgress(DateTime.UtcNow, DateTime.UtcNow, kind, entries.ToList(), false, false);
    }

    // Zwraca false, gdy nie ma czego aktualizować (np. serwer zrestartował się w trakcie) --
    // makro ma to zignorować, a nie przerywać wysyłki.
    public bool Mark(Guid userId, string key, string status)
    {
        if (!_byUser.TryGetValue(userId, out var progress))
            return false;

        var entry = progress.Entries.FirstOrDefault(e => e.Key == key);
        if (entry is null)
            return false;

        entry.Status = status;
        _byUser[userId] = progress with { UpdatedAt = DateTime.UtcNow };
        return true;
    }

    // Bieg skończony. NIE kasujemy od razu: lista z kompletem ptaszków jest tym, co
    // użytkownik ma zobaczyć na koniec. Znika sama po FinishedLinger od ostatniej zmiany --
    // tym zajmuje się Sweep przy kolejnym dostępie, a frontend po prostu przestaje ją
    // pokazywać, gdy serwer odda "finished" i minie jego własny czas wyświetlania.
    // Zwraca bieg, KTÓRY WŁAŚNIE SIĘ SKOŃCZYŁ -- i tylko przy pierwszym wywołaniu, bo z tego
    // powstaje raport w powiadomieniach (zob. TransferProgressEndpoints). Powtórne "finish"
    // tego samego biegu oddaje null, żeby makro, które zawoła je dwa razy (ścieżka błędu plus
    // normalne zakończenie), nie zostawiło dwóch takich samych raportów.
    public TransferProgress? Finish(Guid userId)
    {
        if (!_byUser.TryGetValue(userId, out var progress) || progress.Finished)
            return null;

        var finished = progress with { UpdatedAt = DateTime.UtcNow, Finished = true };
        _byUser[userId] = finished;
        return finished;
    }

    // Użytkownik w przeglądarce prosi o przerwanie biegu. Serwer NIE przerywa niczego sam --
    // nie ma jak, makro działa w CAD-zie na innej maszynie. Tylko zapisuje prośbę, a makro
    // sprawdza ją przy każdym odpytaniu o formularz i przed każdym kolejnym plikiem
    // (GET /api/progress/cancelled) i zatrzymuje się w pierwszym takim miejscu. Plik, który
    // akurat leci, zostaje dokończony: przerwanie w połowie zapisu zostawiłoby go uszkodzonego.
    //
    // Zwraca false, gdy nie ma czego anulować -- biegu nie ma albo już się skończył.
    public bool Cancel(Guid userId)
    {
        if (!_byUser.TryGetValue(userId, out var progress) || progress.Finished)
            return false;

        _byUser[userId] = progress with { UpdatedAt = DateTime.UtcNow, Cancelled = true };
        return true;
    }

    public bool IsCancelled(Guid userId) =>
        _byUser.TryGetValue(userId, out var progress) && progress.Cancelled && !progress.Finished;

    public void Clear(Guid userId) => _byUser.TryRemove(userId, out _);

    public TransferProgress? Get(Guid userId)
    {
        Sweep();
        if (!_byUser.TryGetValue(userId, out var progress))
            return null;

        if (progress.Finished && DateTime.UtcNow - progress.UpdatedAt > FinishedLinger)
        {
            _byUser.TryRemove(userId, out _);
            return null;
        }

        return progress;
    }

    // Sweep-on-access zamiast osobnej usługi w tle -- ten sam wybór i ten sam powód co w
    // CreateTicketStore: wolumen to jeden wpis na jedno kliknięcie "Upload".
    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - MaxAge;
        foreach (var (key, value) in _byUser)
        {
            if (value.UpdatedAt < cutoff)
                _byUser.TryRemove(key, out _);
        }
    }
}

// Status pozycji: "pending" | "active" | "done" | "failed" | "skipped".
// "skipped" ma realne zastosowanie przy wysyłce: komponent już podpięty do elementu PDM nie
// jest wysyłany ponownie, ale ma się pokazać na liście, żeby liczby się zgadzały z tym, co
// użytkownik widzi w drzewie złożenia.
class TransferProgressEntry
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Status { get; set; } = "pending";
    // Poziom zagłębienia w drzewie złożenia (0 = najwyższy) — tylko do wcięcia w panelu.
    // Ustawiany przez ProgressTree; bez relacji wszystko zostaje na 0, czyli płasko.
    public int Depth { get; set; }
}

record TransferProgress(
    DateTime StartedAt,
    DateTime UpdatedAt,
    string Kind,
    List<TransferProgressEntry> Entries,
    bool Finished,
    // Użytkownik poprosił w przeglądarce o przerwanie -- zob. Cancel. Zostaje true także po
    // zakończeniu, bo z tego raport w powiadomieniach wie, że bieg przerwano, a nie że po
    // prostu nie doszedł do końca.
    bool Cancelled);

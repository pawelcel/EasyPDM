using System.Collections.Concurrent;

// "Makro prosi o utworzenie elementu" — podawane JUŻ OTWARTEJ karcie przeglądarki, zamiast
// otwierania nowej na każdy komponent.
//
// Po co to w ogóle istnieje: wysyłając złożenie, makro potrzebuje formularza dla KAŻDEGO
// nowego komponentu. Dotąd otwierało na to osobną kartę, a przed każdą musiało pokazać
// natywne okno "OK" — nie dla potwierdzenia, tylko dlatego, że ochrona Windows przed
// kradzieżą fokusu przepuszcza PIERWSZE programowe otwarcie przeglądarki w danym biegu, a
// każde kolejne otwiera po cichu w tle. Bez tego kliknięcia karta z formularzem powstawała
// niewidoczna, a makro czekało w nieskończoność na dane, których nie dało się wpisać.
// Przy złożeniu na kilkadziesiąt części oznaczało to kilkadziesiąt kliknięć "OK" i
// kilkadziesiąt kart.
//
// Skoro aplikacja webowa i tak odpytuje serwer (postęp wysyłki, powiadomienia), nie musi
// powstawać żadna nowa karta: makro zostawia prośbę tutaj, a karta, która już jest otwarta
// i ma fokus, sama ją podejmuje i pokazuje ten sam formularz co dotąd.
//
// Ten sam wybór co w CreateTicketStore i TransferProgressStore: w pamięci procesu, bez
// tabeli, kluczowane UŻYTKOWNIKIEM, bo makro i przeglądarka są zalogowane tym samym kontem
// (most bilet->ciasteczko). Stan żyje minuty i nie musi przeżyć restartu — po restarcie
// makro po prostu wróci do otwierania karty, czyli do zachowania sprzed tej zmiany.
class CadRequestStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, CadRequest> _byUser = new();

    public void Publish(Guid userId, CadRequest request)
    {
        Sweep();
        _byUser[userId] = request;
    }

    public CadRequest? Get(Guid userId)
    {
        Sweep();
        return _byUser.TryGetValue(userId, out var request) ? request : null;
    }

    // Przeglądarka zgłasza "biorę to na siebie". To JEDYNY sygnał, po którym makro poznaje,
    // że ktoś naprawdę patrzy -- bez niego nie da się odróżnić otwartej karty od zamkniętej
    // przeglądarki, a różnica jest zasadnicza: w drugim przypadku makro MUSI wrócić do
    // otwierania karty, inaczej czekałoby na formularz, którego nikt nigdy nie zobaczy.
    public bool MarkTaken(Guid userId, string ticket)
    {
        if (!_byUser.TryGetValue(userId, out var request) || request.Ticket != ticket)
            return false;

        _byUser[userId] = request with { TakenAt = DateTime.UtcNow };
        return true;
    }

    public void Clear(Guid userId, string? ticket = null)
    {
        if (ticket is null)
        {
            _byUser.TryRemove(userId, out _);
            return;
        }

        // Kasujemy TYLKO wtedy, gdy to wciąż ta sama prośba -- inaczej sprzątanie po
        // poprzednim komponencie usunęłoby prośbę o następny, zgłoszoną w międzyczasie.
        if (_byUser.TryGetValue(userId, out var request) && request.Ticket == ticket)
            _byUser.TryRemove(userId, out _);
    }

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - MaxAge;
        foreach (var (key, value) in _byUser)
        {
            if (value.CreatedAt < cutoff)
                _byUser.TryRemove(key, out _);
        }
    }
}

record CadRequest(
    DateTime CreatedAt,
    DateTime? TakenAt,
    string Ticket,
    string Mode,
    string? Name,
    string? ItemType,
    string? Material,
    long? DocumentSize,
    int? SuggestedItemNumber);

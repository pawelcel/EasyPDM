using System.Collections.Concurrent;

// Korelacja "makro otworzyło przeglądarkę, przeglądarka utworzyła element, makro chce się o
// tym dowiedzieć" — czysto w pamięci procesu (singleton), bez tabeli w bazie. To stan życia
// rzędu sekund-minut, nikt nigdy nie musi go odpytać po fakcie ani przeżyć restartu serwera
// (w razie restartu makro po prostu dostanie timeout i użytkownik spróbuje ponownie — nie
// gorzej niż dzisiejsza utrata połączenia w trakcie natywnego dialogu). Aplikacja jest
// jednoinstancyjna (Docker/systemd/Windows Service, zawsze jeden proces), więc nie ma
// problemu współdzielenia tego stanu między instancjami.
class CreateTicketStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, TicketState> _tickets = new();

    // Wołane z POST /projects/{id}/nodes (nowy element, existing=false) albo z
    // POST /create-tickets/{ticket}/attach-existing (dogranie do już istniejącego,
    // existing=true) — ogólne, nie FreeCAD-specyficzne (przyda się też dla SolidWorks
    // później). "existing" mówi makru, którą lokalną ścieżkę kodu odpalić po stronie
    // klienta (_rename_and_upload wprost dla nowego, vs push_to_existing_item — ze swoją
    // własną obsługą statusu "wydany" — dla dogrania). "exportStep" to wybór zaznaczony w
    // formularzu w przeglądarce (checkbox widoczny tylko, gdy dodawanie ma przypięty
    // bilet) — makro po stronie klienta używa go zamiast jakiegokolwiek lokalnego wyboru,
    // zob. ItemEndpoints.cs i EasyPDMUpload.FCMacro.
    public void Complete(Guid ticket, Guid itemId, int? itemNumber, string? itemNumberPrefix, string name, bool? exportStep, bool existing)
    {
        Sweep();
        _tickets[ticket] = new TicketState(DateTime.UtcNow, itemId, itemNumber, itemNumberPrefix, name, exportStep, existing);
    }

    public bool TryGet(Guid ticket, out TicketState state)
    {
        Sweep();
        return _tickets.TryGetValue(ticket, out state!);
    }

    // Sweep-on-access zamiast osobnego hosted service — wolumen jest niski (jeden bilet na
    // jedno kliknięcie "Upload" w makrze), więc nie potrzeba tła.
    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - MaxAge;
        foreach (var (key, value) in _tickets)
        {
            if (value.CreatedAt < cutoff)
                _tickets.TryRemove(key, out _);
        }
    }
}

record TicketState(DateTime CreatedAt, Guid ItemId, int? ItemNumber, string? ItemNumberPrefix, string Name, bool? ExportStep, bool Existing);

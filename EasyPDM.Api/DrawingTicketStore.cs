using System.Collections.Concurrent;

// Korelacja "makro SolidWorks nie potrafiło samo rozstrzygnąć, do którego elementu podpiąć
// wgrywany rysunek (.SLDDRW) -- kilku różnych kandydatów po drzewie widoków -- więc otworzyło
// przeglądarkę z listą do wyboru, i chce się dowiedzieć, co wybrano". Ten sam wzorzec co
// CreateTicketStore.cs (czysto w pamięci procesu, sweep-on-access, 30 min TTL), tylko
// prostszy stan -- kandydaci lecą wprost w URL-u otwieranym w przeglądarce (zob.
// BuildBrowserDrawingUrl w EasyPDMUpload.bas), więc bilet nie musi nic nieść PRZED
// rozwiązaniem, tylko wynik wyboru PO nim.
class DrawingTicketStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, DrawingTicketState> _tickets = new();

    // Wołane z POST /drawing-tickets/{ticket}/resolve po wyborze w przeglądarce.
    public void Complete(Guid ticket, Guid itemId, bool exportPdf)
    {
        Sweep();
        _tickets[ticket] = new DrawingTicketState(DateTime.UtcNow, itemId, exportPdf);
    }

    public bool TryGet(Guid ticket, out DrawingTicketState state)
    {
        Sweep();
        return _tickets.TryGetValue(ticket, out state!);
    }

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

record DrawingTicketState(DateTime CreatedAt, Guid ItemId, bool ExportPdf);

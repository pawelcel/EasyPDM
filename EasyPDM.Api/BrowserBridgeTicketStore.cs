using System.Collections.Concurrent;
using System.Security.Cryptography;

// Most "makro CAD -> przeglądarka systemowa" (GET /api/auth/browser-login) dziś przenosi
// PRAWDZIWY token sesji (ważny 30 dni) wprost w query stringu URL-a, który system otwiera w
// przeglądarce -- ten sam sekret ląduje więc w historii przeglądarki na maszynie użytkownika.
// Ten magazyn zamienia to na krótkotrwały, jednorazowy bilet: makro (mające już sesję przez
// nagłówek Cookie, zob. get_session_token()/EasyPDMUpload.FCMacro) najpierw woła
// POST /api/auth/browser-bridge-ticket, dostaje bilet ważny MaxAge, i DOPIERO ten bilet ląduje
// w URL-u otwieranym w przeglądarce. GET /browser-login zamienia bilet z powrotem na
// prawdziwy token sesji I OD RAZU go zużywa (TryConsume usuwa wpis) -- powtórne otwarcie tego
// samego URL-a (np. z historii przeglądarki) nic już nie daje. Czysto w pamięci procesu,
// jak CreateTicketStore -- ten sam wzorzec, ta sama skala czasowa (sekundy-minuty).
class BrowserBridgeTicketStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, (string SessionToken, DateTime CreatedAt)> _tickets = new();

    public string Issue(string sessionToken)
    {
        Sweep();
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tickets[ticket] = (sessionToken, DateTime.UtcNow);
        return ticket;
    }

    // Jednorazowe -- wpis znika natychmiast, niezależnie od tego, czy w ogóle był
    // jeszcze ważny, żeby URL z historii przeglądarki nie dało się odtworzyć.
    public string? TryConsume(string ticket)
    {
        Sweep();
        if (!_tickets.TryRemove(ticket, out var entry))
            return null;
        return DateTime.UtcNow - entry.CreatedAt <= MaxAge ? entry.SessionToken : null;
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

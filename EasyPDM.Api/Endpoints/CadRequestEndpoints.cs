// Prośba makra o formularz, podawana już otwartej karcie przeglądarki — zob. CadRequestStore
// po powód, dla którego to w ogóle istnieje (kilkadziesiąt okien "OK" i kilkadziesiąt kart
// przy wysyłce złożenia).
//
// Wszystko działa na "wołającym", bez identyfikatorów w URL-u: makro i przeglądarka są
// zalogowane tym samym kontem, więc trafiają w ten sam wpis.
static class CadRequestEndpoints
{
    public static void MapCadRequestEndpoints(this WebApplication app, CadRequestStore store)
    {
        // PUT /api/cad-requests — makro zostawia prośbę ZAMIAST otwierać kartę.
        app.MapPut("/api/cad-requests", (PublishCadRequest body, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            if (string.IsNullOrWhiteSpace(body.Ticket))
                return Results.BadRequest("Pole 'ticket' nie może być puste.");
            if (body.Mode is not ("create" or "download"))
                return Results.BadRequest("Pole 'mode' musi być 'create' albo 'download'.");

            store.Publish(user.Id, new CadRequest(
                DateTime.UtcNow, null, body.Ticket, body.Mode,
                body.Name, body.ItemType, body.Material, body.DocumentSize, body.SuggestedItemNumber));
            return Results.Ok();
        });

        // GET /api/cad-requests — odpytywane przez aplikację webową. Brak prośby to zwykły
        // stan, nie błąd: 200 i "null", tak samo jak przy postępie.
        app.MapGet("/api/cad-requests", (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var request = store.Get(user.Id);
            if (request is null)
                return Results.Ok(new { request = (object?)null });

            return Results.Ok(new
            {
                request = new
                {
                    ticket = request.Ticket,
                    mode = request.Mode,
                    name = request.Name,
                    itemType = request.ItemType,
                    material = request.Material,
                    documentSize = request.DocumentSize,
                    suggestedItemNumber = request.SuggestedItemNumber,
                    taken = request.TakenAt is not null,
                }
            });
        });

        // POST /api/cad-requests/take — przeglądarka mówi "biorę to na siebie". Makro czeka
        // na ten sygnał przez chwilę i dopiero gdy go nie ma, wraca do otwierania karty.
        app.MapPost("/api/cad-requests/take", (TicketRequest body, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            return Results.Ok(new { matched = store.MarkTaken(user.Id, body.Ticket ?? "") });
        });

        // GET /api/cad-requests/taken?ticket=... — odpytywane przez MAKRO, nie przeglądarkę.
        // Zwraca 1/0 jako LICZBĘ, a nie wartość logiczną w zagnieżdżonym obiekcie: parsery JSON
        // w makrach VBA mają tylko JsonGetString i JsonGetLong, więc płaska liczba jest tu
        // jedyną postacią, którą da się odczytać bez dokładania im parsera.
        app.MapGet("/api/cad-requests/taken", (HttpContext ctx, string? ticket) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var request = store.Get(user.Id);
            var taken = request is not null && request.Ticket == ticket && request.TakenAt is not null;
            return Results.Ok(new { taken = taken ? 1 : 0 });
        });

        // DELETE /api/cad-requests?ticket=... — prośba obsłużona albo porzucona.
        app.MapDelete("/api/cad-requests", (HttpContext ctx, string? ticket) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            store.Clear(user.Id, ticket);
            return Results.Ok();
        });
    }

    record PublishCadRequest(
        string Ticket, string Mode, string? Name, string? ItemType,
        string? Material, long? DocumentSize, int? SuggestedItemNumber);

    record TicketRequest(string? Ticket);
}

using Npgsql;

// Kto może zmieniać katalogi firmy (Materiały, Obróbki, Producenci, Klienci). Domyślnie każdy
// zalogowany; po włączeniu catalogs_admin_only (Ustawienia -> Użytkownicy) tylko administrator.
// Odczyt zostaje dla wszystkich -- z katalogów wybiera się przy opisywaniu elementów.
static class CatalogPolicy
{
    private static readonly string[] CatalogPaths =
        ["/api/materials", "/api/processes", "/api/manufacturers", "/api/clients"];

    // Brak wiersza w system_state (baza sprzed migracji 068) = false, czyli jak dotąd.
    public static async Task<bool> GetAdminOnlyAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT catalogs_admin_only FROM system_state WHERE id = true;", conn);
        return await cmd.ExecuteScalarAsync() is true;
    }

    // Czy to żądanie ZAPISU do któregoś z katalogów. Sprawdzane po ścieżce w jednym miejscu
    // (middleware w Program.cs), a nie w każdym z kilkudziesięciu handlerów osobno -- nowy
    // endpoint pod tymi ścieżkami jest chroniony od razu, bez pamiętania o dopisaniu warunku.
    // StartsWithSegments porównuje całe segmenty, więc "/api/client-verifications" (weryfikacja
    // klienta w projekcie, nie katalog) tu nie wpada.
    public static bool IsCatalogWrite(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
        && CatalogPaths.Any(p => request.Path.StartsWithSegments(p));
}

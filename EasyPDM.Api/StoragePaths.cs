using Npgsql;

// Ścieżki plików w bazie są bezwzględne (items.file_path itd.), więc każda operacja, po której
// magazyn plików leży gdzie indziej niż w chwili zapisu ścieżek, musi je przepisać: przeniesienie
// magazynu w Ustawieniach i przywrócenie kopii zapasowej na serwerze z innym katalogiem
// magazynu (inny komputer, Docker zamiast instalacji natywnej, Windows zamiast Linuksa).
public static class StoragePaths
{
    // Wszystkie tabele trzymające ścieżkę pliku w magazynie. Każda ma id UUID i file_path TEXT.
    // Lista jest jedna dla przenoszenia i przywracania -- wcześniej przenoszenie miało własną,
    // bez załączników projektu i weryfikacji klienta, przez co te pliki po przeniesieniu
    // magazynu przestawały się pobierać.
    public static readonly string[] FileTables =
        ["items", "item_attachments", "client_nodes", "project_attachments", "item_client_verification_attachments"];

    // Nowe położenie pliku zapisanego pod storedPath, gdy magazyn jest teraz w newRoot.
    //
    // Gdy znany jest dawny katalog magazynu (oldRoot), wystarczy podmienić początek ścieżki --
    // ale tylko na granicy katalogu, żeby "/srv/storage" nie dopasowało "/srv/storage2/...".
    // Kopie zapasowe sprzed zapisywania katalogu magazynu go nie mają; wtedy szukamy pliku w
    // nowym magazynie po coraz krótszych końcówkach ścieżki (od najdłuższej, czyli najbardziej
    // jednoznacznej). Pliki w magazynie mają w nazwach identyfikatory, więc końcówka
    // "<projekt>/<element>.ext" nie trafi w cudzy plik.
    //
    // Ścieżki mogą pochodzić z innego systemu, dlatego dzielone są po obu separatorach.
    // Zwraca null, gdy pliku nie da się znaleźć -- wtedy wiersz zostaje bez zmian.
    public static string? Relocate(string storedPath, string? oldRoot, string newRoot, Func<string, bool> fileExists)
    {
        if (oldRoot is not null)
        {
            var root = oldRoot.TrimEnd('/', '\\');
            if (storedPath.Length > root.Length
                && storedPath.StartsWith(root, StringComparison.Ordinal)
                && storedPath[root.Length] is '/' or '\\')
                return Path.Combine([newRoot, .. Segments(storedPath[root.Length..])]);
        }

        if (fileExists(storedPath) && IsUnder(storedPath, newRoot))
            return storedPath;

        var segments = Segments(storedPath);
        for (var take = segments.Length - 1; take >= 1; take--)
        {
            var candidate = Path.Combine([newRoot, .. segments[^take..]]);
            if (fileExists(candidate))
                return candidate;
        }
        return null;
    }

    // Przepisuje ścieżki we wszystkich tabelach z FileTables. Zwraca liczbę zmienionych wierszy
    // i tych, których pliku nie udało się znaleźć (zostają nietknięte).
    public static async Task<(int Rewritten, int Missing)> RelocateAllAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string? oldRoot, string newRoot, Func<string, bool> fileExists)
    {
        var rewritten = 0;
        var missing = 0;
        foreach (var table in FileTables)
        {
            // Tabela może nie istnieć w bazie odtworzonej ze starej kopii, jeśli migracje jeszcze
            // jej nie dodały -- wtedy nie ma w niej czego przepisywać.
            await using (var exists = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL;", conn, tx))
            {
                exists.Parameters.AddWithValue("table", table);
                if (await exists.ExecuteScalarAsync() is not true)
                    continue;
            }

            var rows = new List<(Guid Id, string Path)>();
            // "table" pochodzi wyłącznie z FileTables (stałe w kodzie), więc interpolacja jest bezpieczna.
            await using (var select = new NpgsqlCommand($"SELECT id, file_path FROM {table} WHERE file_path IS NOT NULL;", conn, tx))
            await using (var reader = await select.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    rows.Add((reader.GetGuid(0), reader.GetString(1)));
            }

            foreach (var (id, storedPath) in rows)
            {
                var relocated = Relocate(storedPath, oldRoot, newRoot, fileExists);
                if (relocated is null)
                {
                    missing++;
                    continue;
                }
                if (relocated == storedPath)
                    continue;

                await using var update = new NpgsqlCommand($"UPDATE {table} SET file_path = @path WHERE id = @id;", conn, tx);
                update.Parameters.AddWithValue("path", relocated);
                update.Parameters.AddWithValue("id", id);
                await update.ExecuteNonQueryAsync();
                rewritten++;
            }
        }
        return (rewritten, missing);
    }

    private static string[] Segments(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static bool IsUnder(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.Ordinal);
    }
}

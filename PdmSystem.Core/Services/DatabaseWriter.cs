using System.Text.Json;
using Npgsql;
using PdmSystem.Core.Models;

namespace PdmSystem.Core.Services;

public class DatabaseWriter
{
    private readonly string _connectionString;

    public DatabaseWriter(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Zapisuje element do bazy. Jeśli plik o tej ścieżce już istnieje — aktualizuje go
    /// tylko wtedy, gdy hash się zmienił (żeby nie nadpisywać ręcznie dodanych tagów/właściwości
    /// przy każdym uruchomieniu skanera bez potrzeby).
    /// </summary>
    public async Task UpsertItemAsync(ItemRecord record)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = """
            INSERT INTO items (file_path, file_name, file_type, file_hash, file_size, modified_at, last_scanned_at, properties)
            VALUES (@path, @name, @type, @hash, @size, @modified, now(), @props::jsonb)
            ON CONFLICT (file_path) DO UPDATE SET
                file_name = EXCLUDED.file_name,
                file_size = EXCLUDED.file_size,
                modified_at = EXCLUDED.modified_at,
                last_scanned_at = now(),
                -- właściwości scalamy zamiast nadpisywać: to co odczytane z pliku ma pierwszeństwo,
                -- ale ręcznie dodane przez użytkownika klucze (spoza pliku) zostają zachowane
                properties = items.properties || EXCLUDED.properties,
                file_hash = EXCLUDED.file_hash
            WHERE items.file_hash IS DISTINCT FROM EXCLUDED.file_hash;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("path", record.FilePath);
        cmd.Parameters.AddWithValue("name", record.FileName);
        cmd.Parameters.AddWithValue("type", record.FileType);
        cmd.Parameters.AddWithValue("hash", record.FileHash);
        cmd.Parameters.AddWithValue("size", record.FileSize);
        cmd.Parameters.AddWithValue("modified", record.ModifiedAt);
        cmd.Parameters.AddWithValue("props", JsonSerializer.Serialize(record.Properties));

        await cmd.ExecuteNonQueryAsync();
    }
}

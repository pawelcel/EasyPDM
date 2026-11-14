using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace EasyPDM.Api.Tests;

// Tabela sessions trzyma tylko skrót SHA-256 tokenu (migracja 073): odczyt tabeli nie daje
// działającej sesji, a sesje sprzed migracji działają dalej po przeliczeniu na miejscu.
[Collection("EasyPDM database")]
public class SessionTokenHashTests
{
    private static async Task<string> LoginForTokenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionToken").GetString()!;
    }

    // To samo kodowanie co migracja 073: wielkie litery hex.
    private static string Sha256Hex(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql, string value)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("v", value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Baza_trzyma_skrot_tokenu_a_nie_token()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await LoginForTokenAsync(client);

        await using var conn = new NpgsqlConnection(EasyPDMWebApplicationFactory.ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await CountAsync(conn, "SELECT count(*) FROM sessions WHERE token_hash = @v;", token));
        Assert.Equal(1, await CountAsync(conn, "SELECT count(*) FROM sessions WHERE token_hash = @v;", Sha256Hex(token)));

        // Token z ciasteczka działa, a wartość odczytana z tabeli nie jest tokenem.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        using var thief = factory.CreateClient();
        thief.DefaultRequestHeaders.Add("Cookie", "pdm_session=" + Sha256Hex(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("/api/auth/me")).StatusCode);

        // Wylogowanie usuwa wiersz po skrócie.
        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(0, await CountAsync(conn, "SELECT count(*) FROM sessions WHERE token_hash = @v;", Sha256Hex(token)));
    }

    [Fact]
    public async Task Migracja_przelicza_istniejace_sesje_bez_wylogowania()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await LoginForTokenAsync(client);

        await using var conn = new NpgsqlConnection(EasyPDMWebApplicationFactory.ConnectionString);
        await conn.OpenAsync();
        // Stan sprzed 073: kolumna "token" z gotowym tokenem.
        await using (var cmd = new NpgsqlCommand(
            "UPDATE sessions SET token_hash = @raw WHERE token_hash = @hash; ALTER TABLE sessions RENAME COLUMN token_hash TO token;", conn))
        {
            cmd.Parameters.AddWithValue("raw", token);
            cmd.Parameters.AddWithValue("hash", Sha256Hex(token));
            await cmd.ExecuteNonQueryAsync();
        }

        var migration = await File.ReadAllTextAsync(FindRepoFile("db", "migrations", "073_session_token_hash.sql"));
        await using (var cmd = new NpgsqlCommand(migration, conn))
            await cmd.ExecuteNonQueryAsync();
        // Drugie uruchomienie niczego nie zmienia (nie liczy skrótu ze skrótu).
        await using (var cmd = new NpgsqlCommand(migration, conn))
            await cmd.ExecuteNonQueryAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Nie znaleziono " + string.Join('/', parts));
    }
}

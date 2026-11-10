using Npgsql;

// Kto może nadać status "Wydany". Domyślnie każdy, kto ma dostęp do elementu; po włączeniu
// release_admin_only (Ustawienia -> Użytkownicy) tylko administrator — autor przekazuje
// element do sprawdzenia, a wydaje go ktoś z uprawnieniami.
static class ReleasePolicy
{
    // Brak wiersza w system_state (baza sprzed migracji 060) = false, czyli zachowanie
    // sprzed wprowadzenia tej opcji.
    public static async Task<bool> GetAdminOnlyAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT release_admin_only FROM system_state WHERE id = true;", conn);
        return await cmd.ExecuteScalarAsync() is true;
    }
}

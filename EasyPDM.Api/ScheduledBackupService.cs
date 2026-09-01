using Npgsql;

// Tło sprawdzające co minutę, czy nadszedł czas na automatyczną kopię zapasową wg
// harmonogramu w tabeli backup_schedule (Ustawienia -> Magazyn plików -> Automatyczna
// kopia). Kopia trafia do osobnego katalogu (BackupRoot) jako ZIP — ta sama zawartość
// co ręczne GET /api/settings/backup, tylko zapisana na dysku zamiast wysłana do
// przeglądarki. Stare automatyczne kopie ponad limit (schedule.RetentionCount, ustawiany
// w Ustawieniach) są kasowane, żeby katalog nie rósł bez końca.
class ScheduledBackupService(
    string connectionString, StorageSettings storage, string backupRoot, ILogger logger)
{
    // Backup nie próbuje się TYLKO w dokładnie zaplanowanej minucie — jeśli ta jedna próba
    // zawiedzie (np. chwilowy brak miejsca na dysku), bez retry kopia zostałaby całkowicie
    // pominięta na resztę dnia (kolejna szansa dopiero jutro, bez żadnego powiadomienia poza
    // logiem błędu). Ponawiamy więc co RetryInterval, aż do sukcesu albo końca dnia.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private DateTime? _lastFailedAttemptAt;

    // Uruchamiane ręcznie z Program.cs (nie przez DI/IHostedService — "app" jest tam budowane
    // przed rejestracją jakichkolwiek usług), z tokenem powiązanym z zamykaniem aplikacji
    // (app.Lifetime.ApplicationStopping).
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automatyczna kopia zapasowa: nieoczekiwany błąd podczas sprawdzania harmonogramu.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task CheckAndRunAsync(CancellationToken stoppingToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(stoppingToken);

        var schedule = await SettingsEndpoints.GetBackupScheduleAsync(conn);
        if (!schedule.Enabled)
            return;

        var now = DateTime.Now;

        // LastRunAt wraca z Npgsql jako DateTime o Kind=Utc (kolumna last_run_at jest
        // TIMESTAMPTZ) — porównanie ".Date" wprost z "now" (Kind=Local) byłoby błędne w
        // strefach czasowych innych niż UTC: blisko północy (np. harmonogram na 00:15 w
        // Polsce, UTC+1/+2) obie daty kalendarzowe różniłyby się, zabezpieczenie "już dziś
        // była kopia" nigdy by się nie włączało, a usługa odpalałaby kopię ponownie co
        // minutę, dopóki czas lokalny nie doszedłby do przesunięcia strefy. ToLocalTime()
        // sprowadza obie strony porównania do tej samej, lokalnej doby.
        var lastRunLocal = schedule.LastRunAt?.ToLocalTime();

        // Niezależnie od częstotliwości: nie uruchamiaj drugi raz tego samego dnia (jedno
        // uruchomienie dziennie to i tak najczęstszy dopuszczalny wybór — "codziennie").
        if (lastRunLocal is not null && lastRunLocal.Value.Date == now.Date)
            return;

        // Od zaplanowanej minuty AŻ DO KOŃCA DNIA (nie tylko dokładnie w niej) — dzięki temu
        // jednorazowy błąd w catch niżej nie pomija całego dnia, tylko czeka na retry.
        var scheduledTimeToday = now.Date.AddHours(schedule.Hour).AddMinutes(schedule.Minute);
        if (now < scheduledTimeToday)
            return;
        if (_lastFailedAttemptAt is not null && now - _lastFailedAttemptAt.Value < RetryInterval)
            return;

        var dueToday = schedule.Frequency switch
        {
            "daily" => true,
            "weekly" => (int)now.DayOfWeek == schedule.DayOfWeek,
            "monthly" => now.Day == Math.Min(schedule.DayOfMonth ?? 1, DateTime.DaysInMonth(now.Year, now.Month)),
            _ => false
        };
        if (!dueToday)
            return;

        logger.LogInformation("Automatyczna kopia zapasowa: uruchamiam ({Frequency}).", schedule.Frequency);
        try
        {
            var bytes = await SettingsEndpoints.CreateBackupZipAsync(connectionString, storage);

            Directory.CreateDirectory(backupRoot);
            var fileName = $"pdm-auto-backup-{now:yyyy-MM-dd_HHmm}.zip";
            await File.WriteAllBytesAsync(Path.Combine(backupRoot, fileName), bytes, stoppingToken);

            PruneOldBackups(schedule.RetentionCount);

            const string sql = "UPDATE backup_schedule SET last_run_at = @now WHERE id = true;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync(stoppingToken);

            _lastFailedAttemptAt = null;
            logger.LogInformation("Automatyczna kopia zapasowa: zapisano {FileName}.", fileName);
        }
        catch (Exception ex)
        {
            _lastFailedAttemptAt = now;
            logger.LogError(ex,
                "Automatyczna kopia zapasowa: nie udało się jej wykonać — ponowię próbę za {Minutes} min " +
                "(o ile dzień się nie zmieni).", RetryInterval.TotalMinutes);
        }
    }

    private void PruneOldBackups(int retentionCount)
    {
        if (!Directory.Exists(backupRoot))
            return;

        var files = Directory.GetFiles(backupRoot, "pdm-auto-backup-*.zip")
            .OrderByDescending(f => f)
            .Skip(retentionCount);

        foreach (var file in files)
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}

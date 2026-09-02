using Npgsql;

// Tło sprawdzające co 15 minut ilość wolnego miejsca na dysku, na którym leży magazyn
// plików (StorageSettings.Path) — jeśli spadnie poniżej progu, powiadamia wszystkich
// administratorów, najwyżej raz dziennie (żeby nie zasypać ich tym samym powiadomieniem
// co 15 minut, dopóki ktoś nie zwolni miejsca).
class DiskSpaceMonitorService(string connectionString, StorageSettings storage, ILogger logger)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    // Próg: mniejsza z dwóch wartości — 2 GB albo 5% pojemności dysku — żeby sensownie
    // działało zarówno na małych, jak i bardzo dużych wolumenach.
    private const long MinFreeBytes = 2L * 1024 * 1024 * 1024;
    private const double MinFreeFraction = 0.05;

    // Stan tylko w pamięci procesu (nie w bazie) — ta sama uproszczona logika "raz
    // dziennie" co _lastFailedAttemptAt w ScheduledBackupService: po restarcie może się
    // powiadomić ponownie tego samego dnia, akceptowalne dla ostrzeżenia tego typu.
    private DateTime? _lastNotifiedDate;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Monitor miejsca na dysku: nieoczekiwany błąd podczas sprawdzania.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task CheckAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.Now;
        if (_lastNotifiedDate is not null && _lastNotifiedDate.Value.Date == now.Date)
            return;

        DriveInfo drive;
        try
        {
            drive = new DriveInfo(storage.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Monitor miejsca na dysku: nie udało się odczytać informacji o dysku dla ścieżki '{Path}'.", storage.Path);
            return;
        }

        var freeBytes = drive.AvailableFreeSpace;
        var totalBytes = drive.TotalSize;
        var threshold = Math.Min(MinFreeBytes, (long)(totalBytes * MinFreeFraction));
        if (freeBytes > threshold)
            return;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(stoppingToken);

        var adminIds = new List<Guid>();
        await using (var cmd = new NpgsqlCommand("SELECT id FROM users WHERE role = 'admin';", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(stoppingToken))
        {
            while (await reader.ReadAsync(stoppingToken))
                adminIds.Add(reader.GetGuid(0));
        }

        var freeGb = Math.Round(freeBytes / 1024.0 / 1024.0 / 1024.0, 1);
        var totalGb = Math.Round(totalBytes / 1024.0 / 1024.0 / 1024.0, 1);
        foreach (var adminId in adminIds)
            await Notifications.NotifyAsync(conn, logger, adminId, "low_disk_space", new { freeGb, totalGb });

        _lastNotifiedDate = now;
    }
}

static class ConfigEndpoints
{
    // GET /api/config — pozwala klientom (np. makru FreeCAD działającemu na tej samej
    // maszynie/vaulcie co API) poznać lokalizację magazynu plików bez sztywnego kodowania
    // ścieżki po ich stronie.
    public static void MapConfigEndpoints(this WebApplication app, StorageSettings storage)
    {
        app.MapGet("/api/config", () => Results.Ok(new { storageRoot = storage.Path }));
    }
}

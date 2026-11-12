// Pliki tymczasowe na duże odpowiedzi (kopia zapasowa, ZIP dokumentacji). Wcześniej archiwa
// powstawały w pamięci albo były czytane do byte[] przed wysłaniem: magazyn większy niż
// ~2 GB przekraczał limit tablicy, a mniejszy i tak zajmował tyle RAM-u, ile ważył. Teraz
// archiwum leży w katalogu tymczasowym i jest wysyłane strumieniem prosto z dysku.
static class TempFiles
{
    public static string NewPath(string prefix, string extension) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}{extension}");

    // Strumień do odczytu, który usuwa plik przy zamknięciu. Results.File zamyka strumień po
    // wysłaniu odpowiedzi (także przy zerwanym połączeniu), więc plik nie zostaje na dysku.
    public static FileStream OpenDeleteOnClose(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    public static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Postęp wysyłki/pobierania: makro zgłasza listę plików i odhacza kolejne, a aplikacja webowa
// pokazuje to jako listę z ptaszkami. Stan żyje w pamięci serwera i jest kluczowany
// UŻYTKOWNIKIEM -- dzięki temu przeglądarka pyta "co robi moje makro?" bez znajomości
// jakiegokolwiek identyfikatora. Te testy pilnują trzech rzeczy, na których to stoi:
// liczenia postępu po stronie serwera, nieprzerywania pracy makra przy nieznanym kluczu
// i tego, że zakończony bieg przestaje być wydawany.
[Collection("EasyPDM database")]
public class TransferProgressTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, string kind, params string[] keys) =>
        await client.PutAsJsonAsync("/api/progress", new
        {
            kind,
            entries = keys.Select(k => new { key = k, label = k + ".SLDPRT" }).ToArray(),
        });

    private static async Task<HttpResponseMessage> MarkAsync(HttpClient client, string key, string status) =>
        await client.PatchAsJsonAsync("/api/progress", new { key, status });

    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/progress");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("progress");
    }

    [Fact]
    public async Task Brak_biegu_to_zwykly_stan_a_nie_blad()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();

        // 200 z "null", nie 404 -- front nie ma odróżniać "nie ma biegu" od "zły adres".
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(client)).ValueKind);
    }

    [Fact]
    public async Task Serwer_liczy_postep_sam()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "upload", "a", "b", "c", "d")).EnsureSuccessStatusCode();
        var fresh = await ReadAsync(client);
        Assert.Equal(4, fresh.GetProperty("total").GetInt32());
        Assert.Equal(0, fresh.GetProperty("done").GetInt32());

        (await MarkAsync(client, "a", "done")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "b", "skipped")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "c", "failed")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "d", "active")).EnsureSuccessStatusCode();

        // "skipped" liczy się jako załatwione (komponent już podpięty, nie ma czego wysyłać),
        // "failed" i "active" nie. Liczenie siedzi na serwerze, nie w kliencie -- ta sama
        // zasada co przy itemNumberLabel/recordName.
        var after = await ReadAsync(client);
        Assert.Equal(2, after.GetProperty("done").GetInt32());
        Assert.Equal(4, after.GetProperty("total").GetInt32());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Nieznany_klucz_nie_jest_bledem_dla_makra()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();

        // Makro odhacza pozycje także wtedy, gdy serwer zdążył się zrestartować albo bieg
        // wygasł. To NIE może być błąd -- inaczej raportowanie postępu wywracałoby wysyłkę,
        // czyli rzecz, dla której użytkownik w ogóle uruchomił makro.
        var response = await MarkAsync(client, "czegos-takiego-nie-ma", "done");
        response.EnsureSuccessStatusCode();
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matched").GetBoolean());
    }

    [Fact]
    public async Task Nowy_bieg_zastepuje_poprzedni()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "upload", "stary1", "stary2", "stary3")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "stary1", "done")).EnsureSuccessStatusCode();

        // Kolejne kliknięcie "Upload" unieważnia poprzednią listę -- bez tego panel pokazywałby
        // wymieszane pozycje z dwóch biegów.
        (await StartAsync(client, "download", "nowy1", "nowy2")).EnsureSuccessStatusCode();

        var after = await ReadAsync(client);
        Assert.Equal("download", after.GetProperty("kind").GetString());
        Assert.Equal(2, after.GetProperty("total").GetInt32());
        Assert.Equal(0, after.GetProperty("done").GetInt32());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Odrzuca_zduplikowane_klucze_i_zle_wartosci()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        // Klucze są tym, po czym idzie odhaczanie -- duplikat oznaczałby, że jedno
        // odhaczenie trafia w dwie pozycje naraz.
        var duplicate = await client.PutAsJsonAsync("/api/progress", new
        {
            kind = "upload",
            entries = new[] { new { key = "ten-sam", label = "a" }, new { key = "ten-sam", label = "b" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(client, "cos-innego", "a")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/progress", new { kind = "upload", entries = Array.Empty<object>() })).StatusCode);

        (await StartAsync(client, "upload", "a")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await MarkAsync(client, "a", "nieznany-status")).StatusCode);

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Zakonczony_bieg_zostaje_oznaczony_a_nie_skasowany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "upload", "a", "b")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "a", "done")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "b", "done")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/finish", null)).EnsureSuccessStatusCode();

        // Lista z kompletem ptaszków JEST potwierdzeniem, że wszystko poszło -- zostaje
        // widoczna przez chwilę po zakończeniu, zamiast znikać w momencie ostatniego pliku.
        var after = await ReadAsync(client);
        Assert.True(after.GetProperty("finished").GetBoolean());
        Assert.Equal(2, after.GetProperty("done").GetInt32());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(client)).ValueKind);
    }

    // Raport z biegu. Makro kończyło dotąd blokującym oknem w CAD-zie -- a odkąd fokus po
    // wysyłce wraca do przeglądarki, takie okno powstaje ZA nią i wisi, czekając na
    // kliknięcie, którego nikt nie widzi (zgłoszone z praktyki). Raport powstaje więc tam,
    // gdzie człowiek i tak patrzy, i składa go SERWER -- jedno miejsce na trzy CAD-y.
    [Fact]
    public async Task Koniec_biegu_zostawia_raport_w_powiadomieniach()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "upload", "a", "b", "c", "d")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "a", "done")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "b", "done")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "c", "skipped")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "d", "failed")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/finish", null)).EnsureSuccessStatusCode();

        var report = await LatestReportAsync(client);
        var data = report.GetProperty("data");
        Assert.Equal("upload", data.GetProperty("kind").GetString());
        Assert.Equal(4, data.GetProperty("total").GetInt32());
        // "done" NIE obejmuje pominiętych: pominięty komponent to taki, który już był w PDM
        // i nie trzeba go było wysyłać -- policzenie go jako wysłanego zawyżałoby raport.
        Assert.Equal(2, data.GetProperty("done").GetInt32());
        Assert.Equal(1, data.GetProperty("skipped").GetInt32());
        Assert.Equal(1, data.GetProperty("failed").GetInt32());
        Assert.Equal(0, data.GetProperty("pending").GetInt32());

        // Nazwy plików, nie same liczby -- po to jest raport, żeby dało się zobaczyć, CO poszło.
        var labels = data.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("label").GetString()).ToList();
        Assert.Contains("a.SLDPRT", labels);
        Assert.Contains("d.SLDPRT", labels);

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // Bieg przerwany w połowie też daje raport -- i nie nazywa niedokończonych pozycji
    // błędami, bo nic się nie zepsuło, po prostu do nich nie doszło.
    [Fact]
    public async Task Przerwany_bieg_liczy_niedokonczone_osobno_od_bledow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "download", "a", "b", "c")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "a", "done")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/finish", null)).EnsureSuccessStatusCode();

        var data = (await LatestReportAsync(client)).GetProperty("data");
        Assert.Equal("download", data.GetProperty("kind").GetString());
        Assert.Equal(1, data.GetProperty("done").GetInt32());
        Assert.Equal(0, data.GetProperty("failed").GetInt32());
        Assert.Equal(2, data.GetProperty("pending").GetInt32());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // Makro potrafi zawołać "finish" dwa razy (ścieżka błędu plus normalne zakończenie).
    // Drugie wywołanie nie ma prawa zostawić drugiego, identycznego raportu.
    [Fact]
    public async Task Powtorzone_zakonczenie_nie_dubluje_raportu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var before = (await ReportsAsync(client)).Count;

        (await StartAsync(client, "upload", "a")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "a", "done")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/finish", null)).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/finish", null)).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, (await ReportsAsync(client)).Count);

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    private static async Task<int> CancelledAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/progress/cancelled");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cancelled").GetInt32();
    }

    // Przycisk „Anuluj" w panelu postępu. Serwer niczego sam nie przerywa -- zapisuje prośbę,
    // a makro sprawdza ją przed kolejnym plikiem. Ten test pilnuje kontraktu, na którym to
    // stoi: makro widzi 0, dopóki nikt nie anulował, i 1 od chwili anulowania.
    [Fact]
    public async Task Anulowanie_jest_widoczne_dla_makra()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "upload", "a", "b", "c")).EnsureSuccessStatusCode();
        Assert.Equal(0, await CancelledAsync(client));

        var cancel = await client.PostAsync("/api/progress/cancel", null);
        cancel.EnsureSuccessStatusCode();
        Assert.True((await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matched").GetBoolean());

        // Liczba, nie wartość logiczna: parsery JSON w makrach VBA mają tylko JsonGetLong.
        Assert.Equal(1, await CancelledAsync(client));
        Assert.True((await ReadAsync(client)).GetProperty("cancelled").GetBoolean());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // Raport z przerwanego biegu mówi, że go przerwano -- inaczej "1 z 3" wyglądałoby jak
    // awaria, a nie jak decyzja użytkownika. Nieodhaczone pozycje to "pending", nie "failed".
    [Fact]
    public async Task Raport_z_anulowanego_biegu_mowi_ze_go_przerwano()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "upload", "a", "b", "c")).EnsureSuccessStatusCode();
        (await MarkAsync(client, "a", "done")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/cancel", null)).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/finish", null)).EnsureSuccessStatusCode();

        var data = (await LatestReportAsync(client)).GetProperty("data");
        Assert.True(data.GetProperty("cancelled").GetBoolean());
        Assert.Equal(1, data.GetProperty("done").GetInt32());
        Assert.Equal(2, data.GetProperty("pending").GetInt32());
        Assert.Equal(0, data.GetProperty("failed").GetInt32());

        // Po zakończeniu makro nie ma już czego pytać -- następny bieg startuje od zera.
        Assert.Equal(0, await CancelledAsync(client));

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // Anulowanie zakończonego albo nieistniejącego biegu nic nie robi -- spóźnione kliknięcie
    // nie może oznaczyć jako przerwanego następnego biegu, który dopiero ruszy.
    [Fact]
    public async Task Anulowanie_bez_trwajacego_biegu_nic_nie_robi()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();

        var none = await client.PostAsync("/api/progress/cancel", null);
        Assert.False((await none.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matched").GetBoolean());

        (await StartAsync(client, "upload", "a")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/progress/finish", null)).EnsureSuccessStatusCode();
        var late = await client.PostAsync("/api/progress/cancel", null);
        Assert.False((await late.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matched").GetBoolean());

        (await StartAsync(client, "upload", "x")).EnsureSuccessStatusCode();
        Assert.Equal(0, await CancelledAsync(client));

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------------------------------------
    // Lista ułożona w drzewo: złożenie, pod nim z wcięciem jego zawartość.

    private static async Task<List<(string Key, int Depth)>> TreeAsync(HttpClient client)
    {
        var progress = await ReadAsync(client);
        return progress.GetProperty("entries").EnumerateArray()
            .Select(e => (e.GetProperty("key").GetString()!, e.GetProperty("depth").GetInt32()))
            .ToList();
    }

    private static object Entries(params string[] keys) =>
        keys.Select(k => new { key = k, label = k }).ToArray();

    // Makro wysyła od liści (złożenia nie da się podpiąć, zanim nie istnieją jego części), a
    // lista ma stać w kolejności drzewa. Odhaczanie idzie po kluczu, więc jedno drugiemu nie
    // przeszkadza.
    [Fact]
    public async Task Wysylka_ze_struktura_jest_ulozona_w_drzewo()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await client.PutAsJsonAsync("/api/progress", new
        {
            kind = "upload",
            entries = Entries("a", "b", "sub", "c", "__top__"),
            edges = new[]
            {
                new { parent = "__top__", child = "sub" }, new { parent = "__top__", child = "c" },
                new { parent = "sub", child = "a" }, new { parent = "sub", child = "b" },
            },
        })).EnsureSuccessStatusCode();

        Assert.Equal(
            [("__top__", 0), ("sub", 1), ("a", 2), ("b", 2), ("c", 1)],
            await TreeAsync(client));

        // Odhaczanie dalej trafia we właściwą pozycję, choć kolejność listy jest inna.
        (await MarkAsync(client, "a", "done")).EnsureSuccessStatusCode();
        var a = (await ReadAsync(client)).GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("key").GetString() == "a");
        Assert.Equal("done", a.GetProperty("status").GetString());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // Część użyta w kilku złożeniach jest wysyłana raz, więc i na liście stoi raz — pod
    // PIERWSZYM złożeniem, w którym występuje. Licznik "x z y" liczy pliki, nie wiersze.
    [Fact]
    public async Task Czesc_wspolna_stoi_pod_pierwszym_zlozeniem()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await client.PutAsJsonAsync("/api/progress", new
        {
            kind = "upload",
            entries = Entries("sruba", "s1", "s2", "__top__"),
            edges = new[]
            {
                new { parent = "__top__", child = "s1" }, new { parent = "__top__", child = "s2" },
                new { parent = "s1", child = "sruba" }, new { parent = "s2", child = "sruba" },
            },
        })).EnsureSuccessStatusCode();

        Assert.Equal(
            [("__top__", 0), ("s1", 1), ("sruba", 2), ("s2", 1)],
            await TreeAsync(client));
        Assert.Equal(4, (await ReadAsync(client)).GetProperty("total").GetInt32());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // Bez relacji (starsze makro, pojedynczy plik) lista zostaje dokładnie taka, jak przyszła.
    // Cykl w danych nie gubi żadnej pozycji — lista bez którejś kłamałaby przy liczniku.
    [Fact]
    public async Task Bez_relacji_plasko_a_cykl_niczego_nie_gubi()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        (await StartAsync(client, "upload", "x", "y", "z")).EnsureSuccessStatusCode();
        Assert.Equal([("x", 0), ("y", 0), ("z", 0)], await TreeAsync(client));

        (await client.PutAsJsonAsync("/api/progress", new
        {
            kind = "upload",
            entries = Entries("p", "q", "r"),
            edges = new[] { new { parent = "p", child = "q" }, new { parent = "q", child = "p" } },
        })).EnsureSuccessStatusCode();
        var cyclic = await TreeAsync(client);
        Assert.Equal(3, cyclic.Count);
        Assert.Equal(["p", "q", "r"], cyclic.Select(e => e.Key).OrderBy(k => k).ToList());

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    // Przy pobieraniu kluczami są identyfikatory elementów, a relacje są już w bazie — serwer
    // bierze je sam, więc makra pobierania działają z drzewem bez żadnej zmiany.
    [Fact]
    public async Task Pobieranie_bierze_strukture_z_bazy()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        async Task<string> Create(string name, string type, string? parentId)
        {
            var response = await client.PostAsJsonAsync("/api/nodes", new { name, itemType = type, parentId });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        }
        var top = await Create("gora-" + Guid.NewGuid().ToString("N")[..6], "assembly", null);
        var sub = await Create("pod", "assembly", top);
        var part = await Create("czesc", "part", sub);
        var loose = await Create("luzna", "part", top);

        // Kolejność, w jakiej makro dostaje listę z /descendants, nie ma znaczenia.
        (await client.PutAsJsonAsync("/api/progress", new
        {
            kind = "download",
            entries = Entries(part, top, loose, sub),
        })).EnsureSuccessStatusCode();

        var tree = await TreeAsync(client);
        Assert.Equal((top, 0), tree[0]);
        Assert.Contains((sub, 1), tree);
        Assert.Contains((loose, 1), tree);
        Assert.Contains((part, 2), tree);
        Assert.True(tree.IndexOf((part, 2)) == tree.IndexOf((sub, 1)) + 1, "część ma stać tuż pod swoim podzłożeniem");

        (await client.DeleteAsync("/api/progress")).EnsureSuccessStatusCode();
    }

    private static async Task<List<JsonElement>> ReportsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/notifications");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == "cad_transfer_finished")
            .ToList();
    }

    private static async Task<JsonElement> LatestReportAsync(HttpClient client)
    {
        var reports = await ReportsAsync(client);
        Assert.NotEmpty(reports);
        return reports[0];
    }
}

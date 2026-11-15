// EasyPDM companion: does the network part for CAD programs whose plugins cannot reach the
// server themselves -- today SolidCraft, whose WebAssembly plugins have no network at all.
//
// The SolidCraft plugin writes an upload into the outbox folder: the design (.sc), a STEP and
// <id>.request.json. This program picks the request up and uploads it exactly the way the
// FreeCAD and SolidWorks macros upload a document (EasyPDM.FreeCad/EasyPDMUpload.FCMacro):
//
//   - a design already linked to an item (request.easypdm.itemId) goes to that item; a released
//     one gets a new revision only when the request carries a comment, as the macros ask for one;
//   - otherwise the item is chosen in the browser: the request is offered to an EasyPDM tab this
//     run already opened (PUT /cad-requests), or a new tab opens through the one-time browser
//     login bridge, and the program waits for the browser to create or pick the item
//     (GET /create-tickets/{ticket});
//   - the design goes up as the item's "cad" attachment named "<record name>.<revision>.sc", the
//     STEP as its "step" attachment, a preview as its "image", and the mass into its properties.
//
// The outcome is written next to the request as <id>.result.json, and the processed files move
// to outbox/done, so a request is never uploaded twice.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EasyPDM.Companion;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        var cmd = args.FirstOrDefault() ?? "help";
        var opts = Options.Parse(args.Skip(1).ToArray());
        try
        {
            switch (cmd)
            {
                case "login": return await Commands.Login(opts);
                case "logout": return await Commands.Logout();
                case "status": return await Commands.Status();
                case "process": return await Commands.Process(opts, watch: false);
                case "watch": return await Commands.Process(opts, watch: true);
                default:
                    Console.WriteLine("""
                        easypdm-companion login --server http://host:port [--user NAME]
                        easypdm-companion logout
                        easypdm-companion status
                        easypdm-companion process [--outbox DIR] [--no-browser]   upload what is waiting, then exit
                        easypdm-companion watch [--outbox DIR]     keep uploading what arrives
                        """);
                    return cmd == "help" ? 0 : 2;
            }
        }
        catch (CompanionException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}

sealed class CompanionException(string message) : Exception(message);

sealed class Options
{
    public Dictionary<string, string> Values { get; } = new();
    public string? this[string key] => Values.TryGetValue(key, out var v) ? v : null;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "--no-browser")
                o.Values["no-browser"] = "1";
            else if (args[i].StartsWith("--") && i + 1 < args.Length)
                o.Values[args[i][2..]] = args[++i];
        return o;
    }
}

// Server address and macro token, in the user's own config folder. The token is a macro token
// (POST /auth/macro-login, client "EasyPDM companion"): listed and revocable in Settings > Users
// like any CAD macro's, and never the password.
sealed class Config
{
    public string? Server { get; set; }
    public string? Token { get; set; }
    public string? DisplayName { get; set; }

    static string Dir => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyPDM")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "EasyPDM");
    static string FilePath => Path.Combine(Dir, "companion.json");

    public static Config Load() =>
        File.Exists(FilePath) ? JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new() : new();

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

sealed class Api
{
    public const string SessionCookie = "pdm_session";
    readonly HttpClient _http;
    public string Base { get; }

    public Api(string server, string? token)
    {
        Base = server.TrimEnd('/') + "/api";
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (token is not null)
            _http.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie}={token}");
    }

    public static Api FromConfig()
    {
        var c = Config.Load();
        if (c.Server is null || c.Token is null)
            throw new CompanionException("Not logged in: run `easypdm-companion login --server URL` first.");
        return new Api(c.Server, c.Token);
    }

    static async Task<HttpResponseMessage> Check(HttpResponseMessage r, string what)
    {
        if (r.IsSuccessStatusCode) return r;
        var body = await r.Content.ReadAsStringAsync();
        if (r.StatusCode == HttpStatusCode.Unauthorized)
            throw new CompanionException($"{what}: not logged in or the login has expired — run `easypdm-companion login` again.");
        throw new CompanionException($"{what}: {(int)r.StatusCode} {body.Trim()}");
    }

    public async Task<JsonNode?> Get(string path) =>
        JsonNode.Parse(await (await Check(await _http.GetAsync(Base + path), "GET " + path)).Content.ReadAsStringAsync());

    public async Task<(HttpStatusCode, JsonNode?)> GetRaw(string path)
    {
        var r = await _http.GetAsync(Base + path);
        var text = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, text.Length > 0 ? JsonNode.Parse(text) : null);
    }

    public async Task<JsonNode?> Send(HttpMethod method, string path, object? body)
    {
        var req = new HttpRequestMessage(method, Base + path);
        if (body is not null) req.Content = JsonContent.Create(body);
        var text = await (await Check(await _http.SendAsync(req), $"{method} {path}")).Content.ReadAsStringAsync();
        return text.Length > 0 ? JsonNode.Parse(text) : null;
    }

    public async Task Upload(string path, string file, string fileName, IDictionary<string, string> fields)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        var content = new StreamContent(File.OpenRead(file));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", fileName);
        await Check(await _http.PostAsync(Base + path, form), "upload " + fileName);
    }
}

static class Commands
{
    public static async Task<int> Login(Options o)
    {
        var c = Config.Load();
        var server = o["server"] ?? c.Server ?? throw new CompanionException("Give the server: --server http://host:port");
        var user = o["user"] ?? Prompt("Login: ");
        var password = ReadPassword("Password: ");
        var api = new Api(server, null);
        var reply = await api.Send(HttpMethod.Post, "/auth/macro-login",
            new { username = user, password, client = "EasyPDM companion" });
        var token = reply?["sessionToken"]?.GetValue<string>() ?? throw new CompanionException("The server returned no token.");
        c.Server = server; c.Token = token;
        c.DisplayName = reply?["displayName"]?.GetValue<string>() ?? user;
        c.Save();
        Console.WriteLine($"Logged in to {server} as {c.DisplayName}.");
        return 0;
    }

    public static async Task<int> Logout()
    {
        var c = Config.Load();
        if (c.Server is not null && c.Token is not null)
        {
            try { await new Api(c.Server, c.Token).Send(HttpMethod.Post, "/auth/logout", null); } catch { /* signed out locally anyway */ }
        }
        c.Token = null; c.DisplayName = null; c.Save();
        Console.WriteLine("Logged out.");
        return 0;
    }

    public static async Task<int> Status()
    {
        var c = Config.Load();
        if (c.Token is null) { Console.WriteLine($"Not logged in{(c.Server is null ? "" : $" (server {c.Server})")}."); return 1; }
        var me = await Api.FromConfig().Get("/auth/me");
        Console.WriteLine($"Logged in to {c.Server} as {me?["displayName"] ?? me?["username"]}.");
        return 0;
    }

    public static async Task<int> Process(Options o, bool watch)
    {
        var outbox = o["outbox"] ?? Outbox.DefaultPath();
        Directory.CreateDirectory(outbox);
        var api = Api.FromConfig();
        var uploader = new Uploader(api, new SolidCraftCli(o["solidcraft-cli"]), openBrowser: o.Values.ContainsKey("no-browser") is false);
        Console.WriteLine($"EasyPDM companion: outbox {outbox}");
        do
        {
            foreach (var request in Outbox.Pending(outbox))
                await uploader.Handle(outbox, request);
            if (watch) await Task.Delay(TimeSpan.FromSeconds(2));
        } while (watch);
        return 0;
    }

    static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine()?.Trim() ?? "";
    }

    static string ReadPassword(string label)
    {
        Console.Write(label);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}

static class Outbox
{
    // ~/Documents/EasyPDM/outbox, in the language-specific Documents folder (xdg-user-dir on
    // Linux): the SolidCraft Flatpak sees only the Documents folder.
    public static string DefaultPath()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("xdg-user-dir", "DOCUMENTS") { RedirectStandardOutput = true });
                var line = p?.StandardOutput.ReadToEnd().Trim();
                p?.WaitForExit();
                if (!string.IsNullOrEmpty(line) && Directory.Exists(line)) docs = line;
            }
            catch { /* no xdg-user-dir: keep the default */ }
        }
        return Path.Combine(docs, "EasyPDM", "outbox");
    }

    public static IEnumerable<string> Pending(string outbox) =>
        Directory.EnumerateFiles(outbox, "*.request.json").Order(StringComparer.Ordinal).ToList();
}

// The SolidCraft command-line program, for the preview image and the mass: the plugin cannot
// take a picture, and SolidCraft's own mass query comes back empty for a design without
// materials, while `eval` measures every body at its (default) material.
sealed class SolidCraftCli(string? configured)
{
    string[]? _cmd;

    string[]? Command()
    {
        if (_cmd is not null) return _cmd;
        if (configured is not null) return _cmd = [configured];
        if (OperatingSystem.IsWindows())
        {
            var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SolidCraft", "solidcraft-cli.exe");
            if (File.Exists(pf)) return _cmd = [pf];
            return _cmd = ["solidcraft-cli.exe"];
        }
        // The Flatpak first: that is how SolidCraft is installed on most Linux desktops.
        return _cmd = ["flatpak", "run", "--command=solidcraft-cli", "io.tangencylabs.solidcraft"];
    }

    async Task<(int code, string stdout)> Run(params string[] args)
    {
        var cmd = Command()!;
        var psi = new ProcessStartInfo(cmd[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in cmd.Skip(1).Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new CompanionException("could not start solidcraft-cli");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
            Console.Error.WriteLine($"  solidcraft-cli {args[0]} failed ({p.ExitCode}): {(await stderr).Trim()}");
        return (p.ExitCode, await stdout);
    }

    public async Task<double?> MassKg(string design)
    {
        try
        {
            var (code, json) = await Run("eval", design);
            if (code != 0) return null;
            var bodies = JsonNode.Parse(json)?["bodies"]?.AsArray();
            if (bodies is null || bodies.Count == 0) return null;
            double grams = 0;
            foreach (var b in bodies)
                grams += b?["mass_g"]?.GetValue<double>() ?? 0;
            return grams > 0 ? grams / 1000 : null;
        }
        catch (Exception e) { Console.Error.WriteLine($"  mass not measured: {e.Message}"); return null; }
    }

    public async Task<bool> Snapshot(string design, string png)
    {
        try
        {
            var (code, _) = await Run("snapshot", "--in", design, "--out", png, "--view", "iso",
                "--width", "800", "--height", "600", "--no-grid", "--no-axes");
            return code == 0 && File.Exists(png);
        }
        catch (Exception e) { Console.Error.WriteLine($"  no preview: {e.Message}"); return false; }
    }
}

sealed class Uploader(Api api, SolidCraftCli cli, bool openBrowser)
{
    // One per run of this program, like the macros' runId: an EasyPDM tab this run opened takes
    // the next request itself instead of a new tab opening for every upload.
    readonly string _runId = Guid.NewGuid().ToString("N");
    bool _tabOpened;

    public async Task Handle(string outbox, string requestPath)
    {
        var request = JsonNode.Parse(await File.ReadAllTextAsync(requestPath))!;
        var id = request["id"]?.ToString() ?? Path.GetFileName(requestPath).Split('.')[0];
        Console.WriteLine($"[{id}] upload");
        JsonObject result;
        try
        {
            result = await Upload(outbox, request);
            Console.WriteLine($"[{id}] {result["message"]}");
        }
        catch (CompanionException e)
        {
            Console.Error.WriteLine($"[{id}] failed: {e.Message}");
            result = new JsonObject { ["ok"] = false, ["message"] = e.Message };
        }
        result["id"] = id;
        await File.WriteAllTextAsync(Path.Combine(outbox, $"{id}.result.json"), result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var done = Path.Combine(outbox, "done");
        Directory.CreateDirectory(done);
        foreach (var f in Directory.EnumerateFiles(outbox, id + ".*"))
            if (!f.EndsWith(".result.json"))
                File.Move(f, Path.Combine(done, Path.GetFileName(f)), overwrite: true);
    }

    async Task<JsonObject> Upload(string outbox, JsonNode request)
    {
        var design = Path.Combine(outbox, request["design"]?.ToString() ?? throw new CompanionException("the request names no design"));
        if (!File.Exists(design)) throw new CompanionException($"{design} is missing");
        var step = request["step"]?.ToString() is { Length: > 0 } s ? Path.Combine(outbox, s) : null;
        var comment = request["comment"]?.ToString();
        var props = request["properties"];
        var name = props?["description"]?.ToString() is { Length: > 0 } d ? d
            : props?["part_number"]?.ToString() is { Length: > 0 } pn ? pn : "SolidCraft design";

        // Which item: the design's own link, or the browser.
        var itemId = request["easypdm"]?["itemId"]?.ToString();
        var existing = itemId is not null;
        var exportStep = true;
        if (itemId is null)
        {
            var ticket = await ChooseInBrowser(name);
            itemId = ticket["itemId"]!.ToString();
            existing = ticket["existing"]?.GetValue<bool>() ?? false;
            exportStep = ticket["exportStep"]?.GetValue<bool?>() ?? true;
        }

        var item = await api.Get($"/items/{itemId}") ?? throw new CompanionException("item not found");
        if (existing && (item["isLocked"]?.GetValue<bool>() ?? false))
        {
            var status = item["status"]?.ToString();
            if (status is not ("wydany" or "anulowana"))
                throw new CompanionException($"the item is {status} and cannot take a new file now");
            if (string.IsNullOrWhiteSpace(comment))
                throw new CompanionException("the item is released: upload again with a comment to start a new revision");
            await api.Send(HttpMethod.Patch, $"/items/{itemId}/status", new { status = "w_pracy", comment });
            item = await api.Get($"/items/{itemId}") ?? item;
        }

        var record = item["recordName"]?.ToString() ?? $"{item["itemNumber"]}({item["name"]})";
        var revision = item["revisionLabel"]?.ToString() ?? "A";
        var baseName = $"{Sanitize(record)}.{revision}";

        await api.Upload($"/items/{itemId}/attachments", design, baseName + ".sc", new Dictionary<string, string> { ["role"] = "cad" });
        if (exportStep && step is not null && File.Exists(step))
            await api.Upload($"/items/{itemId}/attachments", step, baseName + ".step", new Dictionary<string, string> { ["role"] = "step" });

        var png = Path.Combine(outbox, Path.GetFileNameWithoutExtension(design) + ".png");
        if (await cli.Snapshot(design, png))
            await api.Upload($"/items/{itemId}/attachments", png, baseName + ".png", new Dictionary<string, string> { ["role"] = "image" });

        if (await cli.MassKg(design) is double kg)
        {
            try { await api.Send(HttpMethod.Patch, $"/items/{itemId}/properties", new Dictionary<string, string> { ["mass"] = MassText(kg) }); }
            catch (CompanionException e) { Console.Error.WriteLine($"mass not saved: {e.Message}"); }
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["itemId"] = itemId,
            ["recordName"] = record,
            ["revision"] = revision,
            ["message"] = $"uploaded as {baseName}.sc",
        };
    }

    // The same two-step dance as the macros' submit_via_browser: offer the form to a tab this run
    // already opened, otherwise open one through the one-time login bridge, then wait for the
    // browser to create the item or attach the upload to an existing one.
    async Task<JsonNode> ChooseInBrowser(string name)
    {
        var ticket = Guid.NewGuid().ToString();
        var taken = false;
        if (_tabOpened)
        {
            await api.Send(HttpMethod.Put, "/cad-requests", new { runId = _runId, ticket, mode = "create", name });
            for (var i = 0; i < 15 && !taken; i++)
            {
                await Task.Delay(400);
                var (_, probe) = await api.GetRaw($"/cad-requests/taken?ticket={Uri.EscapeDataString(ticket)}");
                taken = probe?["taken"]?.GetValue<int>() == 1;
            }
            if (!taken)
                try { await api.Send(HttpMethod.Delete, $"/cad-requests?ticket={Uri.EscapeDataString(ticket)}", null); } catch { }
        }
        if (!taken)
        {
            var deepLink = $"/?ticket={Uri.EscapeDataString(ticket)}&name={Uri.EscapeDataString(name)}&runId={_runId}";
            var login = await api.Send(HttpMethod.Post, "/auth/browser-bridge-ticket", new { });
            var url = $"{api.Base}/auth/browser-login?ticket={Uri.EscapeDataString(login!["ticket"]!.ToString())}&redirect={Uri.EscapeDataString(deepLink)}";
            if (openBrowser) OpenBrowser(url);
            else Console.WriteLine($"  open in the browser: {url}");
            _tabOpened = true;
        }
        Console.WriteLine("  waiting for the item to be chosen in the browser…");
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            var (code, data) = await api.GetRaw($"/create-tickets/{ticket}");
            if (code == HttpStatusCode.OK && data is not null) return data;
            await Task.Delay(1000);
        }
        throw new CompanionException("nobody chose the item in the browser within 10 minutes");
    }

    static void OpenBrowser(string url)
    {
        var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo(url) { UseShellExecute = true }
            : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", url) : new ProcessStartInfo("xdg-open", url);
        Process.Start(psi);
    }

    static string Sanitize(string name)
    {
        var cleaned = Regex.Replace(name, @"[\\/:*?""<>|]+", "_").Trim();
        return cleaned.Length > 0 ? cleaned : "unnamed";
    }

    // As the macros write it: kg with a dot, four decimals, three significant digits for tiny parts.
    static string MassText(double kg)
    {
        var text = kg.ToString("F4", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        if (text is "" or "0")
            text = decimal.Parse(kg.ToString("G3", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        return text;
    }
}

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
                case "setup-solidcraft": return await Setup.SolidCraftPlugin(opts);
                case "autostart": return Setup.Autostart(opts);
                case "logout": return await Commands.Logout();
                case "status": return await Commands.Status();
                case "process": return await Commands.Process(opts, watch: false);
                case "watch": return await Commands.Process(opts, watch: true);
                case "download": return await Commands.Download(opts);
                default:
                    Console.WriteLine("""
                        easypdm-companion login --server http://host:port   approve the sign-in in the browser
                        easypdm-companion login --server URL --password [--user NAME]   sign in with the password instead
                        easypdm-companion logout
                        easypdm-companion status
                        easypdm-companion process [--outbox DIR] [--no-browser]   upload what is waiting, then exit
                        easypdm-companion watch [--outbox DIR]     keep uploading what arrives
                        easypdm-companion download [--no-browser]  pick an item in the browser, open its design
                        easypdm-companion setup-solidcraft [--plugin easypdm.zip]   the SolidCraft plugin, set up for this computer
                        easypdm-companion autostart [--off]          start `watch` when you log in
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

static class Browser
{
    public static void Open(string url)
    {
        var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo(url) { UseShellExecute = true }
            : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", url) : new ProcessStartInfo("xdg-open", url);
        Process.Start(psi);
    }
}

sealed class Options
{
    public Dictionary<string, string> Values { get; } = new();
    public string? this[string key] => Values.TryGetValue(key, out var v) ? v : null;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
            if (args[i] is "--no-browser" or "--password" or "--off" or "--background")
                o.Values[args[i][2..]] = "1";
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

    public async Task<(HttpStatusCode, JsonNode?)> PostRaw(string path, object body)
    {
        var r = await _http.PostAsJsonAsync(Base + path, body);
        var text = await r.Content.ReadAsStringAsync();
        JsonNode? node = null;
        try { node = text.Length > 0 ? JsonNode.Parse(text) : null; } catch (JsonException) { }
        return (r.StatusCode, node);
    }

    public async Task<JsonNode?> Send(HttpMethod method, string path, object? body)
    {
        var req = new HttpRequestMessage(method, Base + path);
        if (body is not null) req.Content = JsonContent.Create(body);
        var text = await (await Check(await _http.SendAsync(req), $"{method} {path}")).Content.ReadAsStringAsync();
        return text.Length > 0 ? JsonNode.Parse(text) : null;
    }

    public async Task DownloadTo(string path, string target)
    {
        var r = await Check(await _http.GetAsync(Base + path, HttpCompletionOption.ResponseHeadersRead), "GET " + path);
        var temp = target + ".part";
        await using (var f = File.Create(temp))
            await r.Content.CopyToAsync(f);
        File.Move(temp, target, overwrite: true);
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
        if (!o.Values.ContainsKey("password"))
        {
            Console.WriteLine(await DeviceLogin(server, openBrowser: !o.Values.ContainsKey("no-browser")));
            return 0;
        }
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

    // Sign in by approval in the browser (DeviceLoginEndpoints on the server): the person allows
    // this program on the EasyPDM page where they are logged in, and the program receives its
    // own macro token. The password never passes through this program or the outbox.
    public static async Task<string> DeviceLogin(string server, bool openBrowser)
    {
        var api = new Api(server, null);
        var start = await api.Send(HttpMethod.Post, "/auth/device/start", new { client = "EasyPDM companion" })
            ?? throw new CompanionException("the server did not start a sign-in (is it older than 0.9?)");
        var deviceCode = start["deviceCode"]!.ToString();
        var userCode = start["userCode"]!.ToString();
        var url = server.TrimEnd('/') + start["verificationPath"]!.ToString();
        Console.WriteLine($"Approve the sign-in in the browser. Code: {userCode}");
        if (openBrowser) Browser.Open(url);
        else Console.WriteLine($"  open in the browser: {url}");
        var deadline = DateTime.UtcNow.AddSeconds(start["expiresIn"]?.GetValue<int>() ?? 600);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000);
            var (code, body) = await api.PostRaw("/auth/device/token", new { deviceCode });
            if (code == HttpStatusCode.Accepted) continue;
            if (code != HttpStatusCode.OK)
                throw new CompanionException(body?["detail"]?.ToString() ?? $"sign-in failed ({(int)code})");
            var c = Config.Load();
            c.Server = server;
            c.Token = body!["sessionToken"]!.ToString();
            c.DisplayName = body["displayName"]?.ToString() ?? body["username"]?.ToString();
            c.Save();
            return $"Logged in to {server} as {c.DisplayName}.";
        }
        throw new CompanionException("the sign-in was not approved within the time limit");
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
        var background = o.Values.ContainsKey("background");
        if (background) Background.Start();
        var uploader = new Uploader(Api.FromConfig, new SolidCraftCli(o["solidcraft-cli"]), openBrowser: o.Values.ContainsKey("no-browser") is false)
        {
            Notify = background ? Background.Notify : null,
        };
        Console.WriteLine($"EasyPDM companion: outbox {outbox}");
        do
        {
            foreach (var request in Outbox.Pending(outbox))
                await uploader.Handle(outbox, request);
            if (watch) await Task.Delay(TimeSpan.FromSeconds(2));
        } while (watch);
        return 0;
    }

    public static async Task<int> Download(Options o)
    {
        var r = await new Uploader(Api.FromConfig, new SolidCraftCli(o["solidcraft-cli"]), openBrowser: !o.Values.ContainsKey("no-browser")).Download();
        Console.WriteLine(r["message"]);
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

    // Writes the item link into a downloaded design, so its next upload goes to the same item
    // without the browser: the same metadata key and shape the plugin keeps (see its META_KEY).
    public async Task<bool> SetLink(string design, JsonObject link)
    {
        var script = design + ".easypdm-link.json";
        var values = new JsonObject { ["io.github.pawelcel.easypdm"] = link.DeepClone() };
        if (link["recordName"] is JsonNode r) values["part_number"] = r.DeepClone();
        if (link["revision"] is JsonNode v) values["revision"] = v.DeepClone();
        await File.WriteAllTextAsync(script, new JsonArray(new JsonObject
        {
            ["command"] = "document.metadata.set", ["params"] = new JsonObject { ["values"] = values },
        }).ToJsonString());
        try
        {
            var (code, _) = await Run("run", script, "--in", design, "--save", design, "--quiet");
            // Saving over a design makes SolidCraft keep the previous file as .bak; this save is
            // ours and changes only the link, so the backup is noise in the user's folder.
            if (code == 0 && File.Exists(design + ".bak")) File.Delete(design + ".bak");
            return code == 0;
        }
        catch (Exception e) { Console.Error.WriteLine($"  link not written into the design: {e.Message}"); return false; }
        finally { File.Delete(script); }
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

// The running SolidCraft app's control channel (docs/control-protocol.md in SolidCraft): TCP on
// 127.0.0.1, the port in control/current.port and a per-launch token in control/<port>.token,
// both in the app's data folder. Only on when the user enabled Preferences > Assistant > "Let AI
// agents connect" (or started SolidCraft with --control); otherwise downloads are only saved.
static class SolidCraftApp
{
    static IEnumerable<string> ControlDirs()
    {
        if (Environment.GetEnvironmentVariable("SOLIDCRAFT_CONTROL_DIR") is { Length: > 0 } d) yield return d;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolidCraft", "control");
            yield break;
        }
        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "SolidCraft", "control");
            yield break;
        }
        yield return Path.Combine(home, ".var", "app", "io.tangencylabs.solidcraft", "data", "solidcraft", "control");
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share");
        yield return Path.Combine(data, "solidcraft", "control");
    }

    public static async Task<string?> Open(string path)
    {
        foreach (var dir in ControlDirs())
        {
            var portFile = Path.Combine(dir, "current.port");
            if (!File.Exists(portFile)) continue;
            var port = (await File.ReadAllTextAsync(portFile)).Trim();
            var tokenFile = Path.Combine(dir, port + ".token");
            if (!int.TryParse(port, out var p) || !File.Exists(tokenFile)) continue;
            var token = (await File.ReadAllTextAsync(tokenFile)).Trim();
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, p).WaitAsync(TimeSpan.FromSeconds(3));
                await using var stream = tcp.GetStream();
                var line = new JsonObject
                {
                    ["id"] = 1, ["method"] = "engine.execute", ["token"] = token,
                    ["params"] = new JsonObject { ["command"] = "doc.open", ["params"] = new JsonObject { ["path"] = path } },
                }.ToJsonString() + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(line));
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var reply = JsonNode.Parse(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)) ?? "{}");
                if (reply?["ok"]?.GetValue<bool>() == true) return null;
                return reply?["error"]?.ToString() ?? "SolidCraft did not open it";
            }
            catch (Exception e) { return $"SolidCraft is not answering ({e.Message})"; }
        }
        return "SolidCraft is not running with its control channel on (Preferences > Assistant > Let AI agents connect)";
    }
}

sealed class Uploader(Func<Api> apiFactory, SolidCraftCli cli, bool openBrowser)
{
    // Read from the config on each request: a login or logout from the SolidCraft menu changes it
    // while `watch` runs.
    Api api = null!;

    // Running in the background nobody sees the console, so each outcome is also shown as a
    // desktop notification ("the item is released: add a comment" must reach the person).
    public Action<bool, string>? Notify { get; init; }
    // One per run of this program, like the macros' runId: an EasyPDM tab this run opened takes
    // the next request itself instead of a new tab opening for every upload.
    readonly string _runId = Guid.NewGuid().ToString("N");
    bool _tabOpened;

    public async Task Handle(string outbox, string requestPath)
    {
        var request = JsonNode.Parse(await File.ReadAllTextAsync(requestPath))!;
        var id = request["id"]?.ToString() ?? Path.GetFileName(requestPath).Split('.')[0];
        var kind = request["kind"]?.ToString() ?? "upload";
        Console.WriteLine($"[{id}] {kind}");
        JsonObject result;
        try
        {
            result = kind switch
            {
                "upload" => await Upload(outbox, request),
                "download" => await Download(),
                "login" => await Login(request),
                "logout" => await Logout(),
                _ => throw new CompanionException($"unknown request kind {kind}"),
            };
            Console.WriteLine($"[{id}] {result["message"]}");
            Notify?.Invoke(true, result["message"]?.ToString() ?? "");
        }
        catch (CompanionException e)
        {
            Console.Error.WriteLine($"[{id}] failed: {e.Message}");
            Notify?.Invoke(false, e.Message);
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
        api = apiFactory();
        var design = Path.Combine(outbox, request["design"]?.ToString() ?? throw new CompanionException("the request names no design"));
        if (!File.Exists(design)) throw new CompanionException($"{design} is missing");
        var step = request["step"]?.ToString() is { Length: > 0 } s ? Path.Combine(outbox, s) : null;
        var comment = request["comment"]?.ToString();
        var props = request["properties"];
        var name = props?["description"]?.ToString() is { Length: > 0 } d ? d
            : props?["part_number"]?.ToString() is { Length: > 0 } pn ? pn : "SolidCraft design";

        // Which item: the design's own link; the link file an earlier upload of the same design
        // left (when that upload went through after the plugin last ran, so the design does not
        // carry the item yet); or the browser.
        var designId = request["easypdm"]?["designId"]?.ToString();
        var linkFile = designId is { Length: > 0 } && !designId.Contains('/') && !designId.Contains('\\')
            ? Path.Combine(outbox, designId + ".link.json") : null;
        var itemId = request["easypdm"]?["itemId"]?.ToString();
        if (itemId is null && linkFile is not null && File.Exists(linkFile))
            itemId = JsonNode.Parse(await File.ReadAllTextAsync(linkFile))?["itemId"]?.ToString();
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

        // The plugin reads this back into the design on its next upload (see the plugin's
        // link_file): the design learns which item it is without the user doing anything.
        if (linkFile is not null)
            await File.WriteAllTextAsync(linkFile, new JsonObject
            {
                ["designId"] = designId, ["itemId"] = itemId, ["recordName"] = record, ["revision"] = revision,
            }.ToJsonString());

        return new JsonObject
        {
            ["ok"] = true,
            ["itemId"] = itemId,
            ["recordName"] = record,
            ["revision"] = revision,
            ["message"] = $"uploaded as {baseName}.sc",
        };
    }

    // Download: the item is picked in the browser (as the FreeCAD Download macro does, mode
    // "download"), its current revision's .sc goes into Documents/EasyPDM/designs -- the
    // SolidCraft Flatpak sees only the Documents folder -- with the item link written into it,
    // and the running SolidCraft opens it.
    async Task<JsonObject> Login(JsonNode request)
    {
        var server = request["server"]?.ToString() is { Length: > 0 } s ? s.Trim()
            : Config.Load().Server ?? throw new CompanionException("give the EasyPDM server address");
        var message = await Commands.DeviceLogin(server, openBrowser);
        return new JsonObject { ["ok"] = true, ["message"] = message };
    }

    static async Task<JsonObject> Logout()
    {
        await Commands.Logout();
        return new JsonObject { ["ok"] = true, ["message"] = "logged out" };
    }

    public async Task<JsonObject> Download()
    {
        api = apiFactory();
        var picked = await ChooseInBrowser("", mode: "download");
        var itemId = picked["itemId"]!.ToString();
        var item = await api.Get($"/items/{itemId}") ?? throw new CompanionException("item not found");
        var atts = (await api.Get($"/items/{itemId}/attachments"))?.AsArray() ?? [];
        var designs = atts.Where(a => a?["role"]?.ToString() is null or "cad"
                && (a?["fileName"]?.ToString() ?? "").EndsWith(".sc", StringComparison.OrdinalIgnoreCase)).ToList();
        var rev = item["revisionNumber"]?.GetValue<int>();
        var current = designs.FirstOrDefault(a => a?["revisionNumber"]?.GetValue<int?>() == rev) ?? designs.LastOrDefault()
            ?? throw new CompanionException($"{item["recordName"]} has no SolidCraft design (.sc) to download");

        var folder = Path.Combine(Path.GetDirectoryName(Outbox.DefaultPath())!, "designs");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(current!["fileName"]!.ToString()));
        await api.DownloadTo($"/attachments/{current["id"]}/file", target);

        var record = item["recordName"]?.ToString() ?? "";
        var revision = item["revisionLabel"]?.ToString() ?? "A";
        await cli.SetLink(target, new JsonObject
        {
            ["designId"] = "i" + itemId, ["itemId"] = itemId, ["recordName"] = record, ["revision"] = revision,
        });
        var openError = await SolidCraftApp.Open(target);
        return new JsonObject
        {
            ["ok"] = true, ["itemId"] = itemId, ["recordName"] = record, ["revision"] = revision, ["path"] = target,
            ["message"] = openError is null ? $"opened {Path.GetFileName(target)} in SolidCraft" : $"saved {target}; {openError}",
        };
    }

    // The same two-step dance as the macros' submit_via_browser: offer the form to a tab this run
    // already opened, otherwise open one through the one-time login bridge, then wait for the
    // browser to create the item or attach the upload to an existing one.
    async Task<JsonNode> ChooseInBrowser(string name, string mode = "create")
    {
        var ticket = Guid.NewGuid().ToString();
        var taken = false;
        if (_tabOpened)
        {
            await api.Send(HttpMethod.Put, "/cad-requests", new { runId = _runId, ticket, mode, name });
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
            var deepLink = mode == "download"
                ? $"/?ticket={Uri.EscapeDataString(ticket)}&mode=download&runId={_runId}"
                : $"/?ticket={Uri.EscapeDataString(ticket)}&name={Uri.EscapeDataString(name)}&runId={_runId}";
            var login = await api.Send(HttpMethod.Post, "/auth/browser-bridge-ticket", new { });
            var url = $"{api.Base}/auth/browser-login?ticket={Uri.EscapeDataString(login!["ticket"]!.ToString())}&redirect={Uri.EscapeDataString(deepLink)}";
            if (openBrowser) Browser.Open(url);
            else Console.WriteLine($"  open in the browser: {url}");
            _tabOpened = true;
        }
        Console.WriteLine("  waiting for the item to be chosen in the browser…");
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            var (code, data) = await api.GetRaw($"/create-tickets/{ticket}");
            if (code == HttpStatusCode.OK && data?["itemId"] is not null) return data;
            await Task.Delay(1000);
        }
        throw new CompanionException("nobody chose the item in the browser within 10 minutes");
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

// One-time set-up on a CAD workstation.
static class Setup
{
    // The SolidCraft plugin with this computer's outbox written into its upload form. SolidCraft's
    // file commands take absolute paths inside the granted folder only (a relative one is refused),
    // and a plugin cannot learn its grant's path, so the path goes into the package: as the only
    // value of the form's outbox field, which SolidCraft draws as a fixed choice -- nothing to type
    // and nothing to get wrong.
    public static async Task<int> SolidCraftPlugin(Options o)
    {
        var source = o["plugin"] ?? Path.Combine(AppContext.BaseDirectory, "easypdm-solidcraft-plugin.zip");
        if (!File.Exists(source))
            throw new CompanionException($"{source} not found: give the plugin package with --plugin");
        var outbox = o["outbox"] ?? Outbox.DefaultPath();
        Directory.CreateDirectory(outbox);
        var target = Path.Combine(Path.GetDirectoryName(outbox)!, "easypdm-solidcraft.zip");

        using (var input = System.IO.Compression.ZipFile.OpenRead(source))
        using (var output = System.IO.Compression.ZipFile.Open(target + ".part", System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var entry in input.Entries)
            {
                var copy = output.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                await using var to = copy.Open();
                await using var from = entry.Open();
                if (Path.GetFileName(entry.FullName) != "plugin.json")
                {
                    await from.CopyToAsync(to);
                    continue;
                }
                var manifest = JsonNode.Parse(from)!;
                foreach (var e in manifest["runtime"]!["entries"]!.AsArray())
                    if (e?["input_schema"]?["properties"]?["outbox_path"] is JsonObject field)
                    {
                        field["enum"] = new JsonArray(outbox);
                        field["default"] = outbox;
                    }
                await to.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
            }
        }
        File.Move(target + ".part", target, overwrite: true);
        Console.WriteLine($"""
            SolidCraft plugin ready: {target}

            In SolidCraft: Preferences > Plugins > Install plugin…, pick that file and allow:
              - the "file" command group;
              - the "outbox" folder: {outbox} (read and write).
            Then File > EasyPDM: Log in, and keep `easypdm-companion watch` running
            (`easypdm-companion autostart` starts it when you log in).
            """);
        return 0;
    }

    // Starts `easypdm-companion watch` at login: a desktop entry in ~/.config/autostart on Linux,
    // a .cmd in the Startup folder on Windows. --off removes it.
    public static int Autostart(Options o)
    {
        var exe = Environment.ProcessPath ?? throw new CompanionException("cannot tell where this program is");
        var dll = Path.Combine(AppContext.BaseDirectory, "easypdm-companion.dll");
        var command = (Path.GetFileNameWithoutExtension(exe) is "dotnet" ? $"\"{exe}\" \"{dll}\" watch" : $"\"{exe}\" watch") + " --background";
        if (OperatingSystem.IsWindows())
        {
            // The per-user Run key starts the program directly, without a .cmd window; in the
            // background it then lets go of its own console (Background.Start).
            using var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (o.Values.ContainsKey("off"))
            {
                run.DeleteValue("EasyPDM companion", false);
                Console.WriteLine("Autostart removed.");
            }
            else
            {
                run.SetValue("EasyPDM companion", command);
                Console.WriteLine("`easypdm-companion watch` starts in the background when you log in.");
            }
            return 0;
        }
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        var file = Path.Combine(config, "autostart", "easypdm-companion.desktop");
        var content = $"[Desktop Entry]\nType=Application\nName=EasyPDM companion\nComment=Uploads and downloads for CAD programs\nExec={command}\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        if (o.Values.ContainsKey("off"))
        {
            if (File.Exists(file)) File.Delete(file);
            Console.WriteLine($"Autostart removed ({file}).");
            return 0;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        Console.WriteLine($"`easypdm-companion watch` starts in the background when you log in ({file}).");
        return 0;
    }
}

// `watch --background`: what autostart runs. No console to look at, so output goes to a log
// file and each outcome to a desktop notification.
static class Background
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool FreeConsole();

    public static string LogPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyPDM", "companion.log")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"), "EasyPDM", "companion.log");

    public static void Start()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        // A fresh log each start, so it never grows without bound.
        var log = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var stamped = TextWriter.Synchronized(new StampedWriter(log));
        Console.SetOut(stamped);
        Console.SetError(stamped);
        if (OperatingSystem.IsWindows()) FreeConsole();
    }

    public static void Notify(bool ok, string message)
    {
        var title = ok ? "EasyPDM" : "EasyPDM: not done";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // A balloon from a short-lived tray icon: needs nothing beyond Windows itself.
                var script = "Add-Type -AssemblyName System.Windows.Forms; $n = New-Object System.Windows.Forms.NotifyIcon; "
                    + "$n.Icon = [System.Drawing.SystemIcons]::Information; $n.Visible = $true; "
                    + $"$n.ShowBalloonTip(8000, '{title.Replace("'", "''")}', '{message.Replace("'", "''")}', "
                    + (ok ? "'Info'" : "'Warning'") + "); Start-Sleep -Seconds 9; $n.Dispose()";
                var psi = new ProcessStartInfo("powershell", "-NoProfile -WindowStyle Hidden -Command -") { RedirectStandardInput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                p?.StandardInput.Write(script);
                p?.StandardInput.Close();
            }
            else
            {
                var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false };
                psi.ArgumentList.Add("--app-name=EasyPDM");
                if (!ok) psi.ArgumentList.Add("--urgency=critical");
                psi.ArgumentList.Add(title);
                psi.ArgumentList.Add(message);
                Process.Start(psi);
            }
        }
        catch { /* no notifier on this desktop: the log still has it */ }
    }

    sealed class StampedWriter(TextWriter inner) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;
        public override void Write(char value) => inner.Write(value);
        public override void WriteLine(string? value) => inner.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {value}");
    }
}

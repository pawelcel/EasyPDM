using System.Collections.Concurrent;
using System.Security.Cryptography;
using Npgsql;

// Signing a program in by approving it in the browser -- the "device login" of TVs and command
// line tools -- so a CAD integration never has to ask for, or store, the password.
//
//   1. The program (the EasyPDM companion today; the FreeCAD and SolidWorks add-ins next) calls
//      POST /api/auth/device/start { client } without being logged in and gets a secret device
//      code plus a short user code, and opens the browser on "/?deviceLogin=<user code>".
//   2. The web app, where the person is logged in already (or logs in first), shows which
//      program asks and the code, and "Allow" calls POST /api/auth/device/{userCode}/approve.
//      That issues an ordinary macro token for THAT person -- listed and revocable in
//      Settings -> Users like any macro login -- and parks it with the request.
//   3. The program polls POST /api/auth/device/token { deviceCode } and receives the token once.
//
// The device code is 256 random bits and never leaves the program and the server, so nobody can
// collect a token they did not start. The user code only identifies the request to approve, and
// approving needs a logged-in person; it shows the program's name so a request someone else
// started is recognisable as such. Requests live in memory for ten minutes (the server is one
// process, like CreateTicketStore), and at most MaxPending exist at once, since starting one
// needs no login.
static class DeviceLoginEndpoints
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const int MaxPending = 500;
    // No 0/O or 1/I: the code is read off a screen and may be typed.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private sealed class Request
    {
        public required string DeviceCode { get; init; }
        public required string UserCode { get; init; }
        public required string Client { get; init; }
        public required DateTime CreatedAt { get; init; }
        public string? Token { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public bool Denied { get; set; }
    }

    private static readonly ConcurrentDictionary<string, Request> ByUserCode = new();

    private static void Sweep()
    {
        foreach (var (code, r) in ByUserCode)
            if (DateTime.UtcNow - r.CreatedAt > Lifetime)
                ByUserCode.TryRemove(code, out _);
    }

    private static string NewUserCode()
    {
        Span<char> c = stackalloc char[9];
        for (var i = 0; i < 9; i++)
            c[i] = i == 4 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(c);
    }

    private static string Normalize(string userCode) => userCode.Trim().ToUpperInvariant();

    // Paths that work without a login (the auth middleware in Program.cs exempts them).
    public static bool IsAnonymous(PathString path) =>
        path.StartsWithSegments("/api/auth/device/start") || path.StartsWithSegments("/api/auth/device/token");

    public static void MapDeviceLoginEndpoints(this WebApplication app, string connectionString)
    {
        app.MapPost("/api/auth/device/start", (DeviceStartRequest? body) =>
        {
            Sweep();
            if (ByUserCode.Count >= MaxPending)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            var client = string.IsNullOrWhiteSpace(body?.Client) ? "CAD program" : body!.Client!.Trim();
            if (client.Length > 100) client = client[..100];
            Request r;
            do
            {
                r = new Request
                {
                    DeviceCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                    UserCode = NewUserCode(),
                    Client = client,
                    CreatedAt = DateTime.UtcNow,
                };
            } while (!ByUserCode.TryAdd(r.UserCode, r));
            return Results.Ok(new
            {
                deviceCode = r.DeviceCode,
                userCode = r.UserCode,
                verificationPath = $"/?deviceLogin={r.UserCode}",
                expiresIn = (int)Lifetime.TotalSeconds,
                interval = 2,
            });
        });

        app.MapPost("/api/auth/device/token", (DeviceTokenRequest body) =>
        {
            Sweep();
            var r = ByUserCode.Values.FirstOrDefault(x =>
                CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(x.DeviceCode),
                    System.Text.Encoding.ASCII.GetBytes(body.DeviceCode ?? "")));
            if (r is null)
                return Results.Problem("This sign-in request has expired. Start again.", statusCode: StatusCodes.Status410Gone);
            if (r.Denied)
            {
                ByUserCode.TryRemove(r.UserCode, out _);
                return Results.Problem("The sign-in was refused in the browser.", statusCode: StatusCodes.Status403Forbidden);
            }
            if (r.Token is null)
                return Results.Accepted(value: new { status = "pending" });
            // Handed out once.
            ByUserCode.TryRemove(r.UserCode, out _);
            return Results.Ok(new { sessionToken = r.Token, username = r.Username, displayName = r.DisplayName });
        });

        app.MapGet("/api/auth/device/{userCode}", (string userCode) =>
        {
            Sweep();
            if (!ByUserCode.TryGetValue(Normalize(userCode), out var r) || r.Token is not null || r.Denied)
                return Results.NotFound("This sign-in request has expired or was already answered.");
            return Results.Ok(new { userCode = r.UserCode, client = r.Client, createdAt = r.CreatedAt });
        });

        app.MapPost("/api/auth/device/{userCode}/approve", async (string userCode, HttpContext ctx) =>
        {
            Sweep();
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ByUserCode.TryGetValue(Normalize(userCode), out var r) || r.Token is not null || r.Denied)
                return Results.NotFound("This sign-in request has expired or was already answered.");
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var (token, _) = await AuthEndpoints.IssueMacroTokenAsync(conn, user.Id, r.Client);
            r.Username = user.Username;
            r.DisplayName = user.DisplayName;
            r.Token = token;
            return Results.Ok(new { client = r.Client });
        });

        app.MapPost("/api/auth/device/{userCode}/deny", (string userCode) =>
        {
            if (ByUserCode.TryGetValue(Normalize(userCode), out var r) && r.Token is null)
                r.Denied = true;
            return Results.Ok();
        });
    }
}

record DeviceStartRequest(string? Client);
record DeviceTokenRequest(string? DeviceCode);

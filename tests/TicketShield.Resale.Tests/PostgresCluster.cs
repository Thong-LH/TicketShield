using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Npgsql;

namespace TicketShield.Resale.Tests;

// A disposable native PostgreSQL cluster. Never connects to or resets a developer database.
public sealed class PostgresCluster : IAsyncDisposable
{
    private readonly string bin = ResolvePostgresBin();
    private readonly string root = Path.Combine(Path.GetTempPath(), "ticketshield-resale-test-" + Guid.NewGuid().ToString("N"));

    private static string ResolvePostgresBin()
    {
        var env = Environment.GetEnvironmentVariable("TICKETSHIELD_TEST_POSTGRES_BIN");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

        var candidates = new[]
        {
            @"C:\Program Files\PostgreSQL\18\bin",
            @"C:\Program Files\PostgreSQL\17\bin",
            @"C:\Program Files\PostgreSQL\16\bin",
            @"C:\Program Files\PostgreSQL\15\bin"
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, OperatingSystem.IsWindows() ? "initdb.exe" : "initdb")))
                return dir;
        }

        return @"C:\Program Files\PostgreSQL\18\bin";
    }
    private bool started;
    public string CoreConnection { get; private set; } = "";
    public string MockConnection { get; private set; } = "";
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
    private async Task Run(string tool, params string[] arguments)
    {
        var capture = tool != "pg_ctl";
        var info = new ProcessStartInfo(Path.Combine(bin, tool + (OperatingSystem.IsWindows() ? ".exe" : ""))) {
            RedirectStandardOutput = capture, RedirectStandardError = capture, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var a in arguments) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start PostgreSQL test tool.");
        var stdout = capture ? process.StandardOutput.ReadToEndAsync() : Task.FromResult("");
        var stderr = capture ? process.StandardError.ReadToEndAsync() : Task.FromResult("");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        await stdout; var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException($"{tool} failed: {error}");
    }
    public async Task Start()
    {
        Directory.CreateDirectory(root);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var passwordFile = Path.Combine(root, "init-password");
        await File.WriteAllTextAsync(passwordFile, password);
        try { await Run("initdb", "-D", Path.Combine(root, "data"), "-U", "test_admin", "--auth-host=scram-sha-256", "--auth-local=scram-sha-256", "--pwfile=" + passwordFile, "--encoding=UTF8", "--locale=C"); }
        finally { File.Delete(passwordFile); }
        var port = FreePort();
        await Run("pg_ctl", "-D", Path.Combine(root, "data"), "-l", Path.Combine(root, "postgres.log"), "-o", $"-h 127.0.0.1 -p {port}", "-w", "start");
        started = true;
        var admin = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = port, Username = "test_admin", Password = password, Database = "postgres", Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString)) {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE ROLE test_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE; CREATE DATABASE core_test OWNER test_app;", connection);
            // CREATE DATABASE cannot run in a transaction / multi-statement implicit transaction.
            cmd.CommandText = "CREATE ROLE test_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE"; await cmd.ExecuteNonQueryAsync();
            // Store a SCRAM verifier, not a raw password in SQL or database logs.
            var salt = RandomNumberGenerator.GetBytes(16);
            var salted = Rfc2898DeriveBytes.Pbkdf2(password, salt, 4096, HashAlgorithmName.SHA256, 32);
            var clientKey = HMACSHA256.HashData(salted, "Client Key"u8);
            var storedKey = SHA256.HashData(clientKey); var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
            var verifier = $"SCRAM-SHA-256$4096:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
            cmd.CommandText = $"ALTER ROLE test_app PASSWORD '{verifier}'"; await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE DATABASE core_test OWNER test_app"; await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE DATABASE organizer_test OWNER test_app"; await cmd.ExecuteNonQueryAsync();
        }
        admin.Username = "test_app"; admin.Database = "core_test"; CoreConnection = admin.ConnectionString;
        admin.Database = "organizer_test"; MockConnection = admin.ConnectionString;
    }
    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (started) await Run("pg_ctl", "-D", Path.Combine(root, "data"), "-m", "fast", "-w", "stop");
        var fullRoot = Path.GetFullPath(root);
        var expected = Path.GetFullPath(Path.GetTempPath()) + (Path.EndsInDirectorySeparator(Path.GetTempPath()) ? "" : Path.DirectorySeparatorChar);
        if (!fullRoot.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(fullRoot).StartsWith("ticketshield-resale-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to clean unexpected test directory.");
        if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, recursive: true);
    }
}

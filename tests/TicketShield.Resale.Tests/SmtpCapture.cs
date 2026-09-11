using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TicketShield.Resale.Tests;

public sealed class SmtpCapture : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private Task? loop;
    public ConcurrentQueue<(string Recipient, string Otp)> Messages { get; } = new();
    public bool Reject { get; set; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public void Start() { listener.Start(); loop = Accept(); }
    private async Task Accept()
    {
        while (!stop.IsCancellationRequested) {
            try {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
                await writer.WriteLineAsync("220 localhost test SMTP");
                string recipient = "";
                while (await reader.ReadLineAsync(stop.Token) is { } line) {
                    if (line.StartsWith("EHLO") || line.StartsWith("HELO")) await writer.WriteLineAsync("250 localhost");
                    else if (line.StartsWith("MAIL FROM")) await writer.WriteLineAsync("250 OK");
                    else if (line.StartsWith("RCPT TO")) { recipient = line; await writer.WriteLineAsync(Reject ? "550 rejected" : "250 OK"); }
                    else if (line == "DATA") {
                        await writer.WriteLineAsync("354 send data"); var body = new StringBuilder();
                        while (await reader.ReadLineAsync(stop.Token) is { } data && data != ".") body.AppendLine(data);
                        var raw = body.ToString();
                        var textToSearch = raw;
                        if (raw.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)) {
                            var split = raw.Split(new[] { "\r\n\r\n", "\n\n" }, 2, StringSplitOptions.None);
                            if (split.Length == 2) {
                                try {
                                    var base64Clean = Regex.Replace(split[1], @"\s+", "");
                                    var bytes = Convert.FromBase64String(base64Clean);
                                    textToSearch = Encoding.UTF8.GetString(bytes);
                                } catch { }
                            }
                        }
                        var match = Regex.Match(textToSearch, @">(\d{6})<");
                        if (!match.Success) match = Regex.Match(textToSearch, @"\bis (\d{6})\b");
                        if (!match.Success) match = Regex.Match(textToSearch, @"\b(\d{6})\b");
                        if (!match.Success) {
                            foreach (Match m in Regex.Matches(raw, @"=\?utf-8\?B\?([^\?]+)\?=", RegexOptions.IgnoreCase)) {
                                try {
                                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value));
                                    var subMatch = Regex.Match(decoded, @"\b(\d{6})\b");
                                    if (subMatch.Success) { match = subMatch; break; }
                                } catch { }
                            }
                        }
                        if (match.Success) Messages.Enqueue((recipient, match.Groups[1].Value));
                        await writer.WriteLineAsync("250 accepted");
                    } else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                    else await writer.WriteLineAsync("250 OK");
                }
            } catch (OperationCanceledException) { break; }
            catch (IOException) { }
        }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); if (loop is not null) await loop; stop.Dispose(); }
}

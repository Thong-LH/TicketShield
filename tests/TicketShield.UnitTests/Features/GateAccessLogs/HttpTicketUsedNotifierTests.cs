using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockOrganizer.API.Gate;

namespace TicketShield.UnitTests.Features.GateAccessLogs;

public class HttpTicketUsedNotifierTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public RecordingHandler(HttpStatusCode status) => _status = status;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status);
        }
    }

    [Fact]
    public async Task Notify_SendsTicketCodeAndSecretToWebhook()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);

        await Notifier(handler, ("CoreWebhook:Secret", "webhook-secret")).NotifyAsync("ATSH-GA-999", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "http://core.test/api/v1/webhooks/organizer/ticket-used",
            handler.Request.RequestUri!.ToString());
        Assert.Equal("webhook-secret", Assert.Single(handler.Request.Headers.GetValues("X-Organizer-Secret")));
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("ATSH-GA-999", body.RootElement.GetProperty("ticketCode").GetString());
    }

    [Fact]
    public async Task Notify_WhenNoWebhookSecretIsSet_UsesOrganizerApiKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);

        await Notifier(handler, ("OrganizerResale:ApiKey", "shared-key")).NotifyAsync("ATSH-GA-999", CancellationToken.None);

        Assert.Equal("shared-key", Assert.Single(handler.Request!.Headers.GetValues("X-Organizer-Secret")));
    }

    [Fact]
    public async Task Notify_WhenTicketHasNoEscrow_DoesNotThrow()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);

        await Notifier(handler).NotifyAsync("FIRST-HAND-TICKET", CancellationToken.None);

        Assert.NotNull(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Notify_WhenCoreRejects_Throws(HttpStatusCode status)
    {
        var handler = new RecordingHandler(status);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Notifier(handler).NotifyAsync("ATSH-GA-999", CancellationToken.None));
    }

    private static HttpTicketUsedNotifier Notifier(HttpMessageHandler handler, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://core.test/") };
        return new HttpTicketUsedNotifier(http, configuration, NullLogger<HttpTicketUsedNotifier>.Instance);
    }
}

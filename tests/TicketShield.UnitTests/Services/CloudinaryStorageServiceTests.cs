using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.Services;

public class CloudinaryStorageServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task UploadAsync_WhenSourceUrlEmpty_ThrowsArgumentException(string? url)
    {
        var options = new CloudinaryOptions { CloudName = "test", ApiKey = "key", ApiSecret = "secret" };
        var factory = new Mock<IHttpClientFactory>();
        var service = new CloudinaryStorageService(options, factory.Object, NullLogger<CloudinaryStorageService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(url!, "banner"));
    }

    [Fact]
    public async Task UploadAsync_WhenCredentialsMissing_ReturnsSourceUrlFallbackWithoutCrashing()
    {
        var options = new CloudinaryOptions { CloudName = "", ApiKey = "", ApiSecret = "" };
        var factory = new Mock<IHttpClientFactory>();
        var service = new CloudinaryStorageService(options, factory.Object, NullLogger<CloudinaryStorageService>.Instance);

        const string testUrl = "https://salt.tkbcdn.com/poster.png";
        var result = await service.UploadAsync(testUrl, "banner", "events/demo");

        Assert.Equal(testUrl, result);
    }

    [Fact]
    public async Task UploadAsync_WhenRemoteDirectPullFails_FallsBackToDownloadStreamAndHandlesGracefully()
    {
        var options = new CloudinaryOptions
        {
            CloudName = "mock-cloud",
            ApiKey = "mock-key",
            ApiSecret = "mock-secret",
            RootFolder = "ticketshield"
        };

        var handler = new MockHttpMessageHandler((req) =>
        {
            // Simulate 404 for download stream
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new CloudinaryStorageService(options, factory.Object, NullLogger<CloudinaryStorageService>.Instance);

        const string testUrl = "https://invalid-non-existent-source.example.com/poster.jpg";
        var result = await service.UploadAsync(testUrl, "my-poster", "events/test");

        // Khi toàn bộ remote pull và stream download thất bại, trả về link gốc an toàn
        Assert.Equal(testUrl, result);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}

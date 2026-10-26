using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Services;

public class CloudinaryEvidenceFileStoreTests
{
    private const string Secret = "test-api-secret-0b1c";
    private const string Id = "0123456789abcdef0123456789abcdef";
    private const string ValidUrl =
        "https://res.cloudinary.com/xfxlcgql/image/authenticated/s--AbCd1234--/v1761500000/dispute-evidence/" + Id + ".png";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    [Theory]
    [InlineData(Id + ".png")]
    [InlineData("https://evil.example.com/xfxlcgql/image/authenticated/s--AbCd1234--/v1/dispute-evidence/" + Id + ".png")]
    [InlineData("http://res.cloudinary.com/xfxlcgql/image/authenticated/s--AbCd1234--/v1/dispute-evidence/" + Id + ".png")]
    [InlineData("https://res.cloudinary.com/othercloud/image/authenticated/s--AbCd1234--/v1/dispute-evidence/" + Id + ".png")]
    [InlineData("https://res.cloudinary.com/xfxlcgql/image/upload/s--AbCd1234--/v1/dispute-evidence/" + Id + ".png")]
    [InlineData("https://res.cloudinary.com/xfxlcgql/image/authenticated/v1/dispute-evidence/" + Id + ".png")]
    [InlineData("https://res.cloudinary.com/xfxlcgql/image/authenticated/s--AbCd1234--/v1/other-folder/" + Id + ".png")]
    [InlineData("https://res.cloudinary.com/xfxlcgql/image/authenticated/s--AbCd1234--/v1/dispute-evidence/" + Id + ".gif")]
    [InlineData("https://res.cloudinary.com/xfxlcgql/image/authenticated/s--AbCd1234--/v1/dispute-evidence/" + Id + ".png?x=1")]
    public async Task ReadAsync_WhenUrlIsNotASignedEvidenceUrlOfThisCloud_ReturnsNullWithoutCallingOut(string fileUrl)
    {
        var http = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
        var store = CreateStore(http, out _);

        var content = await store.ReadAsync(fileUrl);

        Assert.Null(content);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(ValidUrl)]
    [InlineData("https://res.cloudinary.com/xfxlcgql/image/authenticated/s--Xy_9-z0Q--/dispute-evidence/" + Id + ".jpg")]
    public async Task ReadAsync_WhenUrlIsValid_DownloadsExactlyThatUrlWithTheEvidenceClient(string fileUrl)
    {
        var http = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
        var store = CreateStore(http, out var factory);

        var content = await store.ReadAsync(fileUrl);

        Assert.Equal(Png, content);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(fileUrl, request.Uri.AbsoluteUri);
        Assert.Equal(["cloudinary-evidence"], factory.Names);
    }

    [Fact]
    public async Task ReadAsync_WhenCloudinarySaysNotFound_ReturnsNull()
    {
        var http = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var store = CreateStore(http, out _);

        Assert.Null(await store.ReadAsync(ValidUrl));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReadAsync_WhenCloudinaryAnswersAnythingElse_ThrowsWithoutLeakingTheUrl(HttpStatusCode status)
    {
        var http = new RecordingHandler((_, _) => new HttpResponseMessage(status));
        var store = CreateStore(http, out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(ValidUrl));

        Assert.DoesNotContain("s--AbCd1234--", ex.Message);
        Assert.DoesNotContain(Secret, ex.Message);
    }

    [Fact]
    public async Task SaveAsync_WhenUploadSucceeds_UploadsPrivatelyIntoTheEvidenceFolderAndReturnsTheSecureUrl()
    {
        var http = new RecordingHandler((request, body) => request.RequestUri!.AbsolutePath.EndsWith("/image/upload")
            ? UploadResponse(FieldValue(body!, "public_id"), "authenticated")
            : new HttpResponseMessage(HttpStatusCode.BadRequest));
        var store = CreateStore(http, out _);

        var secureUrl = await store.SaveAsync(Png, "png");

        var upload = Assert.Single(http.Requests);
        Assert.Equal("https://api.cloudinary.com/v1_1/xfxlcgql/image/upload", upload.Uri.AbsoluteUri);
        Assert.Equal("authenticated", FieldValue(upload.Body!, "type"));
        Assert.Equal("dispute-evidence", FieldValue(upload.Body!, "asset_folder"));
        var publicId = FieldValue(upload.Body!, "public_id");
        Assert.Matches("^dispute-evidence/[0-9a-f]{32}$", publicId);
        Assert.Equal(
            $"https://res.cloudinary.com/xfxlcgql/image/authenticated/s--AbCd1234--/v1761500000/{publicId}.png",
            secureUrl);
    }

    [Fact]
    public async Task SaveAsync_WhenCloudinaryRejectsTheImage_ThrowsAndDeletesNothing()
    {
        var http = new RecordingHandler((_, _) => Json(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid image file"}}"""));
        var store = CreateStore(http, out _);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Png, "png"));

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task SaveAsync_WhenCloudinaryReturnsAPublicUrl_DeletesTheUploadAndThrows()
    {
        var http = new RecordingHandler((request, body) => request.RequestUri!.AbsolutePath.EndsWith("/image/upload")
            ? UploadResponse(FieldValue(body!, "public_id"), "upload")
            : Json(HttpStatusCode.OK, """{"result":"ok"}"""));
        var store = CreateStore(http, out _);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Png, "png"));

        Assert.Equal(2, http.Requests.Count);
        var destroy = http.Requests[1];
        Assert.Equal("https://api.cloudinary.com/v1_1/xfxlcgql/image/destroy", destroy.Uri.AbsoluteUri);
        Assert.Equal(FieldValue(http.Requests[0].Body!, "public_id"), FieldValue(destroy.Body!, "public_id"));
    }

    [Theory]
    [InlineData(Id + ".png")]
    [InlineData("https://res.cloudinary.com/othercloud/image/authenticated/s--AbCd1234--/v1/dispute-evidence/" + Id + ".png")]
    public async Task DeleteAsync_WhenUrlIsNotASignedEvidenceUrlOfThisCloud_DoesNotCallCloudinary(string fileUrl)
    {
        var http = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, """{"result":"ok"}"""));
        var store = CreateStore(http, out _);

        await store.DeleteAsync(fileUrl);

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task DeleteAsync_WhenUrlIsValid_DestroysThatAuthenticatedImage()
    {
        var http = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, """{"result":"ok"}"""));
        var store = CreateStore(http, out _);

        await store.DeleteAsync(ValidUrl);

        var destroy = Assert.Single(http.Requests);
        Assert.Equal("https://api.cloudinary.com/v1_1/xfxlcgql/image/destroy", destroy.Uri.AbsoluteUri);
        Assert.Equal("dispute-evidence/" + Id, FieldValue(destroy.Body!, "public_id"));
        Assert.Equal("authenticated", FieldValue(destroy.Body!, "type"));
        Assert.Equal("true", FieldValue(destroy.Body!, "invalidate"));
    }

    [Fact]
    public async Task DeleteAsync_WhenCloudinaryFails_DoesNotThrow()
    {
        var http = new RecordingHandler((_, _) => throw new HttpRequestException("network down"));
        var store = CreateStore(http, out _);

        await store.DeleteAsync(ValidUrl);

        Assert.Single(http.Requests);
    }

    private static CloudinaryEvidenceFileStore CreateStore(RecordingHandler http, out StubHttpClientFactory factory)
    {
        factory = new StubHttpClientFactory(http);
        return new CloudinaryEvidenceFileStore(
            new CloudinaryOptions { CloudName = "xfxlcgql", ApiKey = "123456789012345", ApiSecret = Secret },
            factory,
            NullLogger<CloudinaryEvidenceFileStore>.Instance);
    }

    // Lấy giá trị một trường trong body multipart hoặc urlencoded mà SDK Cloudinary gửi đi.
    private static string FieldValue(string body, string name)
    {
        var multipart = Regex.Match(body, $"name=\"?{Regex.Escape(name)}\"?\\r?\\n(?:[^\\r\\n]+\\r?\\n)*\\r?\\n(?<v>[^\\r\\n]*)");
        if (multipart.Success)
        {
            return multipart.Groups["v"].Value;
        }

        var encoded = Regex.Match(body, $"(?:^|&){Regex.Escape(name)}=(?<v>[^&]*)");
        return encoded.Success ? Uri.UnescapeDataString(encoded.Groups["v"].Value) : "";
    }

    private static HttpResponseMessage UploadResponse(string publicId, string type)
    {
        var url = $"res.cloudinary.com/xfxlcgql/image/{type}/s--AbCd1234--/v1761500000/{publicId}.png";
        return Json(HttpStatusCode.OK, $$"""
            {
              "asset_id": "3515c6000a548515f1134043f9785c2f",
              "public_id": "{{publicId}}",
              "version": 1761500000,
              "version_id": "7d2cc533bee9ff39f7da7414b61fce7e",
              "signature": "d0b1009e3271a942836c25756ce3e04d205bf754",
              "width": 1,
              "height": 1,
              "format": "png",
              "resource_type": "image",
              "created_at": "2026-10-27T03:00:00Z",
              "tags": [],
              "bytes": 9,
              "type": "{{type}}",
              "etag": "a0c87cd8e6c4ab8f3e20b4f3ec2f7d70",
              "placeholder": false,
              "url": "http://{{url}}",
              "secure_url": "https://{{url}}",
              "asset_folder": "dispute-evidence",
              "display_name": "{{publicId[(publicId.IndexOf('/') + 1)..]}}",
              "original_filename": "evidence"
            }
            """);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!, body));
            return respond(request, body);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> Names { get; } = new();

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }
}

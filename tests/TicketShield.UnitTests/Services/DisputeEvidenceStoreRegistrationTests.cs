using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Infrastructure;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Services;

public class DisputeEvidenceStoreRegistrationTests : IDisposable
{
    private readonly string _diskRoot = Path.Combine(Path.GetTempPath(), "ts-evidence-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddDisputeEvidenceStore_WhenNoCloudinaryKeyIsSet_UsesTheDiskStore()
    {
        var store = Resolve(new Dictionary<string, string?>
        {
            ["Cloudinary:CloudName"] = "",
            ["Cloudinary:ApiKey"] = " ",
            ["Cloudinary:ApiSecret"] = null
        });

        Assert.IsType<DisputeEvidenceFileStore>(store);
    }

    [Fact]
    public void AddDisputeEvidenceStore_WhenAllThreeKeysAreSet_UsesCloudinary()
    {
        var store = Resolve(new Dictionary<string, string?>
        {
            ["Cloudinary:CloudName"] = "xfxlcgql",
            ["Cloudinary:ApiKey"] = "123456789012345",
            ["Cloudinary:ApiSecret"] = "secret"
        });

        Assert.IsType<CloudinaryEvidenceFileStore>(store);
    }

    [Fact]
    public void AddDisputeEvidenceStore_WhenKeysComeFromDopplerInUpperCase_UsesCloudinary()
    {
        // doppler run đặt biến CLOUDINARY__APIKEY, .NET đọc thành khoá CLOUDINARY:APIKEY.
        var store = Resolve(new Dictionary<string, string?>
        {
            ["CLOUDINARY:CLOUDNAME"] = "xfxlcgql",
            ["CLOUDINARY:APIKEY"] = "123456789012345",
            ["CLOUDINARY:APISECRET"] = "secret"
        });

        Assert.IsType<CloudinaryEvidenceFileStore>(store);
    }

    [Fact]
    public void AddDisputeEvidenceStore_WhenOnlySomeKeysAreSet_RefusesToStartAndNamesTheMissingKey()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Cloudinary:CloudName"] = "xfxlcgql",
            ["Cloudinary:ApiKey"] = "123456789012345",
            ["Cloudinary:ApiSecret"] = ""
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddDisputeEvidenceStore(configuration));

        Assert.Contains("Cloudinary:ApiSecret", ex.Message);
        Assert.DoesNotContain("Cloudinary:ApiKey", ex.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diskRoot))
        {
            Directory.Delete(_diskRoot, recursive: true);
        }
    }

    private IDisputeEvidenceFileStore Resolve(Dictionary<string, string?> values)
    {
        values["DisputeEvidence:StoragePath"] = _diskRoot;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDisputeEvidenceStore(Configuration(values));
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDisputeEvidenceFileStore>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

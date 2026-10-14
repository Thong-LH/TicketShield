using System.Collections.Concurrent;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Infrastructure.ExternalServices.Organizer;

/// <summary>
/// Triển khai bộ phân giải kênh gRPC và endpoint động theo từng Ban tổ chức đối tác
/// </summary>
public sealed class OrganizerGatewayResolver : IOrganizerGatewayResolver, IDisposable
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly OrganizerConnectionOptions _defaultOptions;
    private readonly OrganizerResaleService.OrganizerResaleServiceClient _defaultClient;
    private readonly ILogger<OrganizerGatewayResolver> _logger;

    private static readonly ConcurrentDictionary<string, Lazy<(GrpcChannel Channel, OrganizerResaleService.OrganizerResaleServiceClient Client)>> _channelCache = new();

    public OrganizerGatewayResolver(
        ITicketShieldDbContext dbContext,
        OrganizerConnectionOptions defaultOptions,
        OrganizerResaleService.OrganizerResaleServiceClient defaultClient,
        ILogger<OrganizerGatewayResolver> logger)
    {
        _dbContext = dbContext;
        _defaultOptions = defaultOptions;
        _defaultClient = defaultClient;
        _logger = logger;
    }

    public async Task<OrganizerResaleService.OrganizerResaleServiceClient> ResolveClientAsync(
        Guid? organizerId = null,
        CancellationToken ct = default)
    {
        if (!organizerId.HasValue || organizerId.Value == Guid.Empty)
        {
            return _defaultClient;
        }

        try
        {
            var organizer = await _dbContext.Organizers
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == organizerId.Value, ct);

            if (organizer == null || string.IsNullOrWhiteSpace(organizer.GrpcEndpoint))
            {
                return _defaultClient;
            }

            var endpoint = organizer.GrpcEndpoint.Trim();
            if (!IsValidAndSafeEndpoint(endpoint, out var uri))
            {
                _logger.LogWarning("Endpoint gRPC của Ban tổ chức {OrganizerId} không hợp lệ hoặc không an toàn: {Endpoint}. Fallback sang mặc định.", organizerId, endpoint);
                return _defaultClient;
            }

            // Lazy ensures the channel factory is executed only once even under high concurrent load
            var lazyEntry = _channelCache.GetOrAdd(endpoint, ep => new Lazy<(GrpcChannel Channel, OrganizerResaleService.OrganizerResaleServiceClient Client)>(() =>
            {
                _logger.LogInformation("Khởi tạo GrpcChannel động mới tới Ban tổ chức {OrganizerId} tại {Endpoint}", organizerId, ep);
                
                var httpHandler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true,
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
                };

                var channel = GrpcChannel.ForAddress(ep, new GrpcChannelOptions
                {
                    HttpHandler = httpHandler,
                    DisposeHttpClient = true
                });

                var client = new OrganizerResaleService.OrganizerResaleServiceClient(channel);
                return (channel, client);
            }, LazyThreadSafetyMode.ExecutionAndPublication));

            return lazyEntry.Value.Client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi phân giải gRPC Client cho Ban tổ chức {OrganizerId}. Fallback sang mặc định.", organizerId);
            return _defaultClient;
        }
    }

    public void InvalidateChannel(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;

        var key = endpoint.Trim();
        if (_channelCache.TryRemove(key, out var lazyEntry))
        {
            _logger.LogInformation("Vô hiệu hóa gRPC channel cho endpoint {Endpoint}", key);
            if (lazyEntry.IsValueCreated)
            {
                try
                {
                    lazyEntry.Value.Channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi dispose gRPC channel cho {Endpoint}", key);
                }
            }
        }
    }

    private static bool IsValidAndSafeEndpoint(string endpoint, out Uri? uri)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri) || uri == null)
            return false;

        // Bắt buộc scheme http hoặc https
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        // SSRF protection: Chặn các dải IP metadata nguy hiểm (ví dụ AWS/GCP instance metadata 169.254.169.254)
        if (uri.Host.Equals("169.254.169.254", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public async Task<string?> ResolveApiBaseUrlAsync(Guid organizerId, CancellationToken ct = default)
    {
        var organizer = await _dbContext.Organizers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizerId, ct);

        return organizer?.ApiBaseUrl;
    }

    public async Task<string?> ResolveWebhookSecretKeyAsync(Guid organizerId, CancellationToken ct = default)
    {
        var organizer = await _dbContext.Organizers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizerId, ct);

        return organizer?.WebhookSecretKey;
    }

    public void Dispose()
    {
        // Channels in static cache are long-lived and reused across the process lifecycle.
    }
}

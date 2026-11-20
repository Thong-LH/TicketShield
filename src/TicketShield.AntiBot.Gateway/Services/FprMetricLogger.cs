using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

/// <summary>
/// Triển khai dịch vụ nền ghi log cấu trúc JSON Lines phi đồng bộ sử dụng Bounded Channel (Jira SCRUM-382).
/// Đảm bảo không bao giờ chặn (block) luồng request HTTP của khách hàng và tuân thủ SLA < 20ms.
/// </summary>
public class FprMetricLogger : BackgroundService, IFprMetricLogger, IAsyncDisposable
{
    private readonly AntiBotScoringOptions _options;
    private readonly ILogger<FprMetricLogger> _logger;
    private readonly Channel<ShadowEvaluationLogEntry> _channel;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _droppedLogCount;

    public long DroppedLogCount => Interlocked.Read(ref _droppedLogCount);

    public FprMetricLogger(
        IOptions<AntiBotScoringOptions> options,
        ILogger<FprMetricLogger> logger)
    {
        _options = options.Value;
        _logger = logger;

        int capacity = _options.LogChannelCapacity > 0 ? _options.LogChannelCapacity : 10000;
        var channelOptions = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        };

        _channel = Channel.CreateBounded<ShadowEvaluationLogEntry>(channelOptions);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <summary>
    /// Đẩy log entry vào lock-free queue. Thao tác mất dưới 1 microsecond, tuyệt đối không await I/O.
    /// </summary>
    public bool LogEvaluation(ShadowEvaluationLogEntry entry)
    {
        if (!_options.EnableStructuredFprLogging)
        {
            return false;
        }

        bool written = _channel.Writer.TryWrite(entry);
        if (!written)
        {
            Interlocked.Increment(ref _droppedLogCount);
            if (DroppedLogCount % 1000 == 1)
            {
                _logger.LogWarning("FprMetricLogger: Channel capacity ({Cap}) full. Dropped {Count} logs.",
                    _options.LogChannelCapacity, DroppedLogCount);
            }
        }

        return written;
    }

    /// <summary>
    /// Vòng lặp đọc hàng đợi ngầm và ghi ra file .jsonl theo ngày.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FprMetricLogger: Background JSONL writer started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Chờ và đọc batch log từ channel
                if (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    await DrainBatchAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FprMetricLogger: Unexpected error in background log processing loop.");
                await Task.Delay(100, stoppingToken);
            }
        }

        // Khi nhận tín hiệu dừng, tiến hành drain các log còn tồn đọng
        try
        {
            await DrainBatchAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FprMetricLogger: Error draining remaining logs during shutdown.");
        }

        _logger.LogInformation("FprMetricLogger: Background JSONL writer stopped.");
    }

    /// <summary>
    /// Xả sạch dữ liệu trong buffer Channel ra đĩa.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await DrainBatchAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task DrainBatchAsync(CancellationToken ct)
    {
        var entries = new List<ShadowEvaluationLogEntry>();
        while (_channel.Reader.TryRead(out var entry))
        {
            entries.Add(entry);
            if (entries.Count >= 500)
            {
                break;
            }
        }

        if (entries.Count == 0)
        {
            return;
        }

        string logFilePath = ResolveLogFilePath();
        string? directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = entries.Select(e => JsonSerializer.Serialize(e, _jsonOptions));
        await File.AppendAllLinesAsync(logFilePath, lines, ct);
    }

    /// <summary>
    /// Xác định đường dẫn file log thực tế, tự động thay thế token {yyyyMMdd} theo ngày UTC.
    /// </summary>
    public string ResolveLogFilePath()
    {
        string path = _options.StructuredLogPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "logs/shadow_mode_evaluations_{yyyyMMdd}.jsonl";
        }

        if (path.Contains("{yyyyMMdd}"))
        {
            path = path.Replace("{yyyyMMdd}", DateTime.UtcNow.ToString("yyyyMMdd"));
        }
        else if (path.Contains("{Date}"))
        {
            path = path.Replace("{Date}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        // Nếu là đường dẫn tương đối, gắn với base directory của ứng dụng
        return Path.Combine(AppContext.BaseDirectory, path);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }
}

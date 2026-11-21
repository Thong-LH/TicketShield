using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models.Copilot;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Services.Copilot;

public class TicketCopilotService : ITicketCopilotService
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly IPromptSanitizer _sanitizer;
    private readonly AiAssistantOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TicketCopilotService> _logger;

    private const string SystemInstructionText = """
        Bạn là Ticket Copilot — Trợ lý thông minh cao cấp của sàn chuyển nhượng vé TicketShield.

        Phong cách giao tiếp:
        1. Tự nhiên, thông minh, tinh tế và đàm thoại như một chuyên viên tư vấn cao cấp (concierge) của Stripe/Wise. Trả lời bằng câu văn hoàn chỉnh, mượt mà (chỉ từ 1 đến 2 câu).
        2. TUYỆT ĐỐI CẤM định dạng câu trả lời theo dạng form/bản kê khai máy móc như:
           - Sự kiện: ...
           - Hạng vé: ...
           - Khu vực: ...
           - Mức giá: ...
           Lý do: Thẻ vé đồ họa trực quan đã hiển thị đầy đủ các thông tin này ngay bên dưới tin nhắn! Việc lặp lại dạng form là máy móc, thô cứng và gây khó chịu cho người dùng.
        3. TUYỆT ĐỐI CẤM lặp đi lặp lại câu kết sáo rỗng: "Bạn có thể bấm vào thẻ vé bên dưới để xem chi tiết trên sàn."
        4. TUYỆT ĐỐI CẤM các đoạn khuyến cáo an toàn, "Lưu ý an tâm", két bảo hiểm tiền, vé chính chủ, hay cảnh báo lừa đảo ngoài sàn.

        Ví dụ cách phản hồi chuẩn:
        - Khi tìm 1 vé giá thấp nhất: "Hiện vé có giá tốt nhất trên sàn là hạng **GA Standing** của concert **Anh Trai Say Hi 2026** với mức giá **1.200.000 đ**."
        - Khi tìm vé giá cao nhất: "Vé có giá cao nhất hiện tại là hạng **VIP Zone A** của sự kiện **Anh Trai Say Hi 2026** với giá **2.750.000 đ**."
        - Khi người dùng hỏi số lượng lớn (ví dụ: "cho tôi 100 vé có giá thấp nhất"): Nếu sàn chỉ có ít hơn số vé yêu cầu, hãy phản hồi tự nhiên: "Hiện sàn đang có sẵn 3 vé với mức giá từ **1.200.000 đ** đến **2.750.000 đ**, bạn tham khảo danh sách bên dưới nhé."
        - Khi kiểm tra trần giá: Trả lời tự nhiên bằng 1 câu rõ ràng mức giá đề xuất có hợp lệ hay vượt trần giá của Ban tổ chức.
        """;

    public TicketCopilotService(
        ITicketShieldDbContext dbContext,
        IPromptSanitizer sanitizer,
        IOptions<AiAssistantOptions> options,
        HttpClient httpClient,
        ILogger<TicketCopilotService> logger)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CopilotTicketItemDto>> SearchResaleTicketsAsync(
        string? eventName,
        string? tierName,
        decimal? maxPrice,
        string? zone,
        string? sortBy = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var query = _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Where(l => l.ListingStatus == ListingStatus.Verified && !l.IsPrivate)
            .Where(l => l.Event.EventStartAt.AddHours(-2) > now);

        if (!string.IsNullOrWhiteSpace(eventName))
        {
            var cleanEvent = _sanitizer.SanitizeUserInput(eventName).ToLower();
            query = query.Where(l => l.Event.Name.ToLower().Contains(cleanEvent));
        }

        if (!string.IsNullOrWhiteSpace(tierName))
        {
            var cleanTier = _sanitizer.SanitizeUserInput(tierName).ToLower();
            query = query.Where(l => l.Tier.TierName.ToLower().Contains(cleanTier));
        }

        if (maxPrice.HasValue && maxPrice.Value > 0)
        {
            query = query.Where(l => l.ResalePrice <= maxPrice.Value);
        }

        if (!string.IsNullOrWhiteSpace(zone))
        {
            var cleanZone = _sanitizer.SanitizeUserInput(zone).ToLower();
            query = query.Where(l => l.SeatZone != null && l.SeatZone.ToLower().Contains(cleanZone));
        }

        var sortedQuery = sortBy?.ToUpperInvariant() switch
        {
            "PRICE_DESC" => query.OrderByDescending(l => l.ResalePrice),
            "DATE_ASC" => query.OrderBy(l => l.Event.EventStartAt),
            _ => query.OrderBy(l => l.ResalePrice)
        };

        var effectiveLimit = limit.HasValue && limit.Value > 0 ? limit.Value : 10;
        var listings = await sortedQuery
            .Take(effectiveLimit)
            .ToListAsync(ct);

        return listings.Select(l => new CopilotTicketItemDto
        {
            ListingId = l.Id,
            EventId = l.EventId,
            EventName = _sanitizer.SanitizeDatabaseContent(l.Event.Name),
            TierName = _sanitizer.SanitizeDatabaseContent(l.Tier.TierName),
            SeatZone = l.SeatZone,
            ResalePrice = l.ResalePrice,
            OriginalPrice = l.OriginalPrice,
            DiscountPercentage = l.DiscountPercentage,
            EventStartAt = l.Event.EventStartAt,
            Venue = _sanitizer.SanitizeDatabaseContent(l.Event.Venue)
        }).ToList();
    }

    public async Task<CopilotPriceCapResultDto> CheckPriceCapAsync(
        string tierName,
        decimal proposedPrice,
        string? eventName = null,
        CancellationToken ct = default)
    {
        var cleanTier = _sanitizer.SanitizeUserInput(tierName);
        var cleanTierLower = cleanTier.ToLower();
        var tierQuery = _dbContext.TicketTiers
            .AsNoTracking()
            .Include(t => t.Event)
            .Where(t => t.TierName.ToLower().Contains(cleanTierLower));

        if (!string.IsNullOrWhiteSpace(eventName))
        {
            var cleanEventLower = _sanitizer.SanitizeUserInput(eventName).ToLower();
            tierQuery = tierQuery.Where(t => t.Event.Name.ToLower().Contains(cleanEventLower));
        }

        var tier = await tierQuery.FirstOrDefaultAsync(ct);
        if (tier == null)
        {
            return new CopilotPriceCapResultDto
            {
                TierName = cleanTier,
                ProposedPrice = proposedPrice,
                IsWithinCeiling = true,
                Message = $"Không tìm thấy thông tin hạng vé '{cleanTier}' trong hệ thống. Vui lòng kiểm tra lại tên hạng vé."
            };
        }

        var markup = tier.Event.MaxResaleMarkupPercentage;
        var ceiling = ResaleListing.ComputePriceCeiling(tier.OriginalPrice, markup);
        var isWithin = proposedPrice <= ceiling;

        var message = isWithin
            ? $"Mức giá {proposedPrice:N0} đ hợp lệ, nằm trong trần giá cho phép ({ceiling:N0} đ, giá gốc {tier.OriginalPrice:N0} đ + {markup:0.##}%)."
            : $"Mức giá {proposedPrice:N0} đ vượt quá trần giá quy định {ceiling:N0} đ (giá gốc {tier.OriginalPrice:N0} đ + {markup:0.##}%). Hệ thống sẽ chặn đăng bán vé này.";

        return new CopilotPriceCapResultDto
        {
            TierName = tier.TierName,
            OriginalPrice = tier.OriginalPrice,
            ProposedPrice = proposedPrice,
            PriceCeiling = ceiling,
            MaxMarkupPercentage = markup,
            IsWithinCeiling = isWithin,
            Message = message
        };
    }

    public async Task<CopilotChatResponse> ChatAsync(CopilotChatRequest request, CancellationToken ct = default)
    {
        var sanitizedInput = _sanitizer.SanitizeUserInput(request.Message);
        if (string.IsNullOrWhiteSpace(sanitizedInput))
        {
            return new CopilotChatResponse
            {
                Reply = "Chào bạn! Tôi có thể giúp gì cho bạn trong việc tìm vé hoặc giải đáp chính sách giao dịch trên sàn TicketShield?"
            };
        }

        var apiKey = _options.Gemini.ApiKey;
        var configuredModel = string.IsNullOrWhiteSpace(_options.Gemini.Model) ? "gemini-flash-lite-latest" : _options.Gemini.Model;
        var baseUrl = string.IsNullOrWhiteSpace(_options.Gemini.BaseUrl) ? "https://generativelanguage.googleapis.com/v1beta" : _options.Gemini.BaseUrl;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Gemini API key is not configured. Falling back to local offline search response.");
            return await FallbackLocalIntentProcessingAsync(sanitizedInput, ct);
        }

        var candidateModels = new[] { configuredModel, "gemini-flash-lite-latest", "gemini-flash-latest" }
            .Distinct()
            .ToList();

        foreach (var model in candidateModels)
        {
            try
            {
                var executedTools = new List<string>();
                List<CopilotTicketItemDto>? suggestedTickets = null;
                CopilotPriceCapResultDto? priceCapInfo = null;

                var requestBody = BuildGeminiRequestBody(sanitizedInput, request.GetEffectiveHistory());
                var url = $"{baseUrl.TrimEnd('/')}/models/{model}:generateContent";

                var reqMessage = new HttpRequestMessage(HttpMethod.Post, url);
                reqMessage.Headers.Add("X-goog-api-key", apiKey);
                reqMessage.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

                var resp = await _httpClient.SendAsync(reqMessage, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var errContent = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Gemini API ({Model}) error ({StatusCode}): {Error}", model, resp.StatusCode, errContent);
                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)resp.StatusCode == 503)
                    {
                        continue; // Thử model tiếp theo trong danh sách
                    }
                    return await FallbackLocalIntentProcessingAsync(sanitizedInput, ct);
                }

                var respJson = await resp.Content.ReadAsStringAsync(ct);
                var root = JsonNode.Parse(respJson);
                var candidate = root?["candidates"]?[0];
                var candidateContent = candidate?["content"]?.AsObject();
                var parts = candidateContent?["parts"]?.AsArray();

                var replyText = new StringBuilder();
                JsonObject? functionCall = null;

                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        if (part?["text"] != null)
                        {
                            replyText.Append(part["text"]?.ToString());
                        }
                        else if (part?["functionCall"] != null)
                        {
                            functionCall = part["functionCall"]?.AsObject();
                        }
                    }
                }

                // Xử lý Function Calling nếu Gemini yêu cầu
                if (functionCall != null)
                {
                    var fnName = functionCall["name"]?.ToString();
                    var fnArgs = functionCall["args"]?.AsObject();

                    if (fnName == "SearchResaleTickets")
                    {
                        executedTools.Add("SearchResaleTickets");
                        var eventName = fnArgs?["eventName"]?.ToString();
                        var tierName = fnArgs?["tierName"]?.ToString();
                        decimal? maxPrice = fnArgs?["maxPrice"] != null && decimal.TryParse(fnArgs["maxPrice"]?.ToString(), out var p) ? p : null;
                        var zone = fnArgs?["zone"]?.ToString();
                        var sortBy = fnArgs?["sortBy"]?.ToString();
                        int? limit = fnArgs?["limit"] != null && int.TryParse(fnArgs["limit"]?.ToString(), out var lmt) ? lmt : null;

                        var queryLower = sanitizedInput.ToLowerInvariant();
                        var hasExplicitQuantity = System.Text.RegularExpressions.Regex.IsMatch(queryLower, @"\b(\d+)\s*v[eé]\b")
                            || queryLower.Contains("các vé") || queryLower.Contains("danh sách") || queryLower.Contains("những vé");

                        var isSingularSuperlative = (queryLower.Contains("cao nhất") || queryLower.Contains("đắt nhất") 
                            || queryLower.Contains("cao tiền nhất") || queryLower.Contains("giá cao nhất")
                            || queryLower.Contains("rẻ nhất") || queryLower.Contains("thấp nhất")
                            || queryLower.Contains("giá rẻ nhất") || queryLower.Contains("giá thấp nhất")
                            || queryLower.Contains("vé nào") || queryLower.Contains("vé cao nhất") || queryLower.Contains("vé rẻ nhất"))
                            && !hasExplicitQuantity;

                        if (isSingularSuperlative)
                        {
                            limit = 1;
                            if (queryLower.Contains("cao") || queryLower.Contains("đắt"))
                            {
                                sortBy = "PRICE_DESC";
                            }
                            else if (queryLower.Contains("rẻ") || queryLower.Contains("thấp"))
                            {
                                sortBy = "PRICE_ASC";
                            }
                        }
                        else if ((sortBy == "PRICE_DESC" || sortBy == "PRICE_ASC") && !limit.HasValue && !hasExplicitQuantity)
                        {
                            limit = 1;
                        }
                        else if (queryLower.Contains("rẻ") || queryLower.Contains("thấp"))
                        {
                            sortBy ??= "PRICE_ASC";
                        }
                        else if (queryLower.Contains("cao") || queryLower.Contains("đắt"))
                        {
                            sortBy ??= "PRICE_DESC";
                        }

                        suggestedTickets = await SearchResaleTicketsAsync(eventName, tierName, maxPrice, zone, sortBy, limit, ct);

                        // Gửi kết quả Tool về lại cho Gemini tạo câu phản hồi tự nhiên (kèm candidateContent giữ nguyên thoughtSignature)
                        replyText.Clear();
                        var toolFollowUpReply = await CallGeminiWithFunctionResultAsync(
                            url, apiKey, requestBody, candidateContent, fnName, suggestedTickets, ct);
                        replyText.Append(toolFollowUpReply);
                    }
                    else if (fnName == "CheckPriceCap")
                    {
                        executedTools.Add("CheckPriceCap");
                        var tierName = fnArgs?["tierName"]?.ToString() ?? string.Empty;
                        var proposedPrice = fnArgs?["proposedPrice"] != null && decimal.TryParse(fnArgs["proposedPrice"]?.ToString(), out var pp) ? pp : 0;
                        var eventName = fnArgs?["eventName"]?.ToString();

                        priceCapInfo = await CheckPriceCapAsync(tierName, proposedPrice, eventName, ct);

                        replyText.Clear();
                        var toolFollowUpReply = await CallGeminiWithFunctionResultAsync(
                            url, apiKey, requestBody, candidateContent, fnName, priceCapInfo, ct);
                        replyText.Append(toolFollowUpReply);
                    }
                }

                var finalReply = replyText.Length > 0 ? replyText.ToString() : "Tôi đã xử lý yêu cầu của bạn.";

                return new CopilotChatResponse
                {
                    Reply = finalReply,
                    SuggestedTickets = suggestedTickets,
                    PriceCapInfo = priceCapInfo,
                    ExecutedTools = executedTools.Count > 0 ? executedTools : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call Gemini API ({Model}) in TicketCopilotService", model);
            }
        }

        return await FallbackLocalIntentProcessingAsync(sanitizedInput, ct);
    }

    public async IAsyncEnumerable<string> StreamChatAsync(CopilotChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var sanitizedInput = _sanitizer.SanitizeUserInput(request.Message);
        if (string.IsNullOrWhiteSpace(sanitizedInput))
        {
            yield return "Chào bạn! Tôi có thể giúp gì cho bạn?";
            yield break;
        }

        var apiKey = _options.Gemini.ApiKey;
        var model = string.IsNullOrWhiteSpace(_options.Gemini.Model) ? "gemini-flash-lite-latest" : _options.Gemini.Model;
        var baseUrl = string.IsNullOrWhiteSpace(_options.Gemini.BaseUrl) ? "https://generativelanguage.googleapis.com/v1beta" : _options.Gemini.BaseUrl;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var fallback = await FallbackLocalIntentProcessingAsync(sanitizedInput, ct);
            yield return fallback.Reply;
            yield break;
        }

        var url = $"{baseUrl.TrimEnd('/')}/models/{model}:streamGenerateContent?alt=sse";
        var requestBody = BuildGeminiRequestBody(sanitizedInput, request.GetEffectiveHistory());

        var reqMessage = new HttpRequestMessage(HttpMethod.Post, url);
        reqMessage.Headers.Add("X-goog-api-key", apiKey);
        reqMessage.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage? resp = null;
        try
        {
            resp = await _httpClient.SendAsync(reqMessage, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish SSE stream with Gemini");
        }

        if (resp == null || !resp.IsSuccessStatusCode)
        {
            var fallback = await ChatAsync(request, ct);
            yield return fallback.Reply;
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var tokensYielded = 0;
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var jsonText = line["data: ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(jsonText))
                continue;

            string? textChunk = null;
            try
            {
                var node = JsonNode.Parse(jsonText);
                textChunk = node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
            }
            catch
            {
                // Ignore parse errors on partial frames
            }

            if (!string.IsNullOrEmpty(textChunk))
            {
                tokensYielded++;
                yield return textChunk;
            }
        }

        // Nếu stream không sinh ra text token nào (do Gemini gọi functionCall hoặc stream trống),
        // tự động chuyển tiếp qua ChatAsync để thực thi tool và trả về lời thoại tự nhiên
        if (tokensYielded == 0)
        {
            var syncResult = await ChatAsync(request, ct);
            if (!string.IsNullOrWhiteSpace(syncResult.Reply))
            {
                yield return syncResult.Reply;
            }
        }
    }

    private JsonObject BuildGeminiRequestBody(string prompt, List<CopilotChatMessage>? history)
    {
        var contents = new JsonArray();

        if (history != null && history.Count > 0)
        {
            foreach (var msg in history.TakeLast(6))
            {
                var role = msg.Role.ToLowerInvariant() == "assistant" ? "model" : "user";
                contents.Add(new JsonObject
                {
                    ["role"] = role,
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = _sanitizer.SanitizeUserInput(msg.Content) } }
                });
            }
        }

        contents.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray { new JsonObject { ["text"] = prompt } }
        });

        var toolsArray = new JsonArray
        {
            new JsonObject
            {
                ["function_declarations"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "SearchResaleTickets",
                        ["description"] = "Tra cứu vé chuyển nhượng hợp lệ đang mở bán trên sàn TicketShield theo tên sự kiện, hạng vé, giá tối đa và khu vực ghế.",
                        ["parameters"] = new JsonObject
                        {
                            ["type"] = "OBJECT",
                            ["properties"] = new JsonObject
                            {
                                ["eventName"] = new JsonObject { ["type"] = "STRING", ["description"] = "Tên sự kiện hoặc ca sĩ/nghệ sĩ" },
                                ["tierName"] = new JsonObject { ["type"] = "STRING", ["description"] = "Tên hạng vé (ví dụ VIP, GA, Standard)" },
                                ["maxPrice"] = new JsonObject { ["type"] = "NUMBER", ["description"] = "Mức giá tối đa mong muốn tính bằng VNĐ" },
                                ["zone"] = new JsonObject { ["type"] = "STRING", ["description"] = "Khu vực hoặc khán đài (ví dụ Khán đài A, Zone B)" },
                                ["sortBy"] = new JsonObject { ["type"] = "STRING", ["description"] = "Thứ tự sắp xếp: 'PRICE_DESC' (vé cao tiền nhất / đắt nhất), 'PRICE_ASC' (vé rẻ nhất / giá thấp nhất), 'DATE_ASC' (sắp diễn ra)" },
                                ["limit"] = new JsonObject { ["type"] = "INTEGER", ["description"] = "Số lượng vé cần trả về: 1 khi hỏi vé cao nhất / rẻ nhất; tối đa 5 khi tìm vé chung" }
                            }
                        }
                    },
                    new JsonObject
                    {
                        ["name"] = "CheckPriceCap",
                        ["description"] = "Kiểm tra mức giá bán lại đề xuất có vi phạm chính sách trần giá Fair-Price Cap của Ban tổ chức hay không.",
                        ["parameters"] = new JsonObject
                        {
                            ["type"] = "OBJECT",
                            ["properties"] = new JsonObject
                            {
                                ["tierName"] = new JsonObject { ["type"] = "STRING", ["description"] = "Tên hạng vé cần kiểm tra trần giá" },
                                ["proposedPrice"] = new JsonObject { ["type"] = "NUMBER", ["description"] = "Mức giá đề xuất tính bằng VNĐ" },
                                ["eventName"] = new JsonObject { ["type"] = "STRING", ["description"] = "Tên sự kiện (tùy chọn)" }
                            },
                            ["required"] = new JsonArray { "tierName", "proposedPrice" }
                        }
                    }
                }
            }
        };

        return new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = SystemInstructionText } }
            },
            ["contents"] = contents,
            ["tools"] = toolsArray
        };
    }

    private async Task<string> CallGeminiWithFunctionResultAsync(
        string url,
        string apiKey,
        JsonObject originalRequestBody,
        JsonObject? modelContent,
        string functionName,
        object? resultData,
        CancellationToken ct)
    {
        try
        {
            var contents = originalRequestBody["contents"]?.AsArray();
            if (contents == null) return "Đã tìm thấy thông tin vé phù hợp với yêu cầu của bạn trên hệ thống.";

            // 1. Ghi nhận lượt của model chứa functionCall (giữ nguyên thoughtSignature và id của Gemini)
            if (modelContent != null)
            {
                contents.Add(modelContent.DeepClone());
            }

            // 2. Ghi nhận lượt phản hồi kết quả hàm (Gemini v1beta yêu cầu role: "user")
            var contentNode = JsonNode.Parse(JsonSerializer.Serialize(resultData)) ?? new JsonObject();
            var functionResponsePart = new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = functionName,
                    ["response"] = new JsonObject
                    {
                        ["name"] = functionName,
                        ["content"] = contentNode
                    }
                }
            };

            contents.Add(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { functionResponsePart }
            });

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, url);
            reqMessage.Headers.Add("X-goog-api-key", apiKey);
            reqMessage.Content = new StringContent(originalRequestBody.ToJsonString(), Encoding.UTF8, "application/json");

            var resp = await _httpClient.SendAsync(reqMessage, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Gemini follow-up error ({StatusCode}): {Error}", resp.StatusCode, err);
                
                if (resultData is List<CopilotTicketItemDto> tickets && tickets.Count > 0)
                {
                    var top = tickets[0];
                    return tickets.Count == 1
                        ? $"Hiện vé có giá tốt nhất trên sàn là hạng **{top.TierName}** của sự kiện **{top.EventName}** với giá **{top.ResalePrice:N0} đ**."
                        : $"Hiện trên sàn đang có sẵn {tickets.Count} vé với mức giá từ **{tickets.Min(t => t.ResalePrice):N0} đ** đến **{tickets.Max(t => t.ResalePrice):N0} đ**.";
                }

                if (resultData is CopilotPriceCapResultDto cap)
                {
                    return cap.Message;
                }

                return "Đã tìm thấy thông tin vé phù hợp với yêu cầu của bạn trên hệ thống TicketShield.";
            }

            var respJson = await resp.Content.ReadAsStringAsync(ct);
            var root = JsonNode.Parse(respJson);
            return root?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() 
                ?? "Đã tìm thấy thông tin vé phù hợp với yêu cầu của bạn trên hệ thống.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Gemini follow-up function result call");
            if (resultData is List<CopilotTicketItemDto> tickets && tickets.Count > 0)
            {
                var top = tickets[0];
                return tickets.Count == 1
                    ? $"Hiện vé có giá tốt nhất trên sàn là hạng **{top.TierName}** của sự kiện **{top.EventName}** với giá **{top.ResalePrice:N0} đ**."
                    : $"Hiện trên sàn đang có sẵn {tickets.Count} vé với mức giá từ **{tickets.Min(t => t.ResalePrice):N0} đ** đến **{tickets.Max(t => t.ResalePrice):N0} đ**.";
            }
            return "Đã tìm thấy thông tin theo yêu cầu của bạn trên hệ thống TicketShield.";
        }
    }

    private async Task<CopilotChatResponse> FallbackLocalIntentProcessingAsync(string query, CancellationToken ct)
    {
        var lower = query.ToLowerInvariant();
        var isSearchIntent = lower.Contains("tìm") || lower.Contains("tim")
            || lower.Contains("mua") || lower.Contains("vé") || lower.Contains("ve")
            || lower.Contains("giá") || lower.Contains("gia") || lower.Contains("ticket")
            || lower.Contains("show") || lower.Contains("concert") || lower.Contains("say hi")
            || lower.Contains("anh trai") || lower.Contains("cao") || lower.Contains("đắt") || lower.Contains("rẻ");

        if (isSearchIntent)
        {
            string? eventFilter = null;
            if (lower.Contains("anh trai") || lower.Contains("say hi"))
            {
                eventFilter = "Anh Trai Say Hi";
            }

            var hasExplicitQuantity = System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(\d+)\s*v[eé]\b")
                || lower.Contains("các vé") || lower.Contains("danh sách") || lower.Contains("những vé");

            string? sortBy = null;
            int? limit = null;
            if (lower.Contains("cao nhất") || lower.Contains("đắt nhất") || lower.Contains("cao tiền") || lower.Contains("giá cao"))
            {
                sortBy = "PRICE_DESC";
                if (!hasExplicitQuantity) limit = 1;
            }
            else if (lower.Contains("rẻ nhất") || lower.Contains("thấp nhất") || lower.Contains("giá rẻ"))
            {
                sortBy = "PRICE_ASC";
                if (!hasExplicitQuantity) limit = 1;
            }

            var tickets = await SearchResaleTicketsAsync(eventFilter, null, null, null, sortBy, limit, ct);
            if (tickets.Count > 0)
            {
                string replyMsg;
                if (tickets.Count == 1 && sortBy == "PRICE_DESC")
                {
                    var topTicket = tickets[0];
                    replyMsg = $"Vé có mức giá cao nhất hiện tại là hạng **{topTicket.TierName}** của sự kiện **{topTicket.EventName}** với giá **{topTicket.ResalePrice:N0} đ**.";
                }
                else if (tickets.Count == 1 && sortBy == "PRICE_ASC")
                {
                    var lowestTicket = tickets[0];
                    replyMsg = $"Hiện vé có mức giá tốt nhất trên sàn là hạng **{lowestTicket.TierName}** của sự kiện **{lowestTicket.EventName}** với giá **{lowestTicket.ResalePrice:N0} đ**.";
                }
                else
                {
                    replyMsg = $"Hiện sàn đang có sẵn {tickets.Count} vé với mức giá từ **{tickets.Min(t => t.ResalePrice):N0} đ** đến **{tickets.Max(t => t.ResalePrice):N0} đ**, bạn tham khảo danh sách bên dưới nhé.";
                }

                return new CopilotChatResponse
                {
                    Reply = replyMsg,
                    SuggestedTickets = tickets,
                    ExecutedTools = new List<string> { "SearchResaleTickets" }
                };
            }

            return new CopilotChatResponse
            {
                Reply = "Hiện tại không tìm thấy vé phù hợp theo yêu cầu của bạn trên hệ thống.",
                SuggestedTickets = tickets,
                ExecutedTools = new List<string> { "SearchResaleTickets" }
            };
        }

        return new CopilotChatResponse
        {
            Reply = "TicketShield là sàn chuyển nhượng vé an toàn: Người mua được cấp vé mới chính chủ 100% từ Ban tổ chức, tiền mua được bảo hiểm trong két an toàn và chỉ giải ngân sau khi sự kiện kết thúc."
        };
    }
}

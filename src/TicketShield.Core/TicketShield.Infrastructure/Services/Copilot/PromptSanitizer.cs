using System.Text.RegularExpressions;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services.Copilot;

public class PromptSanitizer : IPromptSanitizer
{
    private static readonly Regex DelimiterPattern = new(
        @"(<\|im_start\|>|<\|im_end\|>|\[INST\]|\[/INST\]|<system>|</system>|<context>|</context>|###\s*(System|Instruction|Assistant):?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InjectionPattern = new(
        @"(ignore\s+(all\s+)?(previous|prior)\s+(instructions|prompts|rules)|disregard\s+(all\s+)?(previous|prior)|you\s+are\s+now\s+(DAN|unfiltered|jailbroken)|developer\s+mode\s+enabled|bỏ\s+qua\s+(toàn\s+bộ\s+)?(chỉ\s+dẫn|quy\s+tắc)\s+trước|quên\s+(hết\s+)?(quy\s+định|luật)\s+cũ|từ\s+giờ\s+hãy\s+đóng\s+vai)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string SanitizeUserInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // 1. Remove dangerous control characters (except newline and tab)
        var cleaned = new string(input.Where(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t').ToArray());

        // 2. Neutralize LLM template delimiters
        cleaned = DelimiterPattern.Replace(cleaned, "[FILTERED_TAG]");

        // 3. Truncate extreme length to prevent context flooding (max 1000 chars for a single query)
        if (cleaned.Length > 1000)
        {
            cleaned = cleaned[..1000];
        }

        return cleaned.Trim();
    }

    public string SanitizeDatabaseContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        // Neutralize delimiters and potential injection vectors in seller/event notes
        var cleaned = DelimiterPattern.Replace(content, " ");
        cleaned = InjectionPattern.Replace(cleaned, "[SECURITY_NEUTRALIZED]");

        return cleaned.Trim();
    }

    public bool ContainsInjectionAttempt(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return DelimiterPattern.IsMatch(input) || InjectionPattern.IsMatch(input);
    }
}

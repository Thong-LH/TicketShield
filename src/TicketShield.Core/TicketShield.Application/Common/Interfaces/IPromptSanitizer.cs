namespace TicketShield.Application.Common.Interfaces;

public interface IPromptSanitizer
{
    string SanitizeUserInput(string input);
    string SanitizeDatabaseContent(string? content);
    bool ContainsInjectionAttempt(string input);
}

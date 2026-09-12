using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MockOrganizer.API.Resale;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// SCRUM-37 · TEST-2.5.2 — Integration tests for the OTP step of ticket verification.
///
/// Every test runs the real chain: TicketShield Core API → gRPC → MockOrganizer → PostgreSQL,
/// with a fake SMTP inbox that captures the OTP e-mail and a controllable clock (f.Clock)
/// so "5 minutes later" takes milliseconds.
///
/// Limits come from MockOrganizer's ResaleOptions, so the tests keep working if the team
/// changes MaxAttempts or OtpLifetimeSeconds in configuration.
/// </summary>
public sealed class OtpVerificationTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    private ResaleOptions Options => f.Mock.Services.GetRequiredService<ResaleOptions>();

    private static string WrongOtpFor(string otp) => otp == "000000" ? "111111" : "000000";

    private Task<(HttpStatusCode Status, JsonElement Body)> Confirm(string verificationId, string otp, string seller = ResaleFixture.Seller) =>
        f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp }, seller: seller);

    private async Task<string?> OrganizerTicketStatus(string ticketCode) =>
        (string?)await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticketCode));

    private static string? Message(JsonElement body) =>
        body.TryGetProperty("message", out var message) ? message.GetString() : null;

    // ───────────────────────── Happy path ─────────────────────────

    [Fact]
    public async Task Otp_email_is_sent_to_the_ticket_owner_with_a_6_digit_code()
    {
        var ticket = await f.Ticket();

        var (_, otp) = await f.Start(ticket);

        Assert.Matches(new Regex(@"^\d{6}$"), otp);
        Assert.Contains("seller@example.invalid", f.Mail.Messages.Last().Recipient);
    }

    [Fact]
    public async Task Correct_otp_verifies_the_ticket_and_locks_it_at_the_organizer()
    {
        var ticket = await f.Ticket(price: 2_500_000m);
        var (verificationId, otp) = await f.Start(ticket);

        var result = await Confirm(verificationId, otp);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var data = result.Body.GetProperty("data");
        Assert.Equal("Verified", data.GetProperty("status").GetString());
        Assert.Equal(2_500_000L, data.GetProperty("originalPrice").GetInt64());
        Assert.Equal("LOCKED_FOR_RESALE", await OrganizerTicketStatus(ticket));
    }

    // ───────────────────────── Wrong OTP & attempt limit ─────────────────────────

    [Fact]
    public async Task Wrong_otp_is_rejected_and_the_ticket_stays_valid()
    {
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);

        var result = await Confirm(verificationId, WrongOtpFor(otp));

        Assert.Equal(HttpStatusCode.Conflict, result.Status);
        Assert.Equal("OTP_INVALID", Message(result.Body));
        Assert.Equal("VALID", await OrganizerTicketStatus(ticket));
    }

    [Fact]
    public async Task Correct_otp_still_works_after_fewer_wrong_attempts_than_the_limit()
    {
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);

        for (var attempt = 1; attempt < Options.MaxAttempts; attempt++)
            Assert.Equal(HttpStatusCode.Conflict, (await Confirm(verificationId, WrongOtpFor(otp))).Status);

        var result = await Confirm(verificationId, otp);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("LOCKED_FOR_RESALE", await OrganizerTicketStatus(ticket));
    }

    [Fact]
    public async Task Verification_is_blocked_once_the_wrong_attempt_limit_is_reached_even_for_the_correct_otp()
    {
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);

        for (var attempt = 1; attempt <= Options.MaxAttempts; attempt++)
            Assert.Equal(HttpStatusCode.Conflict, (await Confirm(verificationId, WrongOtpFor(otp))).Status);

        var result = await Confirm(verificationId, otp);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.Status);
        Assert.Equal("OTP_ATTEMPTS_EXHAUSTED", Message(result.Body));
        Assert.Equal("VALID", await OrganizerTicketStatus(ticket));
    }

    // ───────────────────────── Expiry ─────────────────────────

    [Fact]
    public void Otp_lifetime_is_5_minutes_as_specified_in_SCRUM_37()
    {
        Assert.Equal(300, Options.OtpLifetimeSeconds);
    }

    [Fact]
    public async Task Otp_is_still_accepted_one_second_before_it_expires()
    {
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);

        f.Clock.Advance(TimeSpan.FromSeconds(Options.OtpLifetimeSeconds - 1));
        var result = await Confirm(verificationId, otp);

        Assert.Equal(HttpStatusCode.OK, result.Status);
    }

    [Fact]
    public async Task Otp_is_rejected_after_it_expires_and_the_ticket_stays_valid()
    {
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);

        f.Clock.Advance(TimeSpan.FromSeconds(Options.OtpLifetimeSeconds + 1));
        var result = await Confirm(verificationId, otp);

        Assert.Equal(HttpStatusCode.Conflict, result.Status);
        Assert.Equal("OTP_EXPIRED", Message(result.Body));
        Assert.Equal("VALID", await OrganizerTicketStatus(ticket));
    }

    // ───────────────────────── Ownership ─────────────────────────

    [Fact]
    public async Task Another_seller_cannot_confirm_someone_elses_verification()
    {
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);

        var intruder = await Confirm(verificationId, otp, seller: ResaleFixture.OtherSeller);
        var owner = await Confirm(verificationId, otp);

        Assert.Equal(HttpStatusCode.NotFound, intruder.Status);
        Assert.Equal(HttpStatusCode.OK, owner.Status);
    }

    // ───────────────────────── Open question with the team ─────────────────────────

    [Fact(Skip = "SCRUM-37 says the OTP locks after 3 wrong attempts, MockOrganizer is configured with MaxAttempts = 5. Un-skip once the team agrees on 3.")]
    public void Wrong_attempt_limit_matches_the_jira_rule_of_3()
    {
        Assert.Equal(3, Options.MaxAttempts);
    }
}

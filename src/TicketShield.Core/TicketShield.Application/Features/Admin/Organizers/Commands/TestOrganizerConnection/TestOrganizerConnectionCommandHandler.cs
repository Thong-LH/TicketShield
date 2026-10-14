using System.Diagnostics;
using System.Net.Http;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.TestOrganizerConnection;

public class TestOrganizerConnectionCommandHandler(
    ITicketShieldDbContext dbContext,
    IHttpClientFactory httpClientFactory)
    : IRequestHandler<TestOrganizerConnectionCommand, ApiResponse<TestOrganizerConnectionResult>>
{
    public async Task<ApiResponse<TestOrganizerConnectionResult>> Handle(
        TestOrganizerConnectionCommand request,
        CancellationToken ct)
    {
        var organizer = await dbContext.Organizers
            .FirstOrDefaultAsync(o => o.Id == request.Id, ct);

        if (organizer == null)
        {
            throw new NotFoundException($"Không tìm thấy ban tổ chức với ID '{request.Id}'.");
        }

        var endpoint = !string.IsNullOrWhiteSpace(organizer.GrpcEndpoint)
            ? organizer.GrpcEndpoint
            : organizer.ApiBaseUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            organizer.ConnectionStatus = "DISCONNECTED";
            await dbContext.SaveChangesAsync(ct);

            return new ApiResponse<TestOrganizerConnectionResult>
            {
                Success = false,
                Message = "Ban tổ chức chưa cấu hình gRPC Endpoint hoặc API Base URL.",
                Data = new TestOrganizerConnectionResult(
                    organizer.Id,
                    "DISCONNECTED",
                    0,
                    string.Empty,
                    "Chưa cấu hình Endpoint."
                )
            };
        }

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(4);

        var sw = Stopwatch.StartNew();
        var isConnected = false;
        var message = string.Empty;

        try
        {
            // Healthcheck probe to endpoint root or health endpoint
            var uri = new Uri(endpoint);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            // In HTTP/2 gRPC or REST, getting any response (even 404/405 or 200) proves TCP/HTTP reachability
            isConnected = true;
            message = $"Kết nối ổn định tới máy chủ đối tác ({sw.ElapsedMilliseconds}ms).";
        }
        catch (Exception ex)
        {
            sw.Stop();
            isConnected = false;
            message = $"Không thể kết nối tới {endpoint}: {ex.Message}";
        }

        organizer.ConnectionStatus = isConnected ? "CONNECTED" : "DISCONNECTED";
        organizer.LastPingAt = DateTimeOffset.UtcNow;
        organizer.LastPingLatencyMs = sw.ElapsedMilliseconds;
        if (isConnected && organizer.Status == "DRAFT")
        {
            organizer.Status = "CONNECTED";
        }
        organizer.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var result = new TestOrganizerConnectionResult(
            organizer.Id,
            organizer.ConnectionStatus,
            sw.ElapsedMilliseconds,
            endpoint,
            message
        );

        return new ApiResponse<TestOrganizerConnectionResult>
        {
            Success = isConnected,
            Message = message,
            Data = result
        };
    }
}

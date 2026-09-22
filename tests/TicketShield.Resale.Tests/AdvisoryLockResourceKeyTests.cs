using System.Net;
using System.Reflection;
using Grpc.Core;
using Npgsql;
using TicketShield.Application.Resale;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// BE-CORE-4.3.4 — lock key is derived from the resource id.
/// A missing id is rejected. The same id shares one lock. Two ids do not.
/// </summary>
public sealed class AdvisoryLockResourceKeyTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task Missing_resource_id_is_rejected_without_taking_a_global_lock()
    {
        var core = Assert.Throws<ResaleWorkflowException>(() => CoreKey(""));
        Assert.Equal("INVALID_REFERENCE", core.Code);
        Assert.Equal(400, core.HttpStatus);
        Assert.Throws<ResaleWorkflowException>(() => CoreKey(null));

        var organizer = Assert.Throws<RpcException>(() => OrganizerKey(""));
        Assert.Equal(StatusCode.InvalidArgument, organizer.StatusCode);
        Assert.Equal("INVALID_REFERENCE", organizer.Status.Detail);
        Assert.Throws<RpcException>(() => OrganizerKey(null));

        var rejected = await f.Post("api/ticket-verifications", new { ticketCode = "" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);
        Assert.Equal("INVALID_REFERENCE", rejected.Body.GetProperty("message").GetString());

        var grpc = await Assert.ThrowsAsync<RpcException>(() => f.Grpc.GetVerificationAsync(
            new GetVerificationRequest
            {
                Verification = new VerificationReference
                {
                    VerificationId = "",
                    RequesterRef = ResaleFixture.Seller
                }
            },
            f.Headers).ResponseAsync);
        Assert.Equal(StatusCode.InvalidArgument, grpc.StatusCode);
        Assert.Equal("INVALID_REFERENCE", grpc.Status.Detail);
    }

    [Fact]
    public async Task Same_resource_id_shares_one_lock_so_the_second_request_waits()
    {
        const string ticket = "VE-001";
        var first = CoreKey(ticket);
        var second = CoreKey(ticket);
        Assert.Equal(first, second);
        Assert.NotEqual(84722002L, first);

        var session = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        var organizerFirst = OrganizerKey(session);
        Assert.Equal(organizerFirst, OrganizerKey(session));
        Assert.NotEqual(84722001L, organizerFirst);

        await using var holder = await OpenAsync();
        await using var waiter = await OpenAsync();
        await using var held = await holder.BeginTransactionAsync();
        await LockAsync(holder, held, first);

        await using var waiting = await waiter.BeginTransactionAsync();
        await new NpgsqlCommand("SET LOCAL statement_timeout = '400ms'", waiter, waiting).ExecuteNonQueryAsync();
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => LockAsync(waiter, waiting, second));
        Assert.Equal("57014", blocked.SqlState);
    }

    [Fact]
    public async Task Different_resource_ids_use_different_locks_and_run_together()
    {
        var ticketA = CoreKey("VE-001");
        var ticketB = CoreKey("VE-002");
        Assert.NotEqual(ticketA, ticketB);

        var sessionA = OrganizerKey("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var sessionB = OrganizerKey("cccccccc-cccc-cccc-cccc-cccccccccccc");
        Assert.NotEqual(sessionA, sessionB);

        await using var holder = await OpenAsync();
        await using var other = await OpenAsync();
        await using var held = await holder.BeginTransactionAsync();
        await LockAsync(holder, held, ticketA);

        await using var parallel = await other.BeginTransactionAsync();
        await new NpgsqlCommand("SET LOCAL statement_timeout = '400ms'", other, parallel).ExecuteNonQueryAsync();
        await LockAsync(other, parallel, ticketB);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(f.Database.CoreConnection);
        await connection.OpenAsync();
        return connection;
    }

    private static Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long key)
    {
        var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
        command.Parameters.AddWithValue("key", key);
        return command.ExecuteScalarAsync();
    }

    private static long CoreKey(string? resourceId) =>
        Invoke<long>(typeof(TicketVerificationService), resourceId);

    private static long OrganizerKey(string? resourceId) =>
        Invoke<long>(typeof(MockOrganizer.API.Resale.OrganizerResaleGrpcService), resourceId);

    private static T Invoke<T>(Type type, string? resourceId)
    {
        var method = type.GetMethod("ComputeLockKey", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, "ComputeLockKey");
        try
        {
            return (T)method.Invoke(null, [resourceId])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}

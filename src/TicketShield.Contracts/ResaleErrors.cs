using Google.Protobuf;
using Grpc.Core;
using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Contracts;

public static class ResaleErrors
{
    public const string Trailer = "ticketshield-error-bin";
    public static RpcException Fail(StatusCode status, string code) => new(new Status(status, code),
        new Metadata { { Trailer, new BusinessError { Code = code }.ToByteArray() } });
}

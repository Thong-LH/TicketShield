# Core–MockOrganizer gRPC contract

The authoritative wire contract is `src/TicketShield.Contracts/Protos/Organizer/V1/organizer_resale.proto`. Web and mobile clients call TicketShield.Core over HTTP; only Core calls MockOrganizer over gRPC.

## Confirmed policy

- Money is positive whole VND. `int64` values are dong, not minor units. Publish rejects zero, negatives, fractions and values above the current `numeric(15,2)` database boundary.
- OTP is six decimal digits, expires after five minutes, permits resend after 60 seconds, has at most five failed attempts and at most three resends per verification.
- Only public listings are enabled. Private requests return `PRIVATE_POLICY_NOT_ENABLED`.
- OTP success and the current ticket-valid/unredeemed/unchanged checks occur in the same MockOrganizer transaction that acquires the durable resale lock.
- OTP expiry, abandoned-verification expiry, lock lifecycle and listing lifecycle are separate concepts.

## HTTP API exposed by Core

All mutations require an `Idempotency-Key` header containing a canonical UUID. Authenticated routes take seller identity from the JWT `sub` claim.

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/ticket-verifications` | Start OTP verification; body `{ "ticketCode": "..." }` |
| POST | `/api/ticket-verifications/{id}/resend` | Resend after cooldown |
| POST | `/api/ticket-verifications/{id}/confirm` | Confirm; body `{ "otp": "123456" }` |
| GET | `/api/ticket-verifications/{id}` | Read/reconcile seller-owned verification |
| POST | `/api/resale-listings` | Publish; body `{ "verificationId": "...", "resalePrice": 2500000, "isPrivate": false }` |
| GET | `/api/resale-listings?page=1&size=20` | Public marketplace; never returns ticket code or lock reference |
| POST | `/api/ticket-verifications/{id}/cancel-listing` | Fence listing, close verification and release the exact lock |
| POST | `/api/ticket-verifications/{id}/close` | Close an abandoned verification and release any acquired lock |

Pending operations return HTTP 202 with their operation reference. A timeout does not mean failure; the recovery worker queries the durable Mock operation receipt. Confirm requests can be safely retried with the same key and OTP because no raw OTP is persisted.

## Configuration

The feature is off unless `OrganizerResale:Enabled` and `OrganizerGrpc:Enabled` are true. Use environment variables or User Secrets for API keys, HMAC keys, JWT signing material and SMTP credentials; never commit real credentials.

In Development, MockOrganizer adds an HTTP/2-only loopback endpoint on port 5002 for gRPC while its HTTP/Swagger endpoint remains on port 5001. Plaintext gRPC is accepted only on Development loopback. Other environments require an HTTPS HTTP/2 Kestrel endpoint and an `https://` Core address.

Mock and Core require stable configured mapping from organizer ticket to external event/tier identifiers, then to Core `EventId`/`TierId`. Names and seat text are display data and never identity keys.

Mappings are non-secret nested configuration and are easiest to provide as JSON or individual `dotnet user-secrets set` keys. Mock example: `OrganizerResale:TicketMappings:ATSH-VIP-888:EventId = concert-2026` and `TierId = vip-zone-a`. Core example under an arbitrary entry name such as `OrganizerGrpc:Mappings:vip`: set `ExternalEventId`, `ExternalTierId`, `EventId` and `TierId`. Core requires exactly one match.

The existing application databases receive one isolated extension table and a separate EF history table per service: `organizer_resale_records` / `__OrganizerResaleMigrationsHistory` and `core_resale_records` / `__CoreResaleMigrationsHistory`. Existing entities, DbContexts, migrations and seeders are unchanged.

## Verification

Run `dotnet test tests/TicketShield.Resale.Tests`. Tests create a native temporary PostgreSQL cluster, two real loopback hosts and an SMTP capture server; they do not use the development databases or send external email. Coverage includes the full public flow, replay and payload mismatch, JWT/gRPC authentication, wrong-attempt durability, resend/expiry, invalid/used tickets, owner change, concurrent locks, late-request fencing, stale release, recovery after a lost response, SMTP rejection and exact VND conversion.

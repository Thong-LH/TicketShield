using Microsoft.EntityFrameworkCore;
using TicketShield.Settlement.API.Data;
using TicketShield.Settlement.API.Gateway;
using TicketShield.Settlement.API.Payouts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var connectionString = builder.Configuration.GetConnectionString("Settlement")
    ?? "Host=localhost;Port=5432;Database=settlement_db;Username=postgres;Password=12345";
builder.Services.AddDbContext<SettlementDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<ISettlementGateway, MockNapasPayoutGateway>();
builder.Services.AddHttpClient<ICorePayoutReporter, HttpCorePayoutReporter>(client =>
{
    var core = builder.Configuration["Settlement:CoreBaseUrl"] ?? "http://localhost:5003";
    client.BaseAddress = new Uri(core.TrimEnd('/') + "/");
});
builder.Services.AddScoped<SettlementPayoutService>();
builder.Services.AddHostedService<SettlementRetryWorker>();

var app = builder.Build();
var secret = app.Configuration["Settlement:SharedSecret"] ?? "";
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (!context.Request.Headers.TryGetValue("X-Settlement-Key", out var provided) || provided != secret)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }

    await next();
});
app.MapControllers();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SettlementDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program;

using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Resale;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
var resaleEnabled = builder.Services.AddOrganizerResale(builder.Configuration);
if (resaleEnabled && builder.Environment.IsDevelopment())
{
    var httpPort = builder.Configuration.GetValue("HttpPort", 5001);
    var grpcPort = builder.Configuration.GetValue("OrganizerResale:DevelopmentGrpcPort", 5002);
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.ListenLocalhost(httpPort, endpoint => endpoint.Protocols = HttpProtocols.Http1AndHttp2);
        k.ListenLocalhost(grpcPort, endpoint => endpoint.Protocols = HttpProtocols.Http2);
    });
}

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=organizer_db;Username=postgres;Password=12345";

builder.Services.AddDbContext<OrganizerDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();
if (resaleEnabled)
{
    using var resaleScope = app.Services.CreateScope();
    await resaleScope.ServiceProvider.GetRequiredService<ResaleStore>().Database.MigrateAsync();
    app.MapGrpcService<OrganizerResaleGrpcService>();
}

// Auto-migrate and seed database on startup (Zero-CLI needed for teammates)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = services.GetRequiredService<OrganizerDbContext>();
        await OrganizerDatabaseSeeder.SeedOrganizerAsync(dbContext);
        logger.LogInformation("Organizer database auto-migrated and verified successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while auto-migrating Organizer database.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Content(MockOrganizer.API.Portal.PortalHtml.Html, "text/html"));
app.MapGet("/portal", () => Results.Content(MockOrganizer.API.Portal.PortalHtml.Html, "text/html"));

app.Run();

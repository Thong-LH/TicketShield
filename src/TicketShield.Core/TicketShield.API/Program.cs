using TicketShield.API.Middlewares;
using TicketShield.Application;
using TicketShield.Infrastructure;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Resale;
using TicketShield.API.Resale;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
var resaleEnabled = builder.Services.AddCoreResale(builder.Configuration, builder.Environment);
if (resaleEnabled) builder.Services.AddResaleAuthentication(builder.Configuration);

var app = builder.Build();
if (resaleEnabled)
{
    using var resaleScope = app.Services.CreateScope();
    await resaleScope.ServiceProvider.GetRequiredService<CoreResaleStore>().Database.MigrateAsync();
}

// Auto-migrate and seed database on startup (Zero-CLI needed for teammates)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = services.GetRequiredService<TicketShieldDbContext>();
        await DatabaseSeeder.SeedTicketShieldAsync(dbContext);
        logger.LogInformation("TicketShield database auto-migrated and verified successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while auto-migrating TicketShield database.");
    }
}

// Configure the HTTP request pipeline.
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
if (resaleEnabled) app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

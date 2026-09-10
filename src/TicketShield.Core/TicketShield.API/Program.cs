using TicketShield.API.Middlewares;
using TicketShield.Application;
using TicketShield.Infrastructure;
using TicketShield.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TicketShield Core API",
        Version = "v1",
        Description = "API cho nền tảng bán lại vé P2P an toàn TicketShield AI (Sprint MF-02)"
    });

    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

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
app.UseAuthorization();
app.MapControllers();

app.Run();

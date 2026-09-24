var builder = WebApplication.CreateBuilder(args);

// Centralized CORS Policy for Frontend applications
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "https://localhost:3000",
                "http://localhost:5173",
                "https://localhost:5173",
                "http://localhost:4173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure YARP Reverse Proxy from appsettings
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseWebSockets();

// Health check / Gateway status endpoint
app.MapGet("/", () => Results.Ok(new
{
    service = "TicketShield API Gateway (YARP)",
    status = "Healthy",
    port = 5000,
    routes = new[]
    {
        "/api/v1/auth/** -> TicketShield.Identity (:5002)",
        "/api/v1/**      -> TicketShield.TradingCore (:5003)",
        "/hubs/**        -> TicketShield.TradingCore SignalR (:5003)"
    }
}));

app.MapReverseProxy();

app.Run();

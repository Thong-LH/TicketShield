using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TicketShield.API.Middlewares;
using TicketShield.API.Services;
using TicketShield.Application;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Infrastructure;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Resale;
using TicketShield.API.Resale;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT Support and XML Comments
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TicketShield Core API",
        Version = "v1",
        Description = "API cho nền tảng bán lại vé P2P an toàn TicketShield AI (Sprint MF-02 & Epic-1 Auth)"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Format: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Clean Architecture Layers
// Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
var resaleEnabled = builder.Services.AddCoreResale(builder.Configuration, builder.Environment);
if (resaleEnabled && !string.IsNullOrWhiteSpace(builder.Configuration["ResaleJwt:SigningKey"]))
{
    builder.Services.AddResaleAuthentication(builder.Configuration);
}

// Current User & HttpContext
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// JWT Authentication Setup (Default Scheme for Core Platform)
if (!resaleEnabled || string.IsNullOrWhiteSpace(builder.Configuration["ResaleJwt:SigningKey"]))
{
    var jwtSecret = builder.Configuration["JwtSettings:Secret"] ?? "TicketShieldSuperSecretSecurityKeyForCapstoneProject2026";
    var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "TicketShield";
    var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "TicketShieldApp";

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });
}

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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

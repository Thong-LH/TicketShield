using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TicketShield.API.Hubs;
using TicketShield.API.Middlewares;
using TicketShield.API.Services;
using TicketShield.Application;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Infrastructure;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Resale;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// CORS Configuration for Frontend Development
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
        Description = "Nhập trực tiếp mã JWT Token của bạn vào đây (Swagger sẽ tự động thêm 'Bearer ').",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
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
builder.Services.Configure<TicketShield.Application.Common.Configurations.VietQrSettings>(
    builder.Configuration.GetSection(TicketShield.Application.Common.Configurations.VietQrSettings.SectionName));
builder.Services.Configure<TicketShield.Application.Common.Configurations.SmtpSettings>(
    builder.Configuration.GetSection(TicketShield.Application.Common.Configurations.SmtpSettings.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCoreResale(builder.Configuration, builder.Environment);

// Current User & HttpContext
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Unified JWT Authentication Setup (Single Scheme shared with TicketShield.Identity)
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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromSeconds(15)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddScoped<IPaymentRealtimeNotifier, SignalRPaymentRealtimeNotifier>();

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
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<PaymentHub>("/hubs/payment");

app.Run();

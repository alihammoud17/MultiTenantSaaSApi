using Application.Services;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Presentation.Authorization;
using Presentation.Middleware;
using Presentation.RateLimiting;
using Presentation.Observability;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Logging.Configure(options => options.ActivityTrackingOptions =
    Microsoft.Extensions.Logging.ActivityTrackingOptions.SpanId |
    Microsoft.Extensions.Logging.ActivityTrackingOptions.TraceId |
    Microsoft.Extensions.Logging.ActivityTrackingOptions.ParentId);

builder.Services.AddSingleton<ApiObservability>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Tenant Context (scoped per request)
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IRequestTenantResolutionCache, RequestTenantResolutionCache>();
builder.Services.AddHttpContextAccessor();
// JWT Service
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthOrchestrationService, AuthOrchestrationService>();

// Refresh Token Service
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IIdentityLifecycleService, IdentityLifecycleService>();
builder.Services.AddScoped<IIdentityNotificationService, IdentityNotificationService>();
builder.Services.AddScoped<IMfaService, MfaService>();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration["Redis:ConnectionString"];

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Redis connection string is not configured");
    }

    var options = ConfigurationOptions.Parse(connectionString);
    options.AbortOnConnectFail = false;

    return ConnectionMultiplexer.Connect(options);
});

// Rate Limit
builder.Services.AddScoped<IRateLimitService, RateLimitService>();

// Audit
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IBillingCallbackProcessor, BillingCallbackProcessor>();
builder.Services.AddSingleton<IInternalRequestSignatureValidator, InternalRequestSignatureValidator>();
builder.Services.AddScoped<IEntitlementEvaluator, EntitlementEvaluator>();
builder.Services.AddScoped<IEntitlementEnforcer, EntitlementEnforcer>();
builder.Services.AddScoped<IUsageAnalyticsService, UsageAnalyticsService>();
builder.Services.AddScoped<IOutboundWebhookPublisher, OutboundWebhookPublisher>();
builder.Services.AddHttpClient("outbound-webhooks");
builder.Services.AddHostedService<OutboundWebhookDeliveryDispatcher>();

// RBAC
builder.Services.AddScoped<IRbacAuthorizationService, RbacAuthorizationService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };
    });

builder.Services.AddRbacAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthRateLimitPolicyNames.UnauthenticatedAuthEndpoints, httpContext =>
    {
        var hostEnvironment = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (hostEnvironment.IsEnvironment("Testing"))
        {
            return RateLimitPartition.GetNoLimiter("testing-auth-rate-limit-bypass");
        }

        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: remoteIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Multi-Tenant SaaS API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter JWT Bearer token only"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy())
    .AddCheck<DatabaseHealthCheck>("database");


var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "V1");
    });
}

app.UseHttpsRedirection();
app.UseMiddleware<RequestObservabilityMiddleware>();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            service = ApiObservability.ServiceName,
            correlationId = context.TraceIdentifier,
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    duration = entry.Value.Duration.TotalMilliseconds,
                    description = entry.Value.Description
                })
        }));
    }
});
app.MapGet("/metrics", (ApiObservability observability, HttpContext context) => Results.Json(new
{
    correlationId = context.TraceIdentifier,
    generatedAtUtc = DateTime.UtcNow,
    metrics = observability.GetSnapshot()
}));

app.Run();

public partial class Program { }

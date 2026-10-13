using System.IO;
using System.Text;
using System.Threading.RateLimiting;
using ContentEmpireSystem.Api;
using ContentEmpireSystem.Api.Middleware;
using ContentEmpireSystem.Infrastructure;
using ContentEmpireSystem.Infrastructure.Auth;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

DotEnvLoader.LoadIfPresent();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    // A failed production job must not tear down the whole API host.
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 200;
    options.Limits.MaxConcurrentUpgradedConnections = 100;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            ContentEmpireSystem.Api.Helpers.ApiErrorWriter.ToBody(
                ContentEmpireSystem.Core.OperationHints.RateLimited(), context.HttpContext), token);
    };

    options.AddPolicy("api", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    options.AddPolicy("heavy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
});

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ContentEmpireSystem.Api.Filters.IdempotentAttribute>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 500_000_000;
});
builder.Services.AddOpenApi();
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromMinutes(10) };
    options.AddPolicy("ai-generate", new RequestTimeoutPolicy { Timeout = TimeSpan.FromMinutes(12) });
    options.AddPolicy("wan-video", new RequestTimeoutPolicy { Timeout = TimeSpan.FromMinutes(90) });
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddInfrastructure(builder.Environment.ContentRootPath, builder.Configuration);

var jwtOptions = builder.Configuration.GetSection("Auth:Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Auth:Jwt configuration is required.");
if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
    throw new InvalidOperationException("Auth:Jwt:Key must be at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (!string.IsNullOrEmpty(context.Token)
                    || !context.Request.Query.TryGetValue("access_token", out var token))
                {
                    return Task.CompletedTask;
                }

                var path = context.Request.Path.Value ?? "";
                var isActivityStream = context.Request.Path.StartsWithSegments("/api/system/activity/stream");
                var isProjectMedia = path.Contains("/images/", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith("/file", StringComparison.OrdinalIgnoreCase);
                var isProjectVideo = path.EndsWith("/video/file", StringComparison.OrdinalIgnoreCase);

                if (isActivityStream || isProjectMedia || isProjectVideo)
                    context.Token = token;

                return Task.CompletedTask;
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<DatabaseReadinessMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRequestTimeouts();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers().RequireRateLimiting("api");

app.MapGet("/", () => Results.Redirect("/index.html"));

app.MapGet("/health", (IDatabaseBootstrapState dbState) =>
{
    if (dbState.IsFailed)
    {
        return Results.Json(new
        {
            status = "unhealthy",
            provider = dbState.Provider,
            error = dbState.FailureMessage
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!dbState.IsReady)
    {
        return Results.Json(new
        {
            status = "starting",
            provider = dbState.Provider
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "healthy",
        provider = dbState.Provider,
        bootstrapMs = dbState.Elapsed?.TotalMilliseconds
    });
});

try
{
    app.Run();
}
catch (IOException ex) when (ex.InnerException is AddressInUseException
    || ex.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Port 5225 is already in use. Stop the other ContentEmpire instance, then run again.");
    Console.Error.WriteLine("PowerShell: Get-NetTCPConnection -LocalPort 5225 | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }");
    Environment.Exit(1);
}

public partial class Program { }

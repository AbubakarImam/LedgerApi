using LedgerApi.Auditing;
using LedgerApi.Authorization;
using LedgerApi.Configuration;
using LedgerApi.Data;
using LedgerApi.Middleware;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

// Bootstrap logger: records errors that happen before configuration is loaded (e.g. a broken appsettings file).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replace the default logging with Serilog, configured from the "Serilog" section of appsettings.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers(options => options.Filters.Add<IdempotencyKeyLoggingFilter>());
    // Lowercase route URLs in generated links and the Swagger docs (/api/transfers, not /api/Transfers).
    // Matching was already case-insensitive, so existing callers are unaffected.
    builder.Services.AddRouting(options => options.LowercaseUrls = true);
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        // Adds an "Authorize" button to Swagger UI that sends the X-Api-Key header.
        options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationHandler.HeaderName,
            Description = "API key issued to the calling service."
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyAuthenticationHandler.SchemeName }
            }] = []
        });
    });

    // Authentication: who is calling (API key -> client id + scopes).
    builder.Services.AddOptions<ApiKeyOptions>()
        .Bind(builder.Configuration.GetSection("ApiKeys"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<ApiKeyOptions>, ApiKeyOptionsValidator>();

    builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

    // Authorization: one policy per scope, plus a fallback so an endpoint without [Authorize] is never public.
    builder.Services.AddAuthorization(options =>
    {
        foreach (var scope in LedgerScopes.All)
            options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim(LedgerScopes.ClaimType, scope));

        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });

    builder.Services.AddDbContext<LedgerDbContext>(options => options
        .UseNpgsql(builder.Configuration.GetConnectionString("LedgerDb"))
        .UseSnakeCaseNamingConvention());

    builder.Services.AddOptions<FundingOptions>()
        .Bind(builder.Configuration.GetSection("Funding"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<FundingOptions>, FundingOptionsValidator>();

    // AuditLogger reads the actor, IP, user agent and correlation id of the current request.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentClient, HttpCurrentClient>();

    // Health checks for the hosting platform: "ready" includes the database, "live" checks only the process.
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<LedgerDbContext>("database", tags: ["ready"]);

    builder.Services.AddScoped<ITransferService, TransferService>();
    builder.Services.AddScoped<IReversalService, ReversalService>();
    builder.Services.AddScoped<IAccountService, AccountService>();
    builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
    builder.Services.AddScoped<IDepositService, DepositService>();
    builder.Services.AddScoped<IWithdrawalService, WithdrawalService>();
    builder.Services.AddScoped<IAuditLogger, AuditLogger>();

    builder.Services.AddScoped<CreateAccountRequestValidator>();
    builder.Services.AddScoped<TransferRequestValidator>();
    builder.Services.AddScoped<DepositRequestValidator>();
    builder.Services.AddScoped<ReversalRequestValidator>();
    builder.Services.AddScoped<WithdrawalRequestValidator>();

    var app = builder.Build();

    // First in the pipeline, so every later log line (including the request summary) has the id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // One structured log line per request: method, path, status code and elapsed time, plus these extras.
    app.UseSerilogRequestLogging(options =>
    {
        // Platforms probe /health every few seconds; log those only when they fail.
        options.GetLevel = (httpContext, _, exception) =>
            exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
            : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
            : LogEventLevel.Information;

        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserId", httpContext.User.Identity?.Name ?? "anonymous");
            diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    // Configure the HTTP request pipeline.
    // Swagger: always in Development; elsewhere only when Swagger:Enabled is true (the public showcase,
    // decision #41). The docs are public, but every call made from them still needs an API key.
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
    {
        app.UseSwagger();
        // Remember the key pasted into Authorize across page reloads (stored in the visitor's own browser).
        app.UseSwaggerUI(options => options.EnablePersistAuthorization());
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // No UseHttpsRedirection: a redirect cannot protect an API key that was already sent over HTTP.
    // TLS belongs to the deployment (proxy/ingress or Kestrel), with no plain-HTTP port exposed (decision #36).

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Anonymous on purpose: platforms probe without an API key. They answer only "Healthy" or "Unhealthy",
    // never details. Live = the process responds (restart if not); ready = it can reach the database
    // (send traffic only if so).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Ledger API terminated unexpectedly");
    // Non-zero so Docker, Kubernetes and CI see a failed start rather than a clean stop.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Lets the test project start the app in memory with WebApplicationFactory<Program>.
public partial class Program;

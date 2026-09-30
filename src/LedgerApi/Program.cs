using LedgerApi.Auditing;
using LedgerApi.Authorization;
using LedgerApi.Configuration;
using LedgerApi.Data;
using LedgerApi.Middleware;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddDbContext<LedgerDbContext>(options => options
        .UseNpgsql(builder.Configuration.GetConnectionString("LedgerDb"))
        .UseSnakeCaseNamingConvention());

    builder.Services.Configure<FundingOptions>(builder.Configuration.GetSection("Funding"));

    // AuditLogger reads the actor, IP, user agent and correlation id of the current request.
    builder.Services.AddHttpContextAccessor();

    builder.Services.AddScoped<ITransferService, TransferService>();
    builder.Services.AddScoped<IReversalService, ReversalService>();
    builder.Services.AddScoped<IAccountService, AccountService>();
    builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
    builder.Services.AddScoped<IDepositService, DepositService>();
    builder.Services.AddScoped<IWithdrawalService, WithdrawalService>();
    builder.Services.AddScoped<IMandateService, MandateService>();
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
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserId", httpContext.User.Identity?.Name ?? "anonymous");
            diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Ledger API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

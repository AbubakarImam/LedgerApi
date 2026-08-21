using LedgerApi.Auditing;
using LedgerApi.Authorization;
using LedgerApi.Data;
using LedgerApi.Middleware;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<LedgerDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("LedgerDb"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IReversalService, ReversalService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddScoped<IDepositService, DepositService>();
builder.Services.AddScoped<IMandateService, MandateService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

builder.Services.AddScoped<CreateAccountRequestValidator>();

var app = builder.Build();

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

using Api.GrpcServices;
using Api.Middleware;
using Application;
using Infrastructure;
using Infrastructure.Persistence.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<ExceptionHandlingInterceptor>();
    options.Interceptors.Add<ApiKeyAuthInterceptor>();
});

builder.Services.AddSingleton<ExceptionHandlingInterceptor>();
builder.Services.AddSingleton<ApiKeyAuthInterceptor>();

builder.Services.AddGrpcHealthChecks(o =>
    {
        o.Services.MapService("", r => r.Tags.Contains("live"));
        o.Services.MapService("ready", r => r.Tags.Contains("ready"));
    })
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!,
        name: "postgres", tags: ["ready"]);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGrpcService<AccountingGrpcService>();
app.MapGrpcService<AdminAccountingGrpcService>();
app.MapGrpcHealthChecksService();

app.Run();

public partial class Program;

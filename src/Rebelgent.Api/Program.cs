using Rebelgent.Contracts.Responses;
using Rebelgent.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRebelgent();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/health", () => new HealthResponse("healthy", "Rebelgent"));

app.MapGet("/api/system/info", (IWebHostEnvironment env) =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    return new SystemInfoResponse("Rebelgent", version, env.EnvironmentName);
});

app.Run();

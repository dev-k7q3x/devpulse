using System.Text.Json.Serialization;
using DevPulse.Api.Errors;
using DevPulse.Api.RateLimiting;
using DevPulse.Api.Validation;
using DevPulse.Application.Diagnostics;
using DevPulse.Application.Errors;
using DevPulse.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    // Framework domyślnie wpisuje link do RFC 9110 – zastępujemy go slugiem z kontraktu (plan 03, tabela 0.1).
    // Typ ustawiony jawnie w kodzie (np. github-rate-limited) zostaje bez zmian.
    var problem = context.ProblemDetails;
    var isDefaultType = problem.Type is null || problem.Type.StartsWith("https://tools.ietf.org/", StringComparison.Ordinal);
    if (isDefaultType && problem.Status is int status && ProblemTypes.ForStatus(status) is string slug)
    {
        problem.Type = slug;
    }
});
builder.Services.AddExceptionHandler<DevPulseExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();
        options.Conventions.Add(new DefaultRateLimitingConvention());
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddDevPulseRateLimiting(builder.Configuration);

// Composition root: referencja Api → Infrastructure istnieje WYŁĄCZNIE dla rejestracji DI.
// Kontrolery i reszta Api używają tylko interfejsów z DevPulse.Application – nigdy typów z Infrastructure.
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
// Uwierzytelnianie (UseAuthentication/UseAuthorization) musi stać przed UseRateLimiter – partycje liczone per userId.
app.UseRateLimiter();

app.MapControllers();

// Liveness dla healthchecka Railway: żaden check zależności – awaria bazy nie restartuje kontenera.
var livenessOptions = new HealthCheckOptions { Predicate = _ => false };
app.MapHealthChecks("/health", livenessOptions).AllowAnonymous();

// Readiness: PostgreSQL i serwer Hangfire. Odpowiedź tylko Healthy/Unhealthy, bez szczegółów.
var readinessOptions = new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Ready) };
app.MapHealthChecks("/health/ready", readinessOptions).AllowAnonymous();

// Nieznana ścieżka /api/* to 404 ProblemDetails, nie index.html SPA.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, type: ProblemTypes.NotFound));
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;

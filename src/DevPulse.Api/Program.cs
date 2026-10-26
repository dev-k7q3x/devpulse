using System.Text.Json.Serialization;
using DevPulse.Api.Auth;
using DevPulse.Api.Errors;
using DevPulse.Api.Logging;
using DevPulse.Api.OpenApi;
using DevPulse.Api.RateLimiting;
using DevPulse.Api.Security;
using DevPulse.Api.Validation;
using DevPulse.Application.Diagnostics;
using DevPulse.Application.Errors;
using DevPulse.Infrastructure.DependencyInjection;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .ApplyDevPulseDestructuring());

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
        options.Filters.Add<AntiforgeryValidationFilter>();
        options.Filters.Add<ValidationFilter>();
        options.Conventions.Add(new DefaultRateLimitingConvention());
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(options => options.AddOperationTransformer<CamelCaseQueryParametersTransformer>());
builder.Services.AddDevPulseRateLimiting(builder.Configuration);
builder.Services.AddDevPulseAuthentication(builder.Configuration, builder.Environment);

// Railway terminuje TLS na proxy: bez tego rate limiter widzi IP proxy, a callback /signin-github dostaje scheme http.
// Adresy proxy Railway nie są stałe, więc ufamy każdemu; ForwardLimit = 1 bierze tylko wpis dopisany przez proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Composition root: referencja Api → Infrastructure istnieje WYŁĄCZNIE dla rejestracji DI.
// Kontrolery i reszta Api używają tylko interfejsów z DevPulse.Application – nigdy typów z Infrastructure.
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseDevPulseSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    // Po ForwardedHeaders: proxy Railway terminuje TLS, scheme https pochodzi z X-Forwarded-Proto.
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
// Uwierzytelnianie przed UseRateLimiter – partycje liczone per userId z cookie sesji.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Panel Hangfire tylko dla adminów (Admin:Logins), także na produkcji. Domyślny filtr Hangfire (tylko localhost)
// zastępuje autoryzacja endpointu: niezalogowany → 401, zalogowany bez uprawnień → 403.
app.MapHangfireDashboard(AuthServiceCollectionExtensions.HangfireDashboardPath, new DashboardOptions
{
    Authorization = [],
    AppPath = "/",
    DashboardTitle = "DevPulse – Hangfire",
    DisplayStorageConnectionString = false,
})
    .RequireAuthorization(AuthorizationPolicies.Admin);

// Liveness dla healthchecka Railway: żaden check zależności – awaria bazy nie restartuje kontenera.
var livenessOptions = new HealthCheckOptions { Predicate = _ => false };
app.MapHealthChecks("/health", livenessOptions).AllowAnonymous();

// Readiness: PostgreSQL i serwer Hangfire. Odpowiedź tylko Healthy/Unhealthy, bez szczegółów.
var readinessOptions = new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Ready) };
app.MapHealthChecks("/health/ready", readinessOptions).AllowAnonymous();

// Nieznana ścieżka /api/* to 404 ProblemDetails, nie index.html SPA.
// Fallbacki są anonimowe: SPA (w tym /login) musi się załadować bez sesji, a 404 niczego nie zdradza.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, type: ProblemTypes.NotFound))
    .AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;

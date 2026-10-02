using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessCard.Api.Authentication;
using BusinessCard.Api.Errors;
using BusinessCard.Api.Health;
using BusinessCard.Api.Hosting;
using BusinessCard.Api.Observability;
using BusinessCard.Api.Validation;
using BusinessCard.Application;
using BusinessCard.Infrastructure;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logging: configured entirely from the "Serilog" section. No bootstrap logger, because it can only
// be frozen once per process, which breaks integration tests that host the API several times.
// preserveStaticLogger: each host uses its own logger instead of the process-wide static
// Log.Logger, so nothing depends on global mutable state (and parallel test hosts stay apart).
builder.Services.AddSerilog(
    (services, logger) => logger
        .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
        .ReadFrom.Services(services),
    preserveStaticLogger: true);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEntraAuthentication(builder.Configuration);
builder.Services.AddPerUserRateLimiting(builder.Configuration);
builder.Services.AddMetricsExport(builder.Configuration);

builder.Services.AddApiProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services
    .AddControllers(options => options.Filters.Add<FluentValidationFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Business Card API", Version = "v1" });

    // Paste an Entra access token for the API scope (see README → "Calling the API locally").
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Entra ID access token with the access_as_user scope.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

// First: everything after it (logging, HTTPS redirection, rate limiting) sees the real client.
app.UseForwardedHeaders();

// Request logging wraps the exception handler so it records the final status code, not the raw exception.
app.UseApiRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseTraceIdResponseHeader();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
// After authentication (partitions by user), before authorization so floods are cut early.
app.UseRateLimiter();
app.UseAuthorization();

app.MapApiHealthChecks();
app.MapControllers();

app.Run();

// Exposed so WebApplicationFactory<Program> can host the API in integration tests.
public partial class Program;

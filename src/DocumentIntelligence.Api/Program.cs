using System.Diagnostics;
using System.Text.Json.Serialization;
using DocumentIntelligence.Api.Endpoints;
using DocumentIntelligence.Api.Hosting;
using DocumentIntelligence.Api.Ingestion;
using DocumentIntelligence.Api.Tenancy;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.Infrastructure;
using DocumentIntelligence.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging();
builder.AddTracing();

builder.Services.AddDocumentIntelligence(builder.Configuration);
builder.Services.AddIngestionJobPersistence(builder.Configuration);
builder.Services.AddBackgroundIngestion();
builder.Services.AddSingleton<ITenantContext, DefaultTenantContext>();
builder.Services.AddServiceHealthChecks();

builder.Services.AddExceptionHandler<UpstreamServiceExceptionHandler>();
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Browsers on another origin (for example "ng serve" without its proxy) need CORS.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(options =>
    options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information);
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapDocumentEndpoints();
app.MapQueryEndpoints();
app.MapServiceHealthChecks();

app.Run();

public partial class Program;

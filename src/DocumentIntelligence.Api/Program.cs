using System.Diagnostics;
using System.Text.Json.Serialization;
using DocumentIntelligence.Api.Endpoints;
using DocumentIntelligence.Api.Hosting;
using DocumentIntelligence.Api.Ingestion;
using DocumentIntelligence.Api.Security;
using DocumentIntelligence.Infrastructure;
using DocumentIntelligence.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging();
builder.AddTracing();
builder.AddApiAuthentication();
builder.AddRequestProtection();

builder.Services.AddDocumentIntelligence(builder.Configuration);
builder.Services.AddIngestionJobPersistence(builder.Configuration);
builder.Services.AddBackgroundIngestion(builder.Configuration);
builder.Services.AddServiceHealthChecks(builder.Configuration);

builder.Services.AddExceptionHandler<UpstreamServiceExceptionHandler>();
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(options =>
    options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information);
// Browsers on another origin (for example "ng serve" without its proxy) need CORS.
app.UseRequestProtection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapAuthEndpoints();
app.MapDocumentEndpoints();
app.MapQueryEndpoints();
app.MapServiceHealthChecks();

app.Run();

public partial class Program;

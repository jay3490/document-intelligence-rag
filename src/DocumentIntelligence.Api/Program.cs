using System.Text.Json.Serialization;
using DocumentIntelligence.Api.Endpoints;
using DocumentIntelligence.Api.Ingestion;
using DocumentIntelligence.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDocumentIntelligence(builder.Configuration);
builder.Services.AddBackgroundIngestion();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapDocumentEndpoints();
app.MapQueryEndpoints();

app.Run();

public partial class Program;

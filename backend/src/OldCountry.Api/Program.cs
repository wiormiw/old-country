using System.Text.Json.Serialization;
using OldCountry.Api.Common.Realtime;
using OldCountry.Api.Features;
using OldCountry.Api.Middleware;
using OldCountry.Api.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPersistence();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    var json = options.SerializerOptions;
    json.Converters.Add(new JsonStringEnumConverter());
    json.NumberHandling = JsonNumberHandling.Strict;
    json.PropertyNameCaseInsensitive = false;
    json.AllowDuplicateProperties = false;
    json.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddFeatures();
builder.Services.AddRealtime(builder.Environment);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

await app.MigrateDatabaseAsync();

app.MapDefaultEndpoints();
app.MapFeatures();
app.MapRealtime();

await app.RunAsync();

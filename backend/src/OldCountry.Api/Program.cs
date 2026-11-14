using System.Text.Json.Serialization;
using OldCountry.Api.Endpoints;
using OldCountry.Api.Http;
using OldCountry.Api.Realtime;
using OldCountry.Application;
using OldCountry.Infrastructure;
using OldCountry.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.Services.AddApplication();

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
app.MapApiEndpoints();
app.MapRealtime();

await app.RunAsync();

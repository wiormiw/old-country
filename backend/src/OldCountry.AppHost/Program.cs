var builder = DistributedApplication.CreateBuilder(args);

// Latest major (18), floating over its patch releases. Keep in sync with the test container in
// tests/OldCountry.Api.Tests/Integration/ApiFactory.cs.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// The name must match PersistenceRegistration.ConnectionStringName in the API.
var database = postgres.AddDatabase("oldcountry");

builder.AddProject<Projects.OldCountry_Api>("api")
    .WithReference(database)
    .WaitFor(database);

await builder.Build().RunAsync();

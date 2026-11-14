// Local development: Postgres in a persistent container, the API, and the Aspire dashboard.
// The ports do not change, so the server address of the apps stays the same:
//   API       http://localhost:5000, and http://<LAN address>:5000 from other devices
//   Postgres  localhost:55432, user "postgres", database "oldcountry"
// The Postgres password is in the user secrets of this project, not in the repository:
//   dotnet user-secrets set Parameters:postgres-password <password> --project src/OldCountry.AppHost
//   dotnet user-secrets list --project src/OldCountry.AppHost
// Optional: MusicFolder (user secrets or appsettings) goes to the API as its library folder.

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password", secret: true);

// The latest major version (18), with its newest patch. Keep it the same as the test container in
// tests/OldCountry.Api.Tests/Integration/ApiFactory.cs.
var postgres = builder.AddPostgres("postgres", password: password, port: 55432)
    .WithImageTag("18")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// The name must be the same as PersistenceRegistration.ConnectionStringName in Infrastructure.
var database = postgres.AddDatabase("oldcountry");

// The home network can reach the API, not only this computer. Thus phones and other computers can use it:
// http://<LAN address of this computer>:5000. The firewall must also allow port 5000.
var api = builder.AddProject<Projects.OldCountry_Api>("api")
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "0.0.0.0")
    .WithReference(database)
    .WaitFor(database);

if (builder.Configuration["MusicFolder"] is { Length: > 0 } musicFolder)
{
    api.WithEnvironment("Library__Roots__0", musicFolder);
}

await builder.Build().RunAsync();

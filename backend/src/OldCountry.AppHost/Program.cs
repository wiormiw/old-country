// Local development: Postgres in a persistent container plus the API, with the Aspire dashboard.
// Fixed ports so the apps' server address never changes:
//   API       http://localhost:5000, and http://<LAN address>:5000 from other devices
//   Postgres  localhost:55432, user "postgres", database "oldcountry"
// The Postgres password lives in this project's user secrets, not in the repo:
//   dotnet user-secrets set Parameters:postgres-password <password> --project src/OldCountry.AppHost
//   dotnet user-secrets list --project src/OldCountry.AppHost
// Optional: MusicFolder (user secrets or appsettings) is passed to the API as its library folder.

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password", secret: true);

// Latest major (18), floating over its patch releases. Keep in sync with the test container in
// tests/OldCountry.Api.Tests/Integration/ApiFactory.cs.
var postgres = builder.AddPostgres("postgres", password: password, port: 55432)
    .WithImageTag("18")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// The name must match PersistenceRegistration.ConnectionStringName in the API.
var database = postgres.AddDatabase("oldcountry");

// Reachable from the home network, not just this computer, so phones and other computers can use
// it: http://<this computer's LAN address>:5000. (The firewall must allow port 5000 too.)
var api = builder.AddProject<Projects.OldCountry_Api>("api")
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "0.0.0.0")
    .WithReference(database)
    .WaitFor(database);

if (builder.Configuration["MusicFolder"] is { Length: > 0 } musicFolder)
{
    api.WithEnvironment("Library__Roots__0", musicFolder);
}

await builder.Build().RunAsync();

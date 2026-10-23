using Boekerhouden.Server;

var builder = WebApplication.CreateBuilder(args);
Opstart.ConfigureerDiensten(builder);

var app = builder.Build();
await Opstart.InitialiseerDatabase(app.Services);
Opstart.ConfigureerPijplijn(app);

await app.RunAsync();

/// <summary>Toegankelijk voor WebApplicationFactory in de integratietests.</summary>
public partial class Program;

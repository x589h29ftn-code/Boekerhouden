using Boekerhouden.Server;
using Boekerhouden.Server.Demo;

var builder = WebApplication.CreateBuilder(DemoVulling.Argumenten(args));
Opstart.ConfigureerDiensten(builder);

var app = builder.Build();
await Opstart.InitialiseerDatabase(app.Services);
await DemoVulling.Vul(app.Services, app.Configuration.GetSection("Demo").Get<DemoOpties>() ?? new DemoOpties(), app.Environment.ContentRootPath, app.Logger);
Opstart.ConfigureerPijplijn(app);

await app.RunAsync();

/// <summary>Toegankelijk voor WebApplicationFactory in de integratietests.</summary>
public partial class Program;

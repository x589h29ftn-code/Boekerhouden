using Boekerhouden.Administratie;
using Boekerhouden.Administratie.Bureaublad;

// De Windows-app (bureaublad.json naast de exe) start een eigen database en opent een eigen venster; zie Bureaublad.
var bureaublad = Bureaublad.IsAan(args);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = bureaublad ? AppContext.BaseDirectory : null,
});

LokaleDatabase? database = null;
try
{
    if (bureaublad)
    {
        if (OperatingSystem.IsWindows())
        {
            Console.Title = "Boekerhouden Administratie";
        }

        database = await Bureaublad.Voorbereiden(builder);
    }

    Opstart.ConfigureerDiensten(builder);
    var app = builder.Build();
    await Opstart.InitialiseerDatabase(app.Services);
    Opstart.ConfigureerPijplijn(app);
    if (bureaublad)
    {
        Bureaublad.OpenVenster(app);
    }

    await app.RunAsync();
}
catch (BureaubladException fout) when (bureaublad)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(fout.Message);
    if (!Console.IsInputRedirected)
    {
        Console.Error.WriteLine("Druk op Enter om te sluiten.");
        Console.ReadLine();
    }

    return 1;
}
finally
{
    if (database is not null)
    {
        await database.Stop();
    }
}

return 0;

/// <summary>Toegankelijk voor WebApplicationFactory in de integratietests.</summary>
public partial class Program;

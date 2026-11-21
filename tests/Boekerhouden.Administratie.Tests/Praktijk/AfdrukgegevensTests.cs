using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>Draait alleen met BOEKERHOUDEN_AFDRUKGEGEVENS=&lt;pad.xaf&gt;: een volle administratie als auditfile, om afdrukken in de browser te controleren (scripts/ui/afdrukken.js).</summary>
internal sealed class AfdrukgegevensFactAttribute : FactAttribute
{
    public AfdrukgegevensFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BOEKERHOUDEN_AFDRUKGEGEVENS")))
        {
            Skip = "Zet BOEKERHOUDEN_AFDRUKGEGEVENS op een pad voor de auditfile.";
        }
    }
}

public sealed class AfdrukgegevensTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [AfdrukgegevensFact]
    public async Task Schrijf_een_volle_administratie_als_auditfile()
    {
        var w = await Werkadministratie.Maak(app, "Afdruk Handel B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var toeval = new Random(11);
        for (var i = 0; i < 120; i++)
        {
            var concept = await w.Concept(new DateOnly(2025, 1, 1).AddDays(toeval.Next(360)), Math.Round((decimal)(toeval.NextDouble() * 2_000 + 50), 2));
            Assert.True((await verkoop.MaakDefinitief(w.Wie, w.Id, concept)).IsGelukt);
        }

        for (var i = 0; i < 120; i++)
        {
            var datum = new DateOnly(2025, 1, 1).AddDays(toeval.Next(360));
            var document = await w.InkoopDocument($"RG-A-{i:0000}", datum, 100m + i);
            await w.BoekInkoop(document, $"RG-A-{i:0000}", datum, 100m + i);
        }

        string[] kosten = ["4300", "4310", "4900", "4200", "4800"];
        for (var i = 0; i < 1_500; i++)
        {
            var datum = new DateOnly(2025, 1, 1).AddDays(toeval.Next(365));
            var bedrag = Math.Round((decimal)(toeval.NextDouble() * 500 + 1), 2);
            var boekstuk = await grootboek.VolgendBoekstuk(w.Wie, w.Id, "MEM", 2025);
            Assert.True((await grootboek.Boek(w.Wie, w.Id, new Journaalpost("MEM", boekstuk, datum, $"Kosten {i} met een wat langere omschrijving om de regel te vullen",
                [new(kosten[i % kosten.Length], bedrag), new("1100", -bedrag)]))).IsGelukt);
        }

        var xaf = await app.Dienst<AuditfileDienst>().Maak(w.Wie, w.Id, 2025);
        await File.WriteAllBytesAsync(Environment.GetEnvironmentVariable("BOEKERHOUDEN_AFDRUKGEGEVENS")!, xaf.Inhoud);
    }
}

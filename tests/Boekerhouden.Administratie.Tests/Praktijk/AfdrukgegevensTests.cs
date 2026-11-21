using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;

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

        // BOEKERHOUDEN_AFDRUKSCHAAL vermenigvuldigt alle bedragen, om de afdrukken ook met grote bedragen te zien (bijv. 15:
        // ruim 100.000 per maand op de kostenrekeningen en meer dan een miljoen per jaar).
        var schaal = decimal.TryParse(Environment.GetEnvironmentVariable("BOEKERHOUDEN_AFDRUKSCHAAL"), System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 1m;
        // Een paar verzonnen klanten naast die van de werkadministratie, zodat de openstaande posten per klant iets zeggen.
        var inrichting = app.Dienst<InrichtingDienst>();
        List<string> klanten = [w.Klant];
        foreach (var (naam, plaats) in new[] { ("Kringloop De Ruil B.V.", "Ergens"), ("Hoveniersbedrijf Groen Gras", "Nergens"), ("Stichting Fietsmaatje", "Elders"), ("Koeriersdienst Snel V.O.F.", "Overal") })
        {
            var nieuw = await inrichting.VoegRelatieToe(w.Wie, w.Id, new RelatieInvoer(naam, null, null, null,
                new Klantgegevens(true, "Dorpsstraat 1", "1234 AB", plaats, "NL", Betalingstermijn: 30, Omzetrekening: "8000", Verkoopbtwcode: "V21")), null);
            Assert.True(nieuw.IsGelukt, string.Join("; ", nieuw.Fouten));
            klanten.Add((await inrichting.Relaties(w.Wie, w.Id)).Single(r => r.Id == nieuw.Waarde).Code);
        }

        for (var i = 0; i < 120; i++)
        {
            var datum = new DateOnly(2025, 1, 1).AddDays(toeval.Next(360));
            var bedrag = Math.Round((decimal)(toeval.NextDouble() * 2_000 + 50) * schaal, 2);
            var concept = await verkoop.Bewaar(w.Wie, w.Id, null,
                new Verkoopinvoer(klanten[i % klanten.Count], datum, datum.AddDays(30), "Fietsen", [new Verkoopregel("Fiets", 1, bedrag, "8000", "V21")]));
            Assert.True(concept.IsGelukt, string.Join("; ", concept.Fouten));
            Assert.True((await verkoop.MaakDefinitief(w.Wie, w.Id, concept.Waarde)).IsGelukt);
        }

        for (var i = 0; i < 120; i++)
        {
            var datum = new DateOnly(2025, 1, 1).AddDays(toeval.Next(360));
            var document = await w.InkoopDocument($"RG-A-{i:0000}", datum, (100m + i) * schaal);
            await w.BoekInkoop(document, $"RG-A-{i:0000}", datum, (100m + i) * schaal);
        }

        string[] kosten = ["4300", "4310", "4900", "4200", "4800"];
        for (var i = 0; i < 1_500; i++)
        {
            var datum = new DateOnly(2025, 1, 1).AddDays(toeval.Next(365));
            var bedrag = Math.Round((decimal)(toeval.NextDouble() * 500 + 1) * schaal, 2);
            var boekstuk = await grootboek.VolgendBoekstuk(w.Wie, w.Id, "MEM", 2025);
            Assert.True((await grootboek.Boek(w.Wie, w.Id, new Journaalpost("MEM", boekstuk, datum, $"Kosten {i} met een wat langere omschrijving om de regel te vullen",
                [new(kosten[i % kosten.Length], bedrag), new("1100", -bedrag)]))).IsGelukt);
        }

        var xaf = await app.Dienst<AuditfileDienst>().Maak(w.Wie, w.Id, 2025);
        await File.WriteAllBytesAsync(Environment.GetEnvironmentVariable("BOEKERHOUDEN_AFDRUKGEGEVENS")!, xaf.Inhoud);
    }
}

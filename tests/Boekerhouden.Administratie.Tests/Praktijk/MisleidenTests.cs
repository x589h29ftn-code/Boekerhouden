using Boekerhouden.Administratie.Diensten;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Het leergeheugen bewust misleiden: eerst leert het pakket een leverancier (een paar facturen met de hand), daarna
/// komen facturen die erop lijken maar niet vanzelf geboekt mogen worden: een ander IBAN (fraude), een naam die er
/// bijna op lijkt met andere nummers, een veel hoger bedrag, een ander adres, een factuur aan een ander bedrijf, het
/// bekende IBAN (en adres) onder een andere naam, en dezelfde factuur nog eens. Vanzelf boeken is daar steeds een fout.
/// Bewust niet: een factuur op een andere naam op ons eigen adres (handelsnamen wijken vaak af; zie Inkoopverwerker).
/// </summary>
public sealed class MisleidenTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Wat_op_een_bekende_leverancier_lijkt_wordt_niet_vanzelf_geboekt()
    {
        var w = await Werkadministratie.Maak(app, "Misleiden B.V.");
        var inkoop = app.Dienst<InkoopDienst>();
        var echt = Praktijkset.Groothandel;
        var volgnummer = 0;

        async Task<Inleesuitkomst> Lees(Partijdata afzender, decimal bedrag, Partijdata? aan = null, string? nummer = null)
        {
            nummer ??= $"RG-M-{++volgnummer:000}";
            var f = new Factuurdata(true, afzender, nummer, new DateOnly(2025, 3, 1).AddDays(volgnummer), new DateOnly(2025, 3, 15).AddDays(volgnummer), "Levering",
                [new Regeldata("Fietsen", 1, bedrag, "V21", "7000", "I21")], Formaat.Ubl, Betaalwijze.Nummer);
            return await inkoop.Lees(w.Wie, w.Id, new MemoryStream(Praktijkset.Document(f, afzender, aan ?? Praktijkset.Eigen)), f.Bestandsnaam, null);
        }

        // ---- leren: drie facturen met de hand, daarna gaat een gewone factuur vanzelf
        for (var i = 0; i < 3; i++)
        {
            var gelezen = await Lees(echt, 400m + i * 10);
            if (!gelezen.IsGeboekt)
            {
                Assert.True((await w.BoekInkoop(gelezen.DocumentId!.Value, $"RG-M-{volgnummer:000}", new DateOnly(2025, 3, 1).AddDays(volgnummer), 400m + i * 10)).IsGelukt);
            }
        }

        var gewoon = await Lees(echt, 415m);
        uitvoer.WriteLine($"Gewone factuur na leren: {(gewoon.IsGeboekt ? "vanzelf geboekt" : "niet vanzelf: " + gewoon.Melding)}");
        Assert.True(gewoon.IsGeboekt, "Na drie keer leren hoort een gewone factuur vanzelf te gaan: " + gewoon.Melding);

        // ---- wat er op lijkt
        var gevallen = new List<(string Wat, Partijdata Afzender, decimal Bedrag, Partijdata? Aan, string? Nummer)>
        {
            ("ander IBAN (mogelijk fraude)", echt with { Iban = "NL02ABNA0123456789" }, 420m, null, null),
            ("naam die erop lijkt, andere nummers en IBAN", echt with { Naam = "Rijwielgroothandel Noorden B.V.", BtwNummer = "NL001234567B01", Kvk = "87654321", Iban = "NL91ABNA0417164300" }, 410m, null, null),
            ("vijf keer zo hoog bedrag", echt, 2_100m, null, null),
            ("ander adres", echt with { Straat = "Industrieweg 99", Postcode = "9999 ZZ", Plaats = "Elders" }, 405m, null, null),
            ("factuur aan een ander bedrijf", echt, 410m, Praktijkset.Steen, null),
            ("bekend IBAN onder een andere naam", Praktijkset.Hamer with { Iban = echt.Iban }, 410m, null, null),
            ("bekend IBAN en adres onder een andere naam", Praktijkset.Hamer with { Iban = echt.Iban, Straat = echt.Straat, Postcode = echt.Postcode, Plaats = echt.Plaats }, 410m, null, null),
            ("dezelfde factuur nog eens", echt, 415m, null, $"RG-M-{volgnummer:000}"),
        };

        var fout = new List<string>();
        foreach (var (wat, afzender, bedrag, aan, nummer) in gevallen)
        {
            var gelezen = await Lees(afzender, bedrag, aan, nummer);
            uitvoer.WriteLine($"{wat}: {(gelezen.IsGeboekt ? "VANZELF GEBOEKT" : gelezen.Dubbel ? "dubbel" : "niet vanzelf")} — {gelezen.Melding}");
            if (gelezen.IsGeboekt)
            {
                fout.Add($"{wat}: vanzelf geboekt ({gelezen.Melding}).");
            }
        }

        Assert.True(fout.Count == 0, string.Join("\n", fout));
    }
}

using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Verkoop;
using ClosedXML.Excel;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// De overzichten zoals de gebruiker ze ziet: de openstaande posten als Excel in elke weergave (het totaal sluit op de
/// lijst), en de relaties met klant of leverancier erbij.
/// </summary>
public sealed class OverzichtweergaveTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Openstaande_posten_als_Excel_sluiten_in_elke_weergave_en_relaties_tonen_klant_of_leverancier()
    {
        var w = await Werkadministratie.Maak(app, "Overzichtweergave B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        foreach (var (datum, bedrag) in new[] { (new DateOnly(2025, 3, 1), 1_000m), (new DateOnly(2025, 2, 1), 250.50m) })
        {
            Assert.True((await verkoop.MaakDefinitief(w.Wie, w.Id, await w.Concept(datum, bedrag))).IsGelukt);
        }

        var document = await w.InkoopDocument("L-1", new(2025, 4, 1), 121m);
        Assert.True((await w.BoekInkoop(document, "L-1", new(2025, 4, 1), 121m)).IsGelukt);

        var openposten = app.Dienst<OpenpostenDienst>();
        var lijst = await openposten.Debiteuren(w.Wie, w.Id, new(2025, 12, 31));
        Assert.Equal(1_513.11m, lijst.Openstaand);
        foreach (var weergave in Enum.GetValues<Openpostenweergave>())
        {
            using var boek = new XLWorkbook(new MemoryStream(await openposten.DebiteurenExcel(w.Wie, w.Id, new(2025, 12, 31), null, weergave)));
            var blad = boek.Worksheets.First();
            var generaal = blad.RowsUsed().Single(r => r.Cell(1).GetString() == "Totaal generaal");
            var bedragen = generaal.CellsUsed().Where(c => c.DataType == XLDataType.Number).Select(c => c.GetValue<decimal>()).ToList();
            Assert.Contains(lijst.Openstaand, bedragen);
            if (weergave == Openpostenweergave.Posten)
            {
                // Op datum: februari voor maart, met de klant erbij.
                var datums = blad.Column(1).CellsUsed().Where(c => c.DataType == XLDataType.DateTime).Select(c => c.GetDateTime()).ToList();
                Assert.Equal(datums.Order(), datums);
                Assert.Equal(2, datums.Count);
            }
        }

        // Relaties: de klant heet klant, de leverancier van de inkoopfactuur leverancier; lege kolommen staan er niet.
        var relaties = await app.Dienst<InrichtingDienst>().Relaties(w.Wie, w.Id);
        var klant = Assert.Single(relaties, r => r.Code == w.Klant);
        Assert.True(klant.Klant?.IsKlant);
        Assert.False(klant.IsLeverancier);
        Assert.Single(relaties, r => r.IsLeverancier && r.Klant?.IsKlant != true);
    }
}

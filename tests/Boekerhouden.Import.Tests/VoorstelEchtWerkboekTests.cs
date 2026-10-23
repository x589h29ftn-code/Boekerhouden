using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;
using Boekerhouden.Tests.Gedeeld;
using Xunit.Abstractions;

namespace Boekerhouden.Import.Tests;

/// <summary>Kwaliteit van de voorstellen op het echte RGS-werkboek (alleen lokaal; het werkboek staat niet in git).</summary>
public sealed class VoorstelEchtWerkboekTests(ITestOutputHelper uitvoer)
{
    private static readonly Lazy<(RgsSchema Schema, Voorsteller Bv)> Werkboek = new(() =>
    {
        using var stream = File.OpenRead(EchtRgsWerkboekFactAttribute.Pad!);
        var werkboek = RgsWerkboekLezer.Lees(stream, Path.GetFileName(EchtRgsWerkboekFactAttribute.Pad!));
        var schema = new RgsSchema(werkboek.Versie!, werkboek.Codes);
        return (schema, new Voorsteller(schema, schema.Selecteer(RgsSelectie.BV).Select(schema.Zoek).OfType<RgsCode>()));
    });

    [EchtRgsWerkboekFact]
    public void Gangbare_rekeningen_krijgen_het_juiste_voorstel()
    {
        var verwacht = new Dictionary<string, string>
        {
            ["Kas"] = "BLimKasKas",
            ["Rabobank rekening-courant"] = "BLimBanRba",
            ["Debiteuren"] = "BVorDebHad",
            ["Crediteuren"] = "BSchCreHac",
            ["Te betalen omzetbelasting"] = "BSchBepBtw",
            ["Kantoorbenodigdheden"] = "WBedKanKan",
            ["Telefoonkosten"] = "WBedKanTef",
            ["Inventaris aanschafwaarde"] = "BMvaBeiVvp",
            ["Inventaris cumulatieve afschrijving"] = "BMvaBeiCae",
            ["Afschrijving inventaris"] = "WAfsAmvBei",
        };

        var fout = new List<string>();
        foreach (var (omschrijving, code) in verwacht)
        {
            var voorstel = Werkboek.Value.Bv.StelVoor(omschrijving);
            uitvoer.WriteLine($"{omschrijving,-40} → {voorstel?.Code.Code,-12} {voorstel?.Code.Omschrijving} ({voorstel?.Score:0.00})");
            if (voorstel?.Code.Code != code)
            {
                fout.Add($"{omschrijving}: {voorstel?.Code.Code} i.p.v. {code}");
            }
        }

        Assert.Empty(fout);
    }

    [EchtRgsWerkboekFact]
    public void Overzicht_fictieve_bakkerij()
    {
        foreach (var regel in File.ReadLines(TestHulp.FictiefPad("bv-voorbeeld-bakkerij", "saldibalans-2025.csv")).Skip(4))
        {
            var delen = regel.Split(';');
            var voorstel = Werkboek.Value.Bv.StelVoor(delen[1]);
            uitvoer.WriteLine($"{delen[0],-6} {delen[1],-40} → {voorstel?.Code.Code,-12} {voorstel?.Code.Omschrijving} ({voorstel?.Score:0.00})");
        }
    }
}

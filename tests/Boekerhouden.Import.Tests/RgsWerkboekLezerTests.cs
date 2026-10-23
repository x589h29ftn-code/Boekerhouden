using Boekerhouden.Domain.Rgs;
using Boekerhouden.Tests.Gedeeld;

namespace Boekerhouden.Import.Tests;

public sealed class RgsWerkboekLezerTests
{
    private static RgsWerkboek LeesUittreksel(IEnumerable<object?[]>? extra = null, string werkblad = "Totaal-RGS3.8-def", string bestandsnaam = RgsTestWerkboek.Bestandsnaam) =>
        RgsWerkboekLezer.Lees(new MemoryStream(RgsTestWerkboek.Maak(extra, werkblad)), bestandsnaam);

    [Fact]
    public void Leest_het_hoofdblad_met_codes_filters_en_versie()
    {
        var werkboek = LeesUittreksel();

        Assert.True(werkboek.IsGeslaagd, string.Join("; ", werkboek.Meldingen.Select(m => m.Tekst)));
        Assert.Equal("3.8", werkboek.Versie);
        Assert.Equal("Totaal-RGS3.8-def", werkboek.Werkblad);
        Assert.Equal(RgsTestWerkboek.Regels.Length, werkboek.Codes.Count);
        Assert.Equal(["Basis", "Uitgebr", "EZ/VOF", "ZZP", "WoCo"], werkboek.Kiesfilters);
        Assert.Contains("BV", werkboek.Wegfilters);
        Assert.Contains("EZ/VOF", werkboek.Wegfilters);
    }

    [Fact]
    public void Code_met_ouder_saldokant_en_omslag()
    {
        var bank = LeesUittreksel().Codes.Single(c => c.Code == "BLimBanRba");

        Assert.Equal(4, bank.Niveau);
        Assert.Equal("BLimBan", bank.Ouder);
        Assert.Equal(Saldokant.Debet, bank.Kant);
        Assert.Equal("BSchSakRba", bank.Omslagcode);
        Assert.Contains("Basis", bank.Kiesfilters);
        Assert.False(bank.Inactief);
    }

    [Fact]
    public void Niveau_1_heeft_geen_ouder_en_geen_saldokant()
    {
        var balans = LeesUittreksel().Codes[0];

        Assert.Equal("B", balans.Code);
        Assert.Equal(1, balans.Niveau);
        Assert.Null(balans.Ouder);
        Assert.Null(balans.Kant);
    }

    [Fact]
    public void Kies_en_wegfilter_met_dezelfde_naam_worden_op_positie_onderscheiden()
    {
        var codes = LeesUittreksel().Codes;

        // Privéstortingen: kiesfilter EZ/VOF én wegfilter EZ/VOF (specifiek voor een eenmanszaak). De code zit ook in
        // de basisset; een BV filtert hem weg met de wegfilter EZ/VOF.
        var prive = codes.Single(c => c.Code == "BEivKapPrs");
        Assert.Contains("EZ/VOF", prive.Kiesfilters);
        Assert.Contains("EZ/VOF", prive.Wegfilters);

        // Normale aandelen: specifiek voor een BV.
        Assert.Contains("BV", codes.Single(c => c.Code == "BEivGokGea").Wegfilters);
    }

    [Fact]
    public void Selectie_per_rechtsvorm()
    {
        var schema = new RgsSchema("3.8", LeesUittreksel().Codes);

        var bv = schema.Selecteer(RgsSelectie.BV);
        var ez = schema.Selecteer(RgsSelectie.Eenmanszaak);

        Assert.Contains("BEivGokGea", bv);
        Assert.DoesNotContain("BEivGokGea", ez);
        Assert.Contains("BEivKapPrs", ez);
        Assert.DoesNotContain("BEivKapPrs", bv);
        Assert.Contains("BLimBanRba", bv);
        Assert.Contains("BLimBanRba", ez);
        Assert.DoesNotContain("BMvaBegVvpBeg", bv); // niveau 5
        Assert.All(bv, c => Assert.NotNull(schema.Zoek(c)));
        Assert.All(bv.Where(c => c.Length > 1), c => Assert.Contains(schema.Zoek(c)!.Ouder!, bv)); // boom is compleet
    }

    [Fact]
    public void Inactieve_code_wordt_herkend_en_niet_geselecteerd()
    {
        var werkboek = LeesUittreksel();
        var inactief = Assert.Single(werkboek.Codes, c => c.Inactief);

        Assert.DoesNotContain(inactief.Code, new RgsSchema("3.8", werkboek.Codes).Selecteer(RgsSelectie.BV));
    }

    [Fact]
    public void Dubbele_code_geeft_waarschuwing_en_de_eerste_telt()
    {
        var dubbel = RgsTestWerkboek.Regels.Single(r => (string?)r[0] == "BLimKasKas").ToArray();
        dubbel[4] = "Dubbel";

        var werkboek = LeesUittreksel([dubbel]);

        Assert.True(werkboek.IsGeslaagd);
        Assert.Contains(werkboek.Meldingen, m => m.Tekst.Contains("BLimKasKas", StringComparison.Ordinal) && m.Ernst == MeldingErnst.Waarschuwing);
        Assert.Equal("Kas", werkboek.Codes.Single(c => c.Code == "BLimKasKas").Omschrijving);
    }

    [Fact]
    public void Versie_uit_de_bestandsnaam_als_het_werkblad_die_niet_noemt()
    {
        var werkboek = LeesUittreksel(werkblad: "Totaal", bestandsnaam: "RGS 3.9-def.xlsx");

        // De titelcel noemt "RGS3.8"; die gaat voor de bestandsnaam alleen als het werkblad niets zegt.
        Assert.Equal("3.9", werkboek.Versie);
    }

    [Fact]
    public void Ander_bestand_geeft_een_duidelijke_fout()
    {
        var werkboek = RgsWerkboekLezer.Lees(new MemoryStream("geen excel"u8.ToArray()), "iets.xlsx");

        Assert.False(werkboek.IsGeslaagd);
        Assert.Contains("Excel", Assert.Single(werkboek.Meldingen).Tekst, StringComparison.Ordinal);
    }

    [EchtRgsWerkboekFact]
    public void Echt_werkboek_3_8()
    {
        using var stream = File.OpenRead(EchtRgsWerkboekFactAttribute.Pad!);
        var werkboek = RgsWerkboekLezer.Lees(stream, Path.GetFileName(EchtRgsWerkboekFactAttribute.Pad!));
        var schema = new RgsSchema(werkboek.Versie!, werkboek.Codes);
        var bv = schema.Selecteer(RgsSelectie.BV).Select(schema.Zoek).OfType<RgsCode>().ToList();

        Assert.True(werkboek.IsGeslaagd);
        Assert.Equal(42, werkboek.Codes.Count(c => c.Niveau == 2));
        Assert.Equal(1479, werkboek.Codes.Count(c => c.Niveau == 4));
        Assert.Equal(42, werkboek.Codes.Count(c => c.Inactief));
        Assert.Equal(1050, bv.Count(c => c.Niveau == 4));
    }
}

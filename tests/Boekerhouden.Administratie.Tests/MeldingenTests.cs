using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De bevestiging linksonder met "Ongedaan maken", en het bolletje van een verkoopfactuur in de lijst.</summary>
public sealed class MeldingenTests
{
    [Fact]
    public async Task Ongedaan_maken_gebeurt_een_keer_en_meldt_de_uitkomst()
    {
        var meldingen = new Meldingen();
        var gewijzigd = 0;
        meldingen.Gewijzigd += () => gewijzigd++;
        var keren = 0;

        meldingen.Toon("Factuur geboekt", () => { keren++; return Task.FromResult<string?>(null); });
        var eerste = meldingen.Huidig!;
        Assert.True(eerste.KanOngedaan);

        await meldingen.MaakOngedaan();
        await meldingen.MaakOngedaan();

        Assert.Equal(1, keren);
        Assert.Equal("Ongedaan gemaakt.", meldingen.Huidig!.Tekst);
        Assert.False(meldingen.Huidig.KanOngedaan);
        Assert.True(meldingen.Huidig.Nummer > eerste.Nummer);
        Assert.Equal(2, gewijzigd);
    }

    [Fact]
    public async Task Een_fout_of_geen_toegang_komt_in_de_melding()
    {
        var meldingen = new Meldingen();
        meldingen.Toon("Geboekt", () => Task.FromResult<string?>("De factuur is al betaald."));
        await meldingen.MaakOngedaan();
        Assert.Equal("Niet ongedaan gemaakt: De factuur is al betaald.", meldingen.Huidig!.Tekst);

        meldingen.Toon("Gearchiveerd", () => throw new GeenToegangException());
        await meldingen.MaakOngedaan();
        Assert.StartsWith("Niet ongedaan gemaakt: Deze administratie bestaat niet", meldingen.Huidig!.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Een_nieuwe_melding_vervangt_het_ongedaan_maken_van_de_vorige()
    {
        var meldingen = new Meldingen();
        var keren = 0;
        meldingen.Toon("Eerste", () => { keren++; return Task.FromResult<string?>(null); });
        meldingen.Toon("Tweede");
        Assert.False(meldingen.Huidig!.KanOngedaan);
        await meldingen.MaakOngedaan();
        Assert.Equal(0, keren);
        Assert.Equal("Tweede", meldingen.Huidig.Tekst);

        meldingen.Sluit();
        Assert.Null(meldingen.Huidig);
    }

    [Theory]
    [InlineData(Verkoopstatus.Concept, false, null, "2026-12-01", Verkoopstand.Concept)]
    [InlineData(Verkoopstatus.Concept, true, null, "2026-12-01", Verkoopstand.Ingelezen)]
    [InlineData(Verkoopstatus.Definitief, false, "121", "2026-12-01", Verkoopstand.Open)]
    [InlineData(Verkoopstatus.Definitief, false, "121", "2026-11-07", Verkoopstand.Vervallen)]
    [InlineData(Verkoopstatus.Definitief, false, "121", "2026-11-08", Verkoopstand.Open)]
    [InlineData(Verkoopstatus.Definitief, false, "0", "2026-10-01", Verkoopstand.Betaald)]
    [InlineData(Verkoopstatus.Definitief, false, "-50", "2026-10-01", Verkoopstand.Open)]
    public void Verkoopfactuur_staat_concept_open_vervallen_of_betaald(Verkoopstatus status, bool ingelezen, string? openstaand, string vervaldatum, Verkoopstand verwacht)
    {
        var regel = new Verkooplijstregel(Guid.NewGuid(), status, null, "K1", "Klant", new DateOnly(2026, 10, 1), DateOnly.Parse(vervaldatum, System.Globalization.CultureInfo.InvariantCulture),
            "Advies", 100m, 121m, openstaand is null ? null : decimal.Parse(openstaand, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UtcNow, ingelezen);
        Assert.Equal(verwacht, regel.Stand(new DateOnly(2026, 11, 8)));
    }
}

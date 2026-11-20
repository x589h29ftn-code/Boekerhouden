using Boekerhouden.Administratie.Components.Gedeeld;
using Boekerhouden.Administratie.Diensten;
using Microsoft.AspNetCore.Components;

namespace Boekerhouden.Administratie.Tests;

public sealed class TerugwegTests
{
    private sealed class Navigatie : NavigationManager
    {
        public Navigatie(string adres) => Initialize("http://localhost/", adres);
    }

    [Fact]
    public void Het_adres_van_hier_gaat_als_terug_mee_ook_met_een_eigen_terug()
    {
        var id = Guid.NewGuid();
        var kolommenbalans = new Navigatie($"http://localhost/a/{id}/kolommenbalans?van=2026-01-01&tot=2026-12-31");
        var kaart = kolommenbalans.MetTerug($"/a/{id}/grootboek?rekening=4300");

        Assert.Equal($"/a/{id}/grootboek?rekening=4300&terug={Uri.EscapeDataString($"/a/{id}/kolommenbalans?van=2026-01-01&tot=2026-12-31")}", kaart);

        // Op de kaart (die zelf een terug heeft) naar een boeking: de kaart met zijn terug gaat mee.
        var opKaart = new Navigatie("http://localhost" + kaart);
        var boeking = opKaart.MetTerug($"/a/{id}/boeking/MEM/2026-0001");
        Assert.StartsWith($"/a/{id}/boeking/MEM/2026-0001?terug=", boeking, StringComparison.Ordinal);
        Assert.Equal(kaart, Uri.UnescapeDataString(boeking.Split("?terug=")[1]));
        Assert.DoesNotContain("//", boeking, StringComparison.Ordinal);
    }

    [Fact]
    public void Een_doorgegeven_bestand_is_er_een_keer_en_alleen_voor_het_doel()
    {
        var overdracht = new Bestandoverdracht();
        overdracht.Geef("activastaat:1", "staat.xlsx", [1, 2, 3]);

        Assert.Null(overdracht.Neem("activastaat:2"));
        Assert.Equal(("staat.xlsx", new byte[] { 1, 2, 3 }), overdracht.Neem("activastaat:1"));
        Assert.Null(overdracht.Neem("activastaat:1"));
    }
}

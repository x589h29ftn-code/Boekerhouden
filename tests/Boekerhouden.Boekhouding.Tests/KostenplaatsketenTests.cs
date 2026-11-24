using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>De kostenplaats op een boekingsregel en de hash-keten: bestaande boekingen houden hun hash.</summary>
public sealed class KostenplaatsketenTests
{
    private static readonly Journaalpost Post = new("MEM", "2026-0001", new(2026, 3, 31), "Brandstof",
        [new("4510", 80m, "Tanken", "I21", null, null), new("1100", -80m)]);

    /// <summary>De hash zoals hij was vóór er kostenplaatsen waren: zes velden per regel.</summary>
    private static string OudeHash(Journaalpost post, long volgnummer, string vorige)
    {
        var inhoud = JsonSerializer.Serialize(new object?[]
        {
            volgnummer, vorige, post.Dagboek, post.Boekstuk, post.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), post.Omschrijving,
            post.Regels.Select(r => new object?[] { r.Rekeningnummer, r.Bedrag.ToString("0.00", CultureInfo.InvariantCulture), r.Omschrijving, r.BtwCode, r.Relatie, r.Factuurnummer }).ToArray(),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhoud)));
    }

    [Fact]
    public void Zonder_kostenplaats_blijft_de_hash_zoals_hij_was()
    {
        Assert.Equal(OudeHash(Post, 7, Journaalketen.Begin), Journaalketen.Hash(Post, 7, Journaalketen.Begin));
    }

    [Fact]
    public void Een_kostenplaats_telt_mee_in_de_hash()
    {
        var met = Post with { Regels = [Post.Regels[0] with { Kostenplaats = "AUTO1" }, Post.Regels[1]] };
        var hash = Journaalketen.Hash(met, 7, Journaalketen.Begin);

        Assert.NotEqual(Journaalketen.Hash(Post, 7, Journaalketen.Begin), hash);
        Assert.NotEqual(hash, Journaalketen.Hash(met with { Regels = [Post.Regels[0] with { Kostenplaats = "AUTO2" }, Post.Regels[1]] }, 7, Journaalketen.Begin));
        Assert.Empty(Journaalketen.Controleer([new Ketenschakel(1, met, Journaalketen.Hash(met, 1, Journaalketen.Begin), Journaalketen.Begin)]));
    }

    [Fact]
    public void Alleen_een_andere_kostenplaats_is_een_wijziging()
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));
        var met = Post with { Regels = [Post.Regels[0] with { Kostenplaats = "AUTO1" }, Post.Regels[1]] };

        Assert.Empty(Boekingscorrectie.Controleer(Post, met, schema, memoriaal: true));
        Assert.Equal(["Er is niets gewijzigd."], Boekingscorrectie.Controleer(met, met with { Regels = [met.Regels[0], met.Regels[1] with { Kostenplaats = " " }] }, schema, memoriaal: true));
    }
}

/// <summary>De kostenplaats van een factuurregel komt op de kosten- of omzetregel van de boeking, en mee in de verdeling.</summary>
public sealed class KostenplaatsFactuurTests
{
    [Fact]
    public void Alleen_de_kostenregel_krijgt_de_kostenplaats()
    {
        var factuur = new Facturen.Factuur(Facturen.Factuursoort.Inkoop, "GARAGE", "G-1", new(2026, 3, 10), new(2026, 4, 9), "Onderhoud",
            [new Facturen.Factuurregel("Onderhoud", "4100", "I21", 100m, Kostenplaats: "AUTO1"), new Facturen.Factuurregel("Overig", "4100", "I21", 50m)]);

        var (post, _) = Facturen.Factuurboeker.Boek(factuur, Testadministratie.Inkoop, "2026-0001", Testadministratie.Schema(), Testadministratie.Btw);

        var met = Assert.Single(post.Regels, r => r.Kostenplaats is not null);
        Assert.Equal(("4100", 100m, "AUTO1"), (met.Rekeningnummer, met.Bedrag, met.Kostenplaats));
    }

    [Fact]
    public void Een_verkoopregel_geeft_zijn_kostenplaats_door()
    {
        var factuur = new Verkoop.Verkoopfactuur("KLANT", new Verkoop.Partij("Klant", new()), "20260001", new(2026, 3, 1), new(2026, 3, 15), "Maart",
            [new Verkoop.Verkoopregel("Brood", 2m, 50m, "8000", "V9", Kostenplaats: "WINKEL")]);

        Assert.Equal("WINKEL", factuur.NaarFactuur(Testadministratie.Btw).Regels.Single().Kostenplaats);
    }

    [Fact]
    public void De_verdeling_per_maand_houdt_de_kostenplaats()
    {
        var verdeling = new Periodeverdeling(new(2026, 1, 1), new(2026, 3, 31), "1410");

        var maanden = verdeling.PerMaand([new Verdeelregel("4300", 300m, "Verzekering", "AUTO1")]);

        Assert.Equal(3, maanden.Count);
        Assert.All(maanden, m => Assert.Equal([("4300", "AUTO1"), ("1410", null)], m.Regels.Select(r => (r.Rekeningnummer, r.Kostenplaats))));
    }
}

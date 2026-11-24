using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Een dagboek verwijderen: een leeg bank- of memoriaaldagboek mag weg (met een regel in het wijzigingslog); met
/// boekingen of ingelezen afschriften niet, inkoop en verkoop nooit, en het laatste bank- en memoriaaldagboek ook niet.
/// Alleen wie mag boekhouden.
/// </summary>
public sealed class DagboekVerwijderenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static string Fouten(Uitkomst u) => string.Join(" ", u.Fouten);

    [Fact]
    public async Task Een_leeg_memoriaal_mag_weg_met_boekingen_of_als_laatste_niet()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.True((await grootboek.VoegMemoriaalToe(wie, id, "MLON", "Memoriaal lonen")).IsGelukt);
        Assert.True((await grootboek.VoegMemoriaalToe(wie, id, "MACC", "Memoriaal accountant")).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new("MACC", "2026-0001", new(2026, 3, 1), "Correctie", [new("4100", 10m), new("1100", -10m)]))).IsGelukt);

        Assert.True((await grootboek.VerwijderDagboek(wie, id, "MLON")).IsGelukt);
        Assert.DoesNotContain(await grootboek.Dagboeken(wie, id), d => d.Code == "MLON");
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.DagboekVerwijderd && r.Onderwerp == "MLON Memoriaal lonen" && r.AdministratieId == id);

        Assert.Contains("al geboekt", Fouten(await grootboek.VerwijderDagboek(wie, id, "MACC")), StringComparison.Ordinal);
        Assert.Contains("bestaat niet", Fouten(await grootboek.VerwijderDagboek(wie, id, "MLON")), StringComparison.Ordinal);
        Assert.Contains("kan niet weg", Fouten(await grootboek.VerwijderDagboek(wie, id, "INK")), StringComparison.Ordinal);
        Assert.Contains("kan niet weg", Fouten(await grootboek.VerwijderDagboek(wie, id, "VRK")), StringComparison.Ordinal);

        // Het standaardmemoriaal mag weg zolang er een ander is; dan is MACC het laatste.
        Assert.True((await grootboek.VerwijderDagboek(wie, id, "MEM")).IsGelukt);
        Assert.Contains("laatste memoriaal", Fouten(await grootboek.VerwijderDagboek(wie, id, "MACC")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Een_lege_bankrekening_mag_weg_met_afschriften_of_als_laatste_niet()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        var bank = app.Dienst<BankDienst>();

        Assert.Contains("laatste bankdagboek", Fouten(await grootboek.VerwijderDagboek(wie, id, "BNK")), StringComparison.Ordinal);
        var afschrift = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "ing-2026-03.mt940"));
        var gelezen = await bank.Lees(wie, id, new MemoryStream(afschrift), "ing-2026-03.mt940");
        Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        var spaar = await bank.VoegBankToe(wie, id, "SPA", "Spaarrekening", null, "1110");
        Assert.True(spaar.IsGelukt, string.Join("; ", spaar.Fouten));

        Assert.Contains("afschriften", Fouten(await grootboek.VerwijderDagboek(wie, id, "BNK")), StringComparison.Ordinal);
        var weg = await grootboek.VerwijderDagboek(wie, id, "SPA");
        Assert.True(weg.IsGelukt, Fouten(weg));
        Assert.DoesNotContain(await grootboek.Dagboeken(wie, id), d => d.Code == "SPA");
        Assert.Contains(await grootboek.Dagboeken(wie, id), d => d.Code == "BNK");
    }

    [Fact]
    public async Task Alleen_wie_mag_boekhouden()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.VoegMemoriaalToe(wie, id, "MLON", "Memoriaal lonen")).IsGelukt);
        var ondernemer = await app.Gebruiker("Olga Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var buiten = await app.Gebruiker("Bert Buiten");

        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.VerwijderDagboek(ondernemer, id, "MLON"));
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.VerwijderDagboek(buiten, id, "MLON"));
        Assert.Contains(await grootboek.Dagboeken(wie, id), d => d.Code == "MLON");
    }
}

using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Tests;

public sealed class PostBalansTests
{
    private static int volgorde;

    private static RgsCode C(string code, string omschrijving, Saldokant? kant = Saldokant.Debet, string? omslag = null) =>
        new(code, code.Length == 1 ? 1 : 1 + ((code.Length - 1) / 3), code.Length == 1 ? null : code[..Math.Max(1, code.Length - 3)],
            omschrijving, omschrijving, kant, omslag, ++volgorde, null, false, new HashSet<string> { "Basis" }, new HashSet<string>());

    private static readonly RgsSchema Schema = new("test",
    [
        C("B", "Balans", null),
        C("BLim", "Liquide middelen"),
        C("BLimBan", "Tegoeden bij banken"),
        C("BLimBanRba", "Rekening-courant bank", omslag: "BSchSakRba"),
        C("BVor", "Vorderingen"),
        C("BVorOvr", "Overige vorderingen"),
        C("BVorDeb", "Handelsdebiteuren"),
        C("BVorDebHad", "Handelsdebiteuren nominaal"),
        C("BSch", "Kortlopende schulden", Saldokant.Credit),
        C("BSchSak", "Schulden aan banken", Saldokant.Credit),
        C("BSchSakRba", "Rekening-courant bij kredietinstellingen", Saldokant.Credit, omslag: "BLimBanRba"),
        C("W", "W&V", null),
        C("WBed", "Overige bedrijfskosten"),
        C("WBedKan", "Kantoorkosten"),
        C("WBedKanKan", "Kantoorbenodigdheden"),
        C("WOmz", "Netto-omzet", Saldokant.Credit),
    ]);

    private static readonly Guid Bank = Guid.NewGuid(), Debiteuren = Guid.NewGuid(), Kantoor = Guid.NewGuid(), Omzet = Guid.NewGuid(), Lening = Guid.NewGuid(), Los = Guid.NewGuid();

    private static RekeningSaldo S(Guid id, string nummer, decimal? huidig, decimal? vorig = null) => new(id, nummer, nummer, huidig, vorig);

    [Fact]
    public void Saldi_tellen_op_naar_subpost_hoofdpost_en_zijde()
    {
        var balans = PostBalans.Bereken(Schema, [],
            new Dictionary<Guid, KoppelDoel> { [Kantoor] = new("WBedKanKan"), [Omzet] = new("WOmz"), [Debiteuren] = new("BVorDebHad") },
            [S(Kantoor, "4300", 250m, 200m), S(Omzet, "8000", -1_000m, -900m), S(Debiteuren, "1300", 750m, 700m)]);

        Assert.Equal(250m, balans.Totaal("WBedKanKan").Huidig);
        Assert.Equal(250m, balans.Totaal("WBedKan").Huidig);
        Assert.Equal(250m, balans.Totaal("WBed").Huidig);
        Assert.Equal(-750m, balans.Totaal("W").Huidig);
        Assert.Equal(750m, balans.ResultaatHuidig);
        Assert.Equal(700m, balans.ResultaatVorig);
        Assert.Equal(750m, balans.Balans.Huidig);
        Assert.Equal(1, balans.Totaal("BVorDeb").Aantal);
        Assert.Empty(balans.Signalen);
    }

    [Fact]
    public void Negatieve_bank_slaat_om_naar_schulden_per_jaar_apart()
    {
        var balans = PostBalans.Bereken(Schema, [],
            new Dictionary<Guid, KoppelDoel> { [Bank] = new("BLimBanRba") },
            [S(Bank, "1100", -500m, 300m)]);

        Assert.Equal(-500m, balans.Totaal("BSchSakRba").Huidig);
        Assert.Equal(-500m, balans.Totaal("BSch").Huidig);
        Assert.Equal(0m, balans.Totaal("BLim").Huidig);
        Assert.Equal(300m, balans.Totaal("BLim").Vorig);
        Assert.Equal(0m, balans.Totaal("BSch").Vorig);
        Assert.Equal("BSchSakRba", balans.Plaatsing[Bank]);
    }

    [Fact]
    public void Eigen_post_telt_mee_bij_zijn_ouder()
    {
        var eigen = new EigenPost(Guid.NewGuid(), "BVorOvr", "Overige vorderingen 1");
        var balans = PostBalans.Bereken(Schema, [eigen],
            new Dictionary<Guid, KoppelDoel> { [Lening] = new("BVorOvr", eigen.Id) },
            [S(Lening, "1450", 1_200m)]);

        var sleutel = PostBalans.EigenSleutel(eigen.Id);
        Assert.Equal(1_200m, balans.Totaal(sleutel).Huidig);
        Assert.Equal(1_200m, balans.Totaal("BVorOvr").Huidig);
        Assert.Equal(1_200m, balans.Totaal("BVor").Huidig);
        Assert.Equal(sleutel, balans.Plaatsing[Lening]);
    }

    [Fact]
    public void Ongekoppelde_rekening_met_saldo_is_een_signaal()
    {
        var balans = PostBalans.Bereken(Schema, [], new Dictionary<Guid, KoppelDoel>(), [S(Los, "9999", 10m), S(Kantoor, "4300", 0m, 0m)]);

        var signaal = Assert.Single(balans.Signalen);
        Assert.Equal(SignaalSoort.Ongekoppeld, signaal.Soort);
        Assert.Equal(Los, signaal.RekeningId);
        Assert.Equal(new PostTotaal(10m, 0m, 1), balans.Ongekoppeld);
    }

    [Fact]
    public void Balanssaldo_aan_de_verkeerde_kant_zonder_omslag_is_een_signaal()
    {
        var balans = PostBalans.Bereken(Schema, [],
            new Dictionary<Guid, KoppelDoel> { [Debiteuren] = new("BVorDebHad") },
            [S(Debiteuren, "1300", -40m)]);

        var signaal = Assert.Single(balans.Signalen);
        Assert.Equal(SignaalSoort.VerkeerdeKant, signaal.Soort);
        Assert.Contains("credit", signaal.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Onbekende_code_blijft_meetellen_bij_de_zijde()
    {
        var balans = PostBalans.Bereken(Schema, [],
            new Dictionary<Guid, KoppelDoel> { [Kantoor] = new("WBedXyzAbc") },
            [S(Kantoor, "4300", 20m)]);

        Assert.Equal(SignaalSoort.OnbekendeCode, Assert.Single(balans.Signalen).Soort);
        Assert.Equal(20m, balans.WinstEnVerlies.Huidig);
    }
}

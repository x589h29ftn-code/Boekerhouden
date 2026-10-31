using System.Text;
using Boekerhouden.Boekhouding.Bank;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class Mt940Tests
{
    [Fact]
    public void Gestructureerde_omschrijving_met_tegenpartij_kenmerk_en_referentie()
    {
        var afschrift = Assert.Single(Lees("ing-2026-03.mt940"));

        Assert.Equal("NL20INGB0001234567", afschrift.Iban);
        Assert.Equal(5000m, afschrift.Beginsaldo);
        Assert.Equal(5985.35m, afschrift.Eindsaldo);
        Assert.True(afschrift.SluitAan);
        Assert.Equal(3, afschrift.Mutaties.Count);

        var incasso = afschrift.Mutaties[0];
        Assert.Equal(new DateOnly(2026, 3, 2), incasso.Datum);
        Assert.Equal(-25m, incasso.Bedrag);
        Assert.Equal("NL63TRIO0212345678", incasso.TegenIban);
        Assert.Equal("Belnet B.V.", incasso.TegenNaam);
        Assert.Equal("Factuur BN-2026-0311 maand maart", incasso.Omschrijving);
        Assert.Equal("20260302-77", incasso.EndToEndId);

        var ontvangst = afschrift.Mutaties[1];
        Assert.Equal(1210m, ontvangst.Bedrag);
        Assert.Equal("J. de Vries", ontvangst.TegenNaam);
        Assert.Equal("1234567890123456", ontvangst.Betalingskenmerk);
        Assert.Null(ontvangst.EndToEndId);
    }

    [Fact]
    public void Vrije_tekst_met_iban_naam_en_omschrijving()
    {
        var afschrift = Assert.Single(Lees("abn-2026-03.sta"));

        Assert.Equal("417164300", afschrift.Iban);
        Assert.True(afschrift.SluitAan);
        var betaling = afschrift.Mutaties[0];
        Assert.Equal(-50m, betaling.Bedrag);
        Assert.Equal("NL36INGB0007654321", betaling.TegenIban);
        Assert.Equal("KANTOORHANDEL JANSEN", betaling.TegenNaam);
        Assert.Equal("FACTUUR 2026-114", betaling.Omschrijving);
        Assert.Equal("0302-A", betaling.EndToEndId);

        var kosten = afschrift.Mutaties[1];
        Assert.Equal(-1.25m, kosten.Bedrag);
        Assert.Null(kosten.TegenIban);
        Assert.Equal("ABN AMRO Bank N.V. KOSTEN BETAALREKENING", kosten.Omschrijving);
    }

    [Fact]
    public void Tegenrekening_uit_de_aanvullende_regel_en_naam_uit_ordp()
    {
        var afschrift = Assert.Single(Lees("rabo-2026-03.swi"));

        Assert.Equal("NL44RABO0123456789", afschrift.Iban);
        var ontvangst = Assert.Single(afschrift.Mutaties);
        Assert.Equal(60.50m, ontvangst.Bedrag);
        Assert.Equal("NL20INGB0001234567", ontvangst.TegenIban);
        Assert.Equal("Bakkerij Voorbeeld", ontvangst.TegenNaam);
        Assert.Equal("Factuur F-2026-9", ontvangst.Omschrijving);
        Assert.Equal("F-2026-9", ontvangst.EndToEndId);
    }

    [Fact]
    public void Meerdere_afschriften_in_een_bestand_en_een_afschrift_dat_niet_sluit()
    {
        const string tekst = """
            :20:A
            :25:NL20INGB0001234567
            :60F:C260101EUR100,00
            :61:260102C10,00NTRFNONREF
            :86:Eerste
            :62F:C260102EUR110,00
            :20:B
            :25:NL20INGB0001234567
            :60F:C260102EUR110,00
            :61:260103RC5,00NTRFNONREF
            :86:Storno
            :62F:C260103EUR100,00
            """;

        var afschriften = Mt940Lezer.Lees(new StringReader(tekst));

        Assert.Equal(2, afschriften.Count);
        Assert.True(afschriften[0].SluitAan);
        Assert.Equal(-5m, afschriften[1].Mutaties[0].Bedrag);
        Assert.Equal(5m, afschriften[1].Verschil);
        Assert.False(afschriften[1].SluitAan);
    }

    [Fact]
    public void Vrije_tekst_zonder_labels_met_iban_en_naam_op_de_eerste_regel()
    {
        const string tekst = """
            :20:R1
            :25:NL20INGB0001234567
            :28C:00001/001
            :60F:C261001EUR1000,00
            :61:2610021002D99,99NTRF9100234501
            :86:NL44RABO0123456789 Kantoorhandel Jansen B.V.
            Factuur 2026-114
            :61:2610031003D24,50NCHGNONREF
            :86:Kosten zakelijk betaalpakket oktober 2026
            :62F:C261003EUR875,51
            """;

        var mutaties = Mt940Lezer.Lees(new StringReader(tekst)).Single().Mutaties;

        Assert.Equal(("NL44RABO0123456789", "Kantoorhandel Jansen B.V.", "Factuur 2026-114"), (mutaties[0].TegenIban, mutaties[0].TegenNaam, mutaties[0].Omschrijving));
        Assert.Equal((null, null, "Kosten zakelijk betaalpakket oktober 2026"), (mutaties[1].TegenIban, mutaties[1].TegenNaam, mutaties[1].Omschrijving));
    }

    [Fact]
    public void Onleesbare_bestanden_geven_een_duidelijke_fout()
    {
        Assert.Contains("geen MT940", Assert.Throws<BankbestandException>(() => Mt940Lezer.Lees(new StringReader("hallo"))).Message, StringComparison.Ordinal);
        var fout = Assert.Throws<BankbestandException>(() => Mt940Lezer.Lees(new StringReader(":20:A\n:25:NL20INGB0001234567\n:60F:X260101EUR1,00\n")));
        Assert.Contains("Regel 3", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sleutel_is_gelijk_bij_opnieuw_inlezen()
    {
        var eerste = Lees("ing-2026-03.mt940")[0].Mutaties.Select(m => m.Sleutel("NL20INGB0001234567"));
        var tweede = Lees("ing-2026-03.mt940")[0].Mutaties.Select(m => m.Sleutel("NL20INGB0001234567"));

        Assert.Equal(eerste, tweede);
        Assert.Equal(3, eerste.Distinct().Count());
    }

    internal static IReadOnlyList<Bankafschrift> Lees(string bestand)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", bestand));
        return Bankbestand.Lees(stream);
    }
}

public sealed class Camt053Tests
{
    [Fact]
    public void Afschrift_met_saldi_tegenpartijen_kenmerk_en_verzamelboeking()
    {
        var afschrift = Assert.Single(Mt940Tests.Lees("camt053-2026-04.xml"));

        Assert.Equal("NL20INGB0001234567", afschrift.Iban);
        Assert.Equal("4", afschrift.Nummer);
        Assert.Equal(5985.35m, afschrift.Beginsaldo);
        Assert.Equal(6720.65m, afschrift.Eindsaldo);
        Assert.True(afschrift.SluitAan);
        Assert.Equal(4, afschrift.Mutaties.Count);

        var betaling = afschrift.Mutaties[0];
        Assert.Equal(-24.99m, betaling.Bedrag);
        Assert.Equal("Belnet B.V.", betaling.TegenNaam);
        Assert.Equal("NL63TRIO0212345678", betaling.TegenIban);
        Assert.Equal("BN-2026-0411", betaling.EndToEndId);

        Assert.Equal(605m, afschrift.Mutaties[1].Bedrag);
        Assert.Equal("1234567890123457", afschrift.Mutaties[1].Betalingskenmerk);
        Assert.Null(afschrift.Mutaties[1].EndToEndId);
        Assert.Equal(255m, afschrift.Mutaties[2].Bedrag);
        Assert.Equal("factuur 2026-0007", afschrift.Mutaties[2].Omschrijving);

        Assert.Equal("Kosten betaalrekening april", afschrift.Mutaties[3].Omschrijving);
    }

    [Fact]
    public void Geen_afschrift_of_geen_xml_geeft_een_fout()
    {
        Assert.Throws<BankbestandException>(() => Camt053Lezer.Lees(new MemoryStream(Encoding.UTF8.GetBytes("<Document/>"))));
        Assert.Throws<BankbestandException>(() => Camt053Lezer.Lees(new MemoryStream(Encoding.UTF8.GetBytes("<Document"))));
    }
}

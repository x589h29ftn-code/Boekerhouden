using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Herkenning;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Een stapel facturen in één PDF: het voorstel waar te knippen, en het splitsen in losse PDF's.</summary>
public sealed class PdfStapelTests
{
    private static IReadOnlyList<Paginavoorstel> Voorstel(byte[] pdf, Factuursoort soort = Factuursoort.Inkoop) =>
        Stapelsplitser.Stel([.. PdfStapel.Paginas(pdf).Select(p => new Stapelpagina(p.Pagina, p.Scan ? null : p.Regels))], soort);

    [Fact]
    public void Het_voorstel_knipt_bij_elke_factuur_ook_bij_vijf_paginas_en_zonder_nummering()
    {
        var voorstel = Voorstel(FictieveStapel.Pdf());

        Assert.Equal(FictieveStapel.AantalPaginas, voorstel.Count);
        Assert.Equal(FictieveStapel.Beginpaginas, voorstel.Where(p => p.Begin && !p.Leeg).Select(p => p.Nummer));
        Assert.Equal([FictieveStapel.LegePagina], voorstel.Where(p => p.Leeg).Select(p => p.Nummer));
        Assert.Equal("GL-88012", voorstel[1].Factuurnummer);

        // Met paginanummering is het pakket zeker; de pagina met alleen voorwaarden hoort bij de factuur ervoor.
        var linde = voorstel.Skip(1).Take(3).ToList();
        Assert.Equal([(true, false), (false, false), (false, false)], linde.Select(p => (p.Begin, p.Twijfel)));
        Assert.False(voorstel[^1].Begin);
        Assert.Contains("voorwaarden", voorstel[^1].Reden, StringComparison.Ordinal);
    }

    [Fact]
    public void Splitsen_geeft_per_factuur_een_eigen_pdf_die_los_wordt_herkend()
    {
        var pdf = FictieveStapel.Pdf();
        var voorstel = Voorstel(pdf);
        var groepen = Stapelsplitser.Groepen(voorstel.Count, voorstel.Where(p => p.Begin).Select(p => p.Nummer).ToHashSet(), voorstel.Where(p => p.Leeg).Select(p => p.Nummer).ToHashSet());

        var delen = PdfStapel.Splits(pdf, groepen);

        Assert.Equal(FictieveStapel.Inkoop.Length, delen.Count);
        Assert.Equal(FictieveStapel.Inkoop.Select(f => f.Totaalpaginas), delen.Select(d => PdfTekst.Lees(new MemoryStream(d)).Paginas));
        foreach (var (deel, factuur) in delen.Zip(FictieveStapel.Inkoop))
        {
            var herkend = Factuurtekst.Analyseer(PdfTekst.Lees(new MemoryStream(deel)).Tekst);
            Assert.Equal((factuur.Nummer, factuur.Iban, factuur.Exclusief), (herkend.Factuurnummer, herkend.Iban, herkend.TotaalExclusief));
        }
    }

    [Fact]
    public void Zonder_tekst_elke_pagina_een_eigen_factuur_met_twijfel_en_een_leeg_blad_valt_weg()
    {
        var voorstel = Stapelsplitser.Stel([new(1, null), new(2, null), new(3, []), new(4, null)], Factuursoort.Inkoop);

        Assert.Equal([true, true, false, true], voorstel.Select(p => p.Begin));
        Assert.Equal([false, true, false, true], voorstel.Select(p => p.Twijfel));
        Assert.True(voorstel[2].Leeg);
    }

    [Fact]
    public void Groepen_uit_knippen_en_weggelaten_paginas()
    {
        Assert.Equal([[1, 2], [4], [5, 6]], Stapelsplitser.Groepen(6, new HashSet<int> { 1, 3, 4, 5 }, new HashSet<int> { 3 }));
        Assert.Equal([[1, 2, 3]], Stapelsplitser.Groepen(3, new HashSet<int>(), new HashSet<int>()));
        Assert.Empty(Stapelsplitser.Groepen(2, new HashSet<int> { 1 }, new HashSet<int> { 1, 2 }));
    }

    [Fact]
    public void Een_stapel_die_te_groot_is_of_geen_pdf_wordt_geweigerd()
    {
        Assert.Contains("geen PDF", Assert.Throws<DocumentException>(() => PdfStapel.Paginas("geen pdf"u8.ToArray())).Message, StringComparison.Ordinal);
        Assert.Throws<DocumentException>(() => PdfStapel.Splits(FictieveStapel.Pdf(), [[1, 2], [2]]));
        Assert.Throws<DocumentException>(() => PdfStapel.Splits(FictieveStapel.Pdf(), [[999]]));
    }
}

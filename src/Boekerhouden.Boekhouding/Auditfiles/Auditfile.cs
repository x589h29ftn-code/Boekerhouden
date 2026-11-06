using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Auditfiles;

/// <summary>De kop van een auditfile: het boekjaar en waar het bestand vandaan komt.</summary>
public sealed record AuditfileKop(int Boekjaar, DateOnly Begin, DateOnly Eind, string Valuta, DateOnly Gemaakt, string Software, string Versie);

/// <summary>De onderneming waarvan de administratie is.</summary>
public sealed record AuditfileBedrijf(string Naam, string? KvkNummer, string? BtwNummer, string? Straat = null, string? Postcode = null, string? Plaats = null, string? Land = null);

/// <summary>Een klant en/of leverancier (customerSupplier).</summary>
public sealed record AuditfileRelatie(string Code, string Naam, string? KvkNummer, string? BtwNummer, bool Klant, bool Leverancier, IReadOnlyList<string> Ibans);

/// <summary>Een grootboekrekening (ledgerAccount) met, als het pakket die meegeeft, de RGS-code.</summary>
public sealed record AuditfileRekening(string Nummer, string Naam, Rekeningsoort Soort, string? RgsCode);

/// <summary>Een btw-code (vatCode) met de rekeningen voor af te dragen en te vorderen btw.</summary>
public sealed record AuditfileBtwcode(string Code, string Omschrijving, string? AfdragenRekening, string? VorderenRekening);

/// <summary>Een regel van een boeking of van de beginbalans; debet positief, credit negatief.</summary>
public sealed record AuditfileRegel(string Rekening, decimal Bedrag, string? Omschrijving = null, string? Relatie = null, string? Factuurnummer = null, string? BtwCode = null);

/// <summary>Een boeking (transaction) in een dagboek.</summary>
public sealed record AuditfileBoeking(string Boekstuk, string? Omschrijving, DateOnly Datum, int Periode, IReadOnlyList<AuditfileRegel> Regels);

/// <summary>Een dagboek (journal) met zijn boekingen; bij een bank- of kasdagboek de bank- of kasrekening.</summary>
public sealed record AuditfileDagboek(string Code, string Naam, Dagboeksoort Soort, string? Tegenrekening, IReadOnlyList<AuditfileBoeking> Boekingen);

/// <summary>
/// Een XML Auditfile Financieel (XAF), de standaard van de Belastingdienst om een boekhouding over te dragen: kop,
/// onderneming, relaties, rekeningschema, btw-codes, beginbalans en per dagboek de boekingen van één boekjaar.
/// </summary>
/// <param name="Versie">De XAF-versie, bijv. "3.2".</param>
public sealed record Auditfile(
    string Versie,
    AuditfileKop Kop,
    AuditfileBedrijf Bedrijf,
    IReadOnlyList<AuditfileRelatie> Relaties,
    IReadOnlyList<AuditfileRekening> Rekeningen,
    IReadOnlyList<AuditfileBtwcode> Btwcodes,
    DateOnly? BeginbalansDatum,
    IReadOnlyList<AuditfileRegel> Beginbalans,
    IReadOnlyList<AuditfileDagboek> Dagboeken)
{
    public int AantalBoekingen => Dagboeken.Sum(d => d.Boekingen.Count);

    /// <summary>
    /// Wat er inhoudelijk niet klopt: een regel op een rekening die niet in het rekeningschema staat, een boeking of een
    /// beginbalans die niet in evenwicht is. Leeg als alles klopt.
    /// </summary>
    public IReadOnlyList<string> Controleer()
    {
        var fouten = new List<string>();
        var rekeningen = Rekeningen.Select(r => r.Nummer).ToHashSet(StringComparer.Ordinal);
        foreach (var onbekend in Beginbalans.Select(r => r.Rekening).Concat(Dagboeken.SelectMany(d => d.Boekingen).SelectMany(b => b.Regels).Select(r => r.Rekening))
                     .Where(r => !rekeningen.Contains(r)).Distinct(StringComparer.Ordinal).Take(10))
        {
            fouten.Add($"Rekening {onbekend} staat niet in het rekeningschema van de auditfile.");
        }

        if (Beginbalans.Sum(r => r.Bedrag) is var begin && begin != 0)
        {
            fouten.Add($"De beginbalans is niet in evenwicht (verschil {begin:0.00}).");
        }

        foreach (var (dagboek, boeking) in Dagboeken.SelectMany(d => d.Boekingen.Select(b => (d.Code, b))).Where(x => x.b.Regels.Sum(r => r.Bedrag) != 0).Take(10))
        {
            fouten.Add($"Boeking {dagboek} {boeking.Boekstuk} is niet in evenwicht (verschil {boeking.Regels.Sum(r => r.Bedrag):0.00}).");
        }

        return fouten;
    }
}

/// <summary>Een auditfile die niet te lezen is (geen XML, geen auditfile, een DTD, te groot).</summary>
public sealed class AuditfileException : Exception
{
    public AuditfileException()
        : base("De auditfile is niet te lezen.")
    {
    }

    public AuditfileException(string melding)
        : base(melding)
    {
    }

    public AuditfileException(string melding, Exception binnen)
        : base(melding, binnen)
    {
    }
}

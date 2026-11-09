using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Beginbalans;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een post uit de jaarrekening met de voorgestelde rekening (leeg als niets past).</summary>
public sealed record Beginbalansregel(Balanspost Post, string? Rekening);

/// <summary>Het voorstel voor de beginbalans uit een jaarrekening: de posten, de boekdatum en wat opvalt.</summary>
/// <param name="Boekdatum">De dag na de balansdatum (1 januari van het nieuwe jaar).</param>
public sealed record Beginbalansvoorstel(string Bestandsnaam, DateOnly Balansdatum, DateOnly Boekdatum, IReadOnlyList<Beginbalansregel> Regels, IReadOnlyList<string> Signalen);

/// <summary>Een regel van de beginbalans zoals de gebruiker hem boekt: rekening en bedrag (debet positief).</summary>
public sealed record Beginbalansinvoer(string Rekening, decimal Bedrag, string? Omschrijving = null);

/// <summary>
/// De beginbalans uit de jaarrekening van vorig jaar: de PDF lezen (eerst de virusscan), per post een rekening
/// voorstellen en de balans als één memoriaalboeking boeken, alleen als activa en passiva gelijk zijn.
/// </summary>
public sealed class BeginbalansDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, IEnumerable<Documenten.IVirusscanner> virusscanners)
{
    public const int MaximumBytes = 20 * 1024 * 1024;

    public async Task<Uitkomst<Beginbalansvoorstel>> Lees(Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var naam = Path.GetFileName(bestandsnaam);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var pdf = await LeesBegrensd(inhoud, annulering);
        if (pdf is null)
        {
            return Uitkomst<Beginbalansvoorstel>.Mislukt($"{naam} is groter dan {MaximumBytes / 1024 / 1024} MB.");
        }

        if (pdf.Length < 5 || pdf.AsSpan(0, Math.Min(pdf.Length, 1024)).IndexOf("%PDF-"u8) < 0)
        {
            return Uitkomst<Beginbalansvoorstel>.Mislukt($"{naam} is geen PDF.");
        }

        if (virusscanners.FirstOrDefault() is { } scanner)
        {
            var scan = await scanner.ScanAsync(new MemoryStream(pdf), annulering);
            if (scan.Uitslag != Documenten.Scanuitslag.Schoon)
            {
                return Uitkomst<Beginbalansvoorstel>.Mislukt(scan.Uitslag == Documenten.Scanuitslag.Besmet
                    ? $"{naam} bevat kwaadaardige inhoud ({scan.Toelichting}) en is niet gelezen."
                    : $"{scan.Toelichting} {naam} is niet gelezen; probeer het later opnieuw.");
            }
        }

        Jaarrekeningbalans? balans;
        try
        {
            balans = Documenten.PdfJaarrekening.Lees(pdf);
        }
        catch (Documenten.DocumentException fout)
        {
            return Uitkomst<Beginbalansvoorstel>.Mislukt($"{naam}: {fout.Message}");
        }

        if (balans is null)
        {
            return Uitkomst<Beginbalansvoorstel>.Mislukt($"In {naam} is geen balans gevonden (een kop als \"Balans per 31 december 2025\" met activa en passiva).");
        }

        var schema = await grootboek.Schema(wie, administratie);
        var regels = balans.Posten.Select(p => new Beginbalansregel(p, Balansrekeningkeuze.Kies(p, schema)?.Nummer)).ToList();
        var signalen = balans.Controle().ToList();
        var boekdatum = balans.Balansdatum.AddDays(1);
        if (await db.Boekingen.AnyAsync(b => b.Datum == boekdatum && b.Omschrijving.StartsWith("Beginbalans"), annulering))
        {
            signalen.Add($"Op {boekdatum:dd-MM-yyyy} staat al een beginbalans.");
        }

        foreach (var open in regels.Where(r => schema.Zoek(r.Rekening)?.Functie is Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren))
        {
            signalen.Add($"{open.Post.Naam} komt als één bedrag op {open.Rekening}, zonder openstaande posten per factuur.");
        }

        return Uitkomst<Beginbalansvoorstel>.Met(new Beginbalansvoorstel(naam, balans.Balansdatum, boekdatum, regels, signalen));
    }

    /// <summary>
    /// Boekt de beginbalans in het (eerste) memoriaal: per rekening één regel, alleen balansrekeningen, en debet en
    /// credit gelijk.
    /// </summary>
    public async Task<Uitkomst<long>> Boek(Uitvoerder wie, Guid administratie, DateOnly datum, IReadOnlyList<Beginbalansinvoer> regels, string? bron = null)
    {
        ArgumentNullException.ThrowIfNull(regels);
        await using (await toegang.Open(wie, administratie, Recht.Boekhouden))
        {
        }

        var schema = await grootboek.Schema(wie, administratie);
        var fouten = new List<string>();
        foreach (var regel in regels.Where(r => r.Bedrag != 0))
        {
            if (string.IsNullOrWhiteSpace(regel.Rekening))
            {
                fouten.Add($"Kies een rekening voor {regel.Omschrijving ?? "elke regel"}.");
            }
            else if (schema.Zoek(regel.Rekening) is not { } rekening)
            {
                fouten.Add($"Rekening {regel.Rekening} bestaat niet.");
            }
            else if (!rekening.IsBalans)
            {
                fouten.Add($"{rekening.Nummer} {rekening.Naam} is geen balansrekening.");
            }
        }

        var verschil = regels.Sum(r => r.Bedrag);
        if (verschil != 0)
        {
            fouten.Add($"De beginbalans is niet in balans: debet en credit verschillen {Domain.Bedragen.Euro(Math.Abs(verschil))}.");
        }

        if (regels.All(r => r.Bedrag == 0))
        {
            fouten.Add("De beginbalans heeft geen bedragen.");
        }

        var memoriaal = (await grootboek.Dagboeken(wie, administratie)).FirstOrDefault(d => d.Soort == Dagboeksoort.Memoriaal);
        if (memoriaal is null)
        {
            fouten.Add("Er is geen memoriaal om de beginbalans in te boeken.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<long>.Mislukt([.. fouten.Distinct()]);
        }

        // Per rekening één regel, in de volgorde van het rekeningschema.
        var journaal = regels.Where(r => r.Bedrag != 0)
            .GroupBy(r => r.Rekening.Trim())
            .Select(g => new Journaalregel(g.Key, g.Sum(r => r.Bedrag), string.Join(", ", g.Select(r => r.Omschrijving).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct())))
            .Where(r => r.Bedrag != 0)
            .OrderBy(r => r.Rekeningnummer, Rekeningnummers.Volgorde)
            .ToList();
        var boekstuk = await grootboek.VolgendBoekstuk(wie, administratie, memoriaal!.Code, datum.Year);
        var uitkomst = await grootboek.Boek(wie, administratie, new Journaalpost(memoriaal.Code, boekstuk, datum, $"Beginbalans {datum:dd-MM-yyyy}", journaal));
        if (uitkomst.IsGelukt)
        {
            await audit.Log(wie, AuditActies.BeginbalansGeboekt, $"{memoriaal.Code} {boekstuk}", bron is null ? $"{journaal.Count} rekeningen" : $"uit {bron}; {journaal.Count} rekeningen", administratie);
        }

        return uitkomst;
    }

    private static async Task<byte[]?> LeesBegrensd(Stream stream, CancellationToken annulering)
    {
        using var kopie = new MemoryStream();
        var buffer = new byte[81920];
        int gelezen;
        while ((gelezen = await stream.ReadAsync(buffer, annulering)) > 0)
        {
            if (kopie.Length + gelezen > MaximumBytes)
            {
                return null;
            }

            kopie.Write(buffer, 0, gelezen);
        }

        return kopie.ToArray();
    }
}

using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Loon;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een gelezen loonjournaal met per regel de voorgestelde rekening en wat opvalt.</summary>
public sealed record Loonjournaalvoorstel(string Bestandsnaam, Loonjournaal Journaal, IReadOnlyList<Loonkoppeling> Koppelingen, IReadOnlyList<string> Signalen);

/// <summary>
/// Het loonjournaal uit het salarispakket als memoriaalboeking: de PDF lezen (eerst de virusscan), per rekening van het
/// salarispakket een rekening van de administratie voorstellen (onthouden, hetzelfde nummer of dezelfde naam) en de
/// koppelingen onthouden voor de volgende keer. Boeken gaat daarna via het memoriaal, in balans.
/// </summary>
public sealed class LoonjournaalDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, IEnumerable<Documenten.IVirusscanner> virusscanners)
{
    public const int MaximumBytes = 20 * 1024 * 1024;

    public async Task<Uitkomst<Loonjournaalvoorstel>> Lees(Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var naam = Path.GetFileName(bestandsnaam);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        using var buffer = new MemoryStream();
        await inhoud.CopyToAsync(buffer, annulering);
        if (buffer.Length > MaximumBytes)
        {
            return Uitkomst<Loonjournaalvoorstel>.Mislukt($"{naam} is groter dan {MaximumBytes / 1024 / 1024} MB.");
        }

        var pdf = buffer.ToArray();
        if (pdf.Length < 5 || pdf.AsSpan(0, Math.Min(pdf.Length, 1024)).IndexOf("%PDF-"u8) < 0)
        {
            return Uitkomst<Loonjournaalvoorstel>.Mislukt($"{naam} is geen PDF.");
        }

        if (virusscanners.FirstOrDefault() is { } scanner)
        {
            var scan = await scanner.ScanAsync(new MemoryStream(pdf), annulering);
            if (scan.Uitslag != Documenten.Scanuitslag.Schoon)
            {
                return Uitkomst<Loonjournaalvoorstel>.Mislukt(scan.Uitslag == Documenten.Scanuitslag.Besmet
                    ? $"{naam} bevat kwaadaardige inhoud ({scan.Toelichting}) en is niet gelezen."
                    : $"{scan.Toelichting} {naam} is niet gelezen; probeer het later opnieuw.");
            }
        }

        Loonjournaal? journaal;
        try
        {
            journaal = Documenten.PdfLoonjournaal.Lees(pdf);
        }
        catch (Documenten.DocumentException fout)
        {
            return Uitkomst<Loonjournaalvoorstel>.Mislukt($"{naam}: {fout.Message}");
        }

        if (journaal is null)
        {
            return Uitkomst<Loonjournaalvoorstel>.Mislukt($"In {naam} is geen loonjournaal gevonden (rekeningen met bedragen in een kolom debet en credit).");
        }

        var schema = await grootboek.Schema(wie, administratie);
        var onthouden = await db.Loonkoppelingen.AsNoTracking().ToDictionaryAsync(k => k.Loonrekening, k => k.Rekeningnummer, StringComparer.Ordinal, annulering);
        return Uitkomst<Loonjournaalvoorstel>.Met(new Loonjournaalvoorstel(naam, journaal, Loonkoppelingen.Stel(journaal, schema, onthouden), [.. journaal.Controle()]));
    }

    /// <summary>De gekozen rekeningen onthouden voor het volgende loonjournaal (alleen wat nieuw of gewijzigd is wordt gelogd).</summary>
    public async Task<Uitkomst> Onthoud(Uitvoerder wie, Guid administratie, IEnumerable<(Loonjournaalregel Regel, string Rekening)> keuzes)
    {
        ArgumentNullException.ThrowIfNull(keuzes);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var schema = await grootboek.Schema(wie, administratie);
        var bestaand = await db.Loonkoppelingen.ToDictionaryAsync(k => k.Loonrekening, StringComparer.Ordinal);
        var gewijzigd = new List<string>();
        foreach (var (regel, rekening) in keuzes.DistinctBy(k => k.Regel.Nummer))
        {
            if (schema.Zoek(rekening) is null)
            {
                return Uitkomst.Mislukt($"Rekening {rekening} bestaat niet.");
            }

            if (bestaand.TryGetValue(regel.Nummer, out var k))
            {
                if (k.Rekeningnummer == rekening)
                {
                    continue;
                }

                (k.Rekeningnummer, k.Loonnaam) = (rekening, regel.Naam);
            }
            else
            {
                db.Loonkoppelingen.Add(new Loonrekeningkoppeling { AdministratieId = administratie, Loonrekening = regel.Nummer, Loonnaam = regel.Naam, Rekeningnummer = rekening });
            }

            gewijzigd.Add($"{regel.Nummer} {regel.Naam} → {rekening}");
        }

        if (gewijzigd.Count > 0)
        {
            await db.SaveChangesAsync();
            await audit.Log(wie, AuditActies.LoonkoppelingGewijzigd, "Loonjournaal", string.Join("; ", gewijzigd), administratie);
        }

        return Uitkomst.Gelukt;
    }
}

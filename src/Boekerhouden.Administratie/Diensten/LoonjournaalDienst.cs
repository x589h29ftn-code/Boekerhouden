using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Loon;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een onthouden koppeling: de rekening uit het salarispakket en de rekening in de administratie.</summary>
public sealed record Loonkoppelinginstelling(string Loonrekening, string Loonnaam, string Rekeningnummer);

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
        var koppelingen = Loonkoppelingen.Stel(journaal, schema, onthouden);
        var signalen = journaal.Controle().ToList();
        signalen.AddRange(koppelingen.Where(k => k.Bron == Loonkoppelbron.Nieuw)
            .Select(k => $"Nieuw in het salarispakket: {k.Regel.Nummer} {k.Regel.Naam}. Kies bij welke rekening die hoort."));
        return Uitkomst<Loonjournaalvoorstel>.Met(new Loonjournaalvoorstel(naam, journaal, koppelingen, signalen));
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
            var details = string.Join("; ", gewijzigd.Take(20)) + (gewijzigd.Count > 20 ? $"; en {gewijzigd.Count - 20} meer" : null);
            await audit.Log(wie, AuditActies.LoonkoppelingGewijzigd, "Loonjournaal", details, administratie);
        }

        return Uitkomst.Gelukt;
    }

    /// <summary>Alle onthouden koppelingen van de administratie, op nummer van het salarispakket.</summary>
    public async Task<IReadOnlyList<Loonkoppelinginstelling>> Koppelingen(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        return [.. (await db.Loonkoppelingen.AsNoTracking().ToListAsync())
            .OrderBy(k => k.Loonrekening, Boekhouding.Grootboek.Rekeningnummers.Volgorde)
            .Select(k => new Loonkoppelinginstelling(k.Loonrekening, k.Loonnaam, k.Rekeningnummer))];
    }

    /// <summary>Een koppeling instellen of wijzigen zonder loonjournaal (bij de instellingen van de administratie).</summary>
    public async Task<Uitkomst> Zet(Uitvoerder wie, Guid administratie, string loonrekening, string loonnaam, string rekening)
    {
        var nummer = loonrekening?.Trim() ?? "";
        if (nummer.Length is 0 or > 20)
        {
            return Uitkomst.Mislukt("Vul het rekeningnummer uit het salarispakket in.");
        }

        return await Onthoud(wie, administratie, [(new Loonjournaalregel(nummer, (loonnaam ?? "").Trim(), 0m), (rekening ?? "").Trim())]);
    }

    /// <summary>Een onthouden koppeling weghalen; bij het volgende loonjournaal geldt die rekening dan weer als nieuw.</summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid administratie, string loonrekening)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Loonkoppelingen.FirstOrDefaultAsync(k => k.Loonrekening == loonrekening) is not { } k)
        {
            return Uitkomst.Mislukt($"Er is geen koppeling voor {loonrekening}.");
        }

        db.Loonkoppelingen.Remove(k);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.LoonkoppelingGewijzigd, "Loonjournaal", $"{k.Loonrekening} {k.Loonnaam} weggehaald (was {k.Rekeningnummer})", administratie);
        return Uitkomst.Gelukt;
    }
}

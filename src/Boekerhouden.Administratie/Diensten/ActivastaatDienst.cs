using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>De rekeningen voor een groep van de staat (bijv. inventaris): voorgesteld, door de gebruiker aan te passen.</summary>
public sealed record Staatgroepkeuze(string Groep, string? Activarekening, string? Afschrijvingsrekening, string? Kostenrekening);

/// <summary>Eén rekening van de aansluiting: wat de staat zegt (tekenconventie van het grootboek) en wat het grootboek zegt.</summary>
public sealed record Staataansluiting(string Rekening, string Naam, string Wat, decimal Staat, decimal Grootboek)
{
    public decimal Verschil => Grootboek - Staat;
}

/// <summary>Een ingelezen staat met het voorstel om hem over te nemen.</summary>
/// <param name="Begin">De dag na de staat: vanaf hier gaan de activa verder in deze administratie.</param>
/// <param name="Bestaand">Omschrijvingen van de staat die al als vast actief bestaan (die worden overgeslagen).</param>
public sealed record Staatvoorstel(string Bestandsnaam, Ingelezenstaat Staat, DateOnly Begin, IReadOnlyList<Staatgroepkeuze> Groepen, IReadOnlySet<string> Bestaand);

/// <summary>
/// Een staat van vaste activa (mva-staat) van vorig jaar inlezen, uit Excel of PDF, en overnemen als vaste activa: per
/// actief de aanschafwaarde, restwaarde, wat al is afgeschreven en de resterende looptijd (<see cref="Staatovername"/>).
/// Eerst een voorstel met per groep de rekeningen en de aansluiting op de totalen van de staat en op het grootboek per
/// de dag na de staat; overnemen pas als de gebruiker dat kiest. Recht Boekhouden.
/// </summary>
public sealed class ActivastaatDienst(Toegang toegang, ActivaDienst activa, AuditDienst audit, IEnumerable<Documenten.IVirusscanner> virusscanners)
{
    public const int MaximumBytes = 20 * 1024 * 1024;

    public async Task<Uitkomst<Staatvoorstel>> Lees(Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var naam = Path.GetFileName(bestandsnaam);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var bytes = await LeesBegrensd(inhoud, annulering);
        if (bytes is null)
        {
            return Uitkomst<Staatvoorstel>.Mislukt($"{naam} is groter dan {MaximumBytes / 1024 / 1024} MB.");
        }

        var soort = bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%PDF-"u8) >= 0 ? "pdf" : bytes is [(byte)'P', (byte)'K', ..] ? "xlsx" : null;
        if (soort is null)
        {
            return Uitkomst<Staatvoorstel>.Mislukt($"{naam} is geen Excel-bestand (.xlsx) of PDF.");
        }

        if (virusscanners.FirstOrDefault() is { } scanner)
        {
            var scan = await scanner.ScanAsync(new MemoryStream(bytes), annulering);
            if (scan.Uitslag != Documenten.Scanuitslag.Schoon)
            {
                return Uitkomst<Staatvoorstel>.Mislukt(scan.Uitslag == Documenten.Scanuitslag.Besmet
                    ? $"{naam} bevat kwaadaardige inhoud ({scan.Toelichting}) en is niet gelezen."
                    : $"{scan.Toelichting} {naam} is niet gelezen; probeer het later opnieuw.");
            }
        }

        Ingelezenstaat staat;
        try
        {
            staat = soort == "pdf" ? Documenten.PdfActivastaat.Lees(bytes) : Documenten.ExcelActivastaat.Lees(new MemoryStream(bytes));
        }
        catch (Documenten.DocumentException fout)
        {
            return Uitkomst<Staatvoorstel>.Mislukt($"{naam}: {fout.Message}");
        }

        if (!staat.Regels.Any())
        {
            return Uitkomst<Staatvoorstel>.Mislukt([$"In {naam} is geen staat van vaste activa gevonden.", .. staat.Signalen.Select(s => s.Tekst)]);
        }

        if (staat.Einddatum is not { } eind)
        {
            return Uitkomst<Staatvoorstel>.Mislukt($"In {naam} staat in de kop geen jaar (bijv. \"t/m 31-12-2025\"); zonder jaar is niet te zeggen per wanneer de activa verder gaan.");
        }

        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var bestaande = await db.Activa.AsNoTracking().ToListAsync(annulering);
        var groepen = staat.Groepen.Select(g => g.Naam).Distinct().Select(g => Voorstel(g, schema, bestaande)).ToList();
        var omschrijvingen = bestaande.Select(a => Namen.Normaliseer(a.Omschrijving)).ToHashSet(StringComparer.Ordinal);
        var dubbel = staat.Regels.Where(r => omschrijvingen.Contains(Namen.Normaliseer(r.Omschrijving))).Select(r => r.Omschrijving).ToHashSet(StringComparer.Ordinal);
        return Uitkomst<Staatvoorstel>.Met(new Staatvoorstel(naam, staat, eind.AddDays(1), groepen, dubbel));
    }

    /// <summary>
    /// De staat tegen het grootboek per de dag na de staat (de beginbalans telt mee): per activarekening de aanschafwaarde
    /// (debet) en per afschrijvingsrekening de afschrijving (credit); staat alles op één rekening, dan de boekwaarde.
    /// </summary>
    public async Task<IReadOnlyList<Staataansluiting>> Aansluiting(Uitvoerder wie, Guid administratie, Staatvoorstel voorstel, IReadOnlyList<Staatgroepkeuze> keuzes)
    {
        ArgumentNullException.ThrowIfNull(voorstel);
        ArgumentNullException.ThrowIfNull(keuzes);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var staat = new Dictionary<(string Rekening, string Wat), decimal>();
        foreach (var g in voorstel.Staat.Groepen)
        {
            if (keuzes.FirstOrDefault(k => k.Groep == g.Naam) is not { Activarekening: { Length: > 0 } activarekening } keuze)
            {
                continue;
            }

            if (keuze.Afschrijvingsrekening == activarekening)
            {
                staat[(activarekening, "boekwaarde")] = staat.GetValueOrDefault((activarekening, "boekwaarde")) + g.Som.Boekwaarde;
                continue;
            }

            staat[(activarekening, "aanschafwaarde")] = staat.GetValueOrDefault((activarekening, "aanschafwaarde")) + g.Som.AanschafEind;
            if (keuze.Afschrijvingsrekening is { Length: > 0 } afschrijving)
            {
                staat[(afschrijving, "afschrijving")] = staat.GetValueOrDefault((afschrijving, "afschrijving")) - g.Som.AfschrijvingEind;
            }
        }

        var rekeningen = staat.Keys.Select(k => k.Rekening).Distinct().ToList();
        var begin = voorstel.Begin;
        var saldi = await (from r in db.GeldendeRegels
                           join b in db.Boekingen on r.BoekingId equals b.Id
                           where rekeningen.Contains(r.Rekeningnummer) && (b.Datum < begin || (b.Datum == begin && b.Omschrijving.StartsWith("Beginbalans")))
                           group r.Bedrag by r.Rekeningnummer into s
                           select new { Rekening = s.Key, Saldo = s.Sum() }).ToListAsync();
        return [.. staat.OrderBy(s => s.Key.Rekening, StringComparer.Ordinal)
            .Select(s => new Staataansluiting(s.Key.Rekening, schema.Zoek(s.Key.Rekening)?.Naam ?? "", s.Key.Wat, s.Value, saldi.FirstOrDefault(x => x.Rekening == s.Key.Rekening)?.Saldo ?? 0m))];
    }

    /// <summary>
    /// De activa van de staat overnemen met de gekozen rekeningen: alles eerst controleren, dan aanmaken (wat al bestaat
    /// wordt overgeslagen). <paramref name="datums"/>: per rij van de staat een aanschafdatum die de gebruiker invulde.
    /// </summary>
    public async Task<Uitkomst<int>> NeemOver(Uitvoerder wie, Guid administratie, Staatvoorstel voorstel, IReadOnlyList<Staatgroepkeuze> keuzes, IReadOnlyDictionary<int, DateOnly>? datums = null)
    {
        ArgumentNullException.ThrowIfNull(voorstel);
        ArgumentNullException.ThrowIfNull(keuzes);
        await using (await toegang.Open(wie, administratie, Recht.Boekhouden))
        {
        }

        var invoer = new List<ActivumInvoer>();
        var fouten = new List<string>();
        foreach (var regel in voorstel.Staat.Regels.Where(r => !voorstel.Bestaand.Contains(r.Omschrijving)))
        {
            if (Staatovername.Van(regel, voorstel.Begin) is not { } o)
            {
                continue;
            }

            if (keuzes.FirstOrDefault(k => k.Groep == regel.Groep) is not { Activarekening: { Length: > 0 } a, Afschrijvingsrekening: { Length: > 0 } f, Kostenrekening: { Length: > 0 } k })
            {
                fouten.Add($"Kies bij {regel.Groep} de activarekening, de afschrijvingsrekening en de kostenrekening.");
                continue;
            }

            var datum = datums?.GetValueOrDefault(regel.Rij) is { } gekozen && gekozen != default ? gekozen : o.Aanschafdatum;
            invoer.Add(new ActivumInvoer(o.Omschrijving, datum, o.Aanschafwaarde, o.Restwaarde, o.BeginAfschrijving, o.Looptijd, o.Methode, a, f, k, o.AfgeschrevenVoorBegin,
                Bron: null, Notitie: $"Overgenomen van de staat {voorstel.Bestandsnaam}" + (o.DatumGeschat && datum == o.Aanschafdatum ? "; aanschafdatum geschat" : null)));
        }

        // Eerst alles controleren: liever niets aanmaken dan de helft.
        await using (var db = await toegang.Open(wie, administratie, Recht.Boekhouden))
        {
            var (schema, _) = await GrootboekDienst.Inrichting(db);
            fouten.AddRange(invoer.SelectMany(i => i.NaarDomein().Controleer(schema).Select(f => $"{i.Omschrijving}: {f}")));
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<int>.Mislukt([.. fouten.Distinct()]);
        }

        foreach (var i in invoer)
        {
            var uitkomst = await activa.Bewaar(wie, administratie, null, i);
            if (!uitkomst.IsGelukt)
            {
                return Uitkomst<int>.Mislukt([$"{i.Omschrijving}: {string.Join(" ", uitkomst.Fouten)}"]);
            }
        }

        await audit.Log(wie, AuditActies.ActivastaatIngelezen, voorstel.Bestandsnaam,
            $"{invoer.Count} activa per {voorstel.Begin:dd-MM-yyyy}; aanschaf {voorstel.Staat.Totaal.AanschafEind:0.00}, afgeschreven {voorstel.Staat.Totaal.AfschrijvingEind:0.00}", administratie);
        return Uitkomst<int>.Met(invoer.Count);
    }

    /// <summary>
    /// De rekeningen voor een groep: een activarekening met (een deel van) de naam van de groep ("Inventaris",
    /// "Vervoermiddelen"), dan de afschrijvings- en kostenrekening die daarbij horen (zoals bij eerdere activa, anders RGS).
    /// </summary>
    internal static Staatgroepkeuze Voorstel(string groep, Rekeningschema schema, IReadOnlyList<Data.Activum> bestaande)
    {
        var woorden = Namen.Normaliseer(groep).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 5).Select(w => w[..Math.Min(6, w.Length)]).ToList();
        bool Past(Rekening r) => woorden.Any(w => Namen.Normaliseer(r.Naam).Contains(w, StringComparison.Ordinal));
        bool IsAfschrijving(Rekening r) => Namen.Normaliseer(r.Naam) is var n && (n.Contains("afschr", StringComparison.Ordinal) || n.Contains("cumul", StringComparison.Ordinal));
        var kandidaten = schema.Alle.Where(r => r.IsBalans && !IsAfschrijving(r) && Past(r)).ToList();
        var activarekening = kandidaten.FirstOrDefault(r => r.RgsCode is { } rgs && (rgs.StartsWith("BMva", StringComparison.Ordinal) || rgs.StartsWith("BIva", StringComparison.Ordinal)))
                             ?? kandidaten.FirstOrDefault();
        if (activarekening is null)
        {
            return new Staatgroepkeuze(groep, null, null, null);
        }

        var (afschrijving, kosten, _) = ActivaDienst.Rekeningen(schema, bestaande, activarekening.Nummer);
        afschrijving ??= schema.Alle.FirstOrDefault(r => r.IsBalans && IsAfschrijving(r) && Past(r))?.Nummer;
        return new Staatgroepkeuze(groep, activarekening.Nummer, afschrijving, kosten);
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

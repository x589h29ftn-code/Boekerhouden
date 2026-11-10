using System.Security.Cryptography;
using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Documenten;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Eén pagina van een stapel: het voorstel van het pakket en de keuze van de gebruiker.</summary>
/// <param name="Begin">Hier begint een factuur (een knip vóór deze pagina).</param>
/// <param name="Weg">De pagina wordt weggelaten (leeg blad, voorblad).</param>
/// <param name="Twijfel">Het pakket was niet zeker van zijn voorstel vóór deze pagina.</param>
/// <param name="Leeg">Het pakket vond de pagina (bijna) leeg.</param>
/// <param name="Klaar">De factuur met deze pagina is al ingelezen (bij een eerdere poging om te splitsen).</param>
public sealed record Stapelpaginastand(int Nummer, bool Begin, bool Weg, bool Twijfel, bool Leeg, string? Reden, string? Factuurnummer, string? Naam, bool Klaar = false);

public sealed record Stapelkop(
    Guid Id, Factuursoort Soort, string Bestandsnaam, int Paginas, Stapelstatus Status, DateTimeOffset IngelezenOp, string IngelezenDoor, int TeLezen, int? Facturen);

/// <param name="Tekstherkenning">Er is tekstherkenning (OCR) om de pagina's zonder tekst te lezen.</param>
public sealed record Stapelweergave(Stapelkop Kop, IReadOnlyList<Stapelpaginastand> Paginas, bool Tekstherkenning)
{
    /// <summary>De facturen (groepen pagina's) die uit de knippen volgen.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Groepen =>
        Stapelsplitser.Groepen(Paginas.Count, Paginas.Where(p => p.Begin).Select(p => p.Nummer).ToHashSet(), Paginas.Where(p => p.Weg).Select(p => p.Nummer).ToHashSet());
}

/// <summary>Wat er na het splitsen met de facturen is gebeurd.</summary>
/// <param name="Facturen">Hoeveel facturen er uit de stapel kwamen.</param>
/// <param name="Geboekt">Hoeveel daarvan meteen vanzelf zijn geboekt.</param>
/// <param name="Dubbel">Hoeveel er al in de administratie stonden.</param>
/// <param name="Mislukt">Hoeveel er niet zijn ingelezen (de stapel blijft dan open staan).</param>
public sealed record Stapelverslag(int Facturen, int Geboekt, int Dubbel, int Mislukt, IReadOnlyList<string> Meldingen);

/// <summary>
/// Een stapel splitsen: één PDF met veel inkoop- of verkoopfacturen (bijv. een gescande jaaradministratie) inlezen,
/// een voorstel doen waar te knippen (<see cref="Stapelsplitser"/>), de knippen van de gebruiker bewaren en na akkoord
/// elke factuur als eigen PDF door de gewone weg van inlezen sturen (<see cref="InkoopDienst.Lees"/> of
/// <see cref="VerkoopimportDienst.Lees"/>): virusscan, herkenning, controle op dubbel en automatisch boeken. De stapel
/// zelf blijft bewaard, zodat altijd te zien is wat er is aangeleverd.
/// </summary>
public sealed class StapelDienst(
    Toegang toegang,
    InkoopDienst inkoop,
    VerkoopimportDienst verkoopimport,
    AuditDienst audit,
    TimeProvider tijd,
    IEnumerable<IVirusscanner> virusscanners,
    IEnumerable<IOcrMotor> ocrMotoren)
{
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>Na zoveel gelezen pagina's wordt de tekst tussentijds bewaard.</summary>
    private const int BewaarNa = 10;

    /// <summary>Leest een stapel in en maakt het voorstel. Pagina's zonder tekst (een scan) wachten op <see cref="LeesScans"/>.</summary>
    public async Task<Uitkomst<Guid>> Lees(Uitvoerder wie, Guid administratie, Factuursoort soort, Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var naam = Path.GetFileName(bestandsnaam);
        byte[] bytes;
        IReadOnlyList<Stapelpaginatekst> paginas;
        try
        {
            bytes = await LeesBegrensd(inhoud, annulering);
            paginas = PdfStapel.Paginas(bytes);
        }
        catch (DocumentException fout)
        {
            return Uitkomst<Guid>.Mislukt($"{naam}: {fout.Message}");
        }

        if (paginas.Count == 0)
        {
            return Uitkomst<Guid>.Mislukt($"{naam}: de PDF heeft geen pagina's.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (await db.Stapels.AsNoTracking().FirstOrDefaultAsync(s => s.Sha256 == hash && s.Soort == soort, annulering) is { } eerder)
        {
            return eerder.Status == Stapelstatus.Open
                ? Uitkomst<Guid>.Met(eerder.Id)
                : Uitkomst<Guid>.Mislukt($"{naam} is op {Weergave.DatumTijd(eerder.VerwerktOp ?? eerder.IngelezenOp)} al gesplitst in {eerder.Facturen} facturen.");
        }

        if (virusscanners.FirstOrDefault() is { } scanner)
        {
            var scan = await scanner.ScanAsync(new MemoryStream(bytes), annulering);
            if (scan.Uitslag == Scanuitslag.Besmet)
            {
                await audit.Log(wie, AuditActies.DocumentQuarantaine, naam, scan.Toelichting, administratie);
                return Uitkomst<Guid>.Mislukt($"{naam} bevat kwaadaardige inhoud ({scan.Toelichting}) en is niet ingelezen.");
            }

            if (scan.Uitslag == Scanuitslag.NietGescand)
            {
                return Uitkomst<Guid>.Mislukt($"{scan.Toelichting} {naam} is niet ingelezen; probeer het later opnieuw.");
            }
        }

        var eigen = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie, annulering);
        var voorstel = Stapelsplitser.Stel([.. paginas.Select(p => new Stapelpagina(p.Pagina, p.Scan ? null : p.Regels))], soort, eigen.Ibans, eigen.BtwNummer is null ? [] : [eigen.BtwNummer]);
        var stapel = new Stapel
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Soort = soort,
            Bestandsnaam = naam.Length > 260 ? naam[..260] : naam,
            Grootte = bytes.Length,
            Sha256 = hash,
            Paginas = paginas.Count,
            Indeling = JsonSerializer.Serialize(Stand(voorstel), Json),
            TeLezen = paginas.Count(p => p.Scan),
            Status = Stapelstatus.Open,
            IngelezenOp = tijd.GetUtcNow(),
            IngelezenDoor = wie.Naam,
        };
        db.Stapels.Add(stapel);
        db.StapelInhouden.Add(new StapelInhoud { StapelId = stapel.Id, AdministratieId = administratie, Inhoud = bytes });
        await db.SaveChangesAsync(annulering);
        await audit.Log(wie, AuditActies.StapelIngelezen, naam, $"{Naam(soort)}, {paginas.Count} pagina's", administratie);
        return Uitkomst<Guid>.Met(stapel.Id);
    }

    /// <summary>De stapels van een soort, de nieuwste eerst; standaard alleen die nog open staan.</summary>
    public async Task<IReadOnlyList<Stapelkop>> Lijst(Uitvoerder wie, Guid administratie, Factuursoort soort, bool ookVerwerkt = false)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var stapels = await db.Stapels.AsNoTracking().Where(s => s.Soort == soort && (ookVerwerkt || s.Status == Stapelstatus.Open)).ToListAsync();
        return [.. stapels.OrderByDescending(s => s.IngelezenOp).Select(Kop)];
    }

    public async Task<Stapelweergave?> Open(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.Stapels.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id) is { } stapel
            ? new Stapelweergave(Kop(stapel), Paginas(stapel), ocrMotoren.Any())
            : null;
    }

    /// <summary>De PDF van de stapel (voor de miniaturen en het grote beeld).</summary>
    public async Task<byte[]?> Inhoud(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.StapelInhouden.AsNoTracking().Where(i => i.StapelId == id).Select(i => i.Inhoud).FirstOrDefaultAsync();
    }

    /// <summary>Bewaart de knippen en de weggelaten pagina's van de gebruiker (tussentijds, of vlak voor het splitsen).</summary>
    public async Task<Uitkomst> Bewaar(Uitvoerder wie, Guid administratie, Guid id, IReadOnlyCollection<int> begin, IReadOnlyCollection<int> weggelaten)
    {
        ArgumentNullException.ThrowIfNull(begin);
        ArgumentNullException.ThrowIfNull(weggelaten);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Stapels.FirstOrDefaultAsync(s => s.Id == id) is not { } stapel)
        {
            return Uitkomst.Mislukt("Deze stapel bestaat niet (meer).");
        }

        if (stapel.Status != Stapelstatus.Open)
        {
            return Uitkomst.Mislukt("Deze stapel is al gesplitst.");
        }

        if (begin.Concat(weggelaten).Any(p => p < 1 || p > stapel.Paginas))
        {
            return Uitkomst.Mislukt("Een pagina in de indeling bestaat niet.");
        }

        var paginas = Paginas(stapel);
        var nieuw = paginas.Select(p => p.Klaar ? p : p with { Begin = begin.Contains(p.Nummer), Weg = weggelaten.Contains(p.Nummer) }).ToList();
        if (!nieuw.SequenceEqual(paginas))
        {
            stapel.Indeling = JsonSerializer.Serialize(nieuw, Json);
            stapel.Aangepast = true;
            await db.SaveChangesAsync();
        }

        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Leest de pagina's zonder tekst met tekstherkenning (OCR), een voor een, en maakt daarna het voorstel opnieuw. Heeft
    /// de gebruiker intussen zelf geknipt, dan blijven zijn knippen staan en komt alleen de informatie per pagina erbij.
    /// </summary>
    /// <param name="voortgang">Na elke pagina: hoeveel er gelezen zijn van hoeveel.</param>
    public async Task<Uitkomst> LeesScans(Uitvoerder wie, Guid administratie, Guid id, IProgress<(int Gedaan, int Totaal)>? voortgang = null, CancellationToken annulering = default)
    {
        if (ocrMotoren.FirstOrDefault() is not { } motor)
        {
            return Uitkomst.Mislukt("Tekstherkenning (Tesseract) is hier niet geïnstalleerd; zet de knippen met de hand.");
        }

        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Stapels.FirstOrDefaultAsync(s => s.Id == id, annulering) is not { Status: Stapelstatus.Open } stapel)
        {
            return Uitkomst.Mislukt("Deze stapel bestaat niet (meer) of is al gesplitst.");
        }

        var inhoud = await db.StapelInhouden.SingleAsync(i => i.StapelId == id, annulering);
        var tekstlaag = Tekstlaag(inhoud);
        var lezer = new OcrLezer(motor);
        var beelden = OcrLezer.Afbeeldingen(inhoud.Inhoud, PdfStapel.MaximumBytes, PdfStapel.MaximumPaginas);
        var paginas = PdfStapel.Paginas(inhoud.Inhoud);
        var teLezen = paginas.Where(p => p.Scan && !tekstlaag.ContainsKey(p.Pagina)).Select(p => p.Pagina).ToList();
        var gedaan = 0;
        foreach (var pagina in teLezen)
        {
            annulering.ThrowIfCancellationRequested();
            var beeld = beelden.FirstOrDefault(b => b.Pagina == pagina);
            try
            {
                tekstlaag[pagina] = beeld.Afbeelding is null ? [] : [.. await lezer.LeesPaginaAsync(pagina, beeld.Afbeelding, beeld.Plek, annulering)];
            }
            catch (Exception fout) when (fout is TimeoutException or DocumentException)
            {
                // Een pagina die niet te lezen is, blijft zonder tekst: de gebruiker knipt daar zelf.
                tekstlaag[pagina] = [];
            }

            gedaan++;
            voortgang?.Report((gedaan, teLezen.Count));
            if (gedaan % BewaarNa == 0)
            {
                inhoud.Tekstlaag = JsonSerializer.Serialize(tekstlaag, Json);
                stapel.TeLezen = teLezen.Count - gedaan;
                await db.SaveChangesAsync(annulering);
            }
        }

        var eigen = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie, annulering);
        var voorstel = Stapelsplitser.Stel(
            [.. paginas.Select(p => new Stapelpagina(p.Pagina, !p.Scan ? p.Regels : tekstlaag.TryGetValue(p.Pagina, out var woorden) ? PdfTekst.Regels(woorden) : null))],
            stapel.Soort, eigen.Ibans, eigen.BtwNummer is null ? [] : [eigen.BtwNummer]);
        // De gebruiker kan intussen geknipt hebben: de actuele stand ophalen voor die erbij komt.
        await db.Entry(stapel).ReloadAsync(annulering);
        if (stapel.Status != Stapelstatus.Open)
        {
            return Uitkomst.Gelukt;
        }

        var huidig = Paginas(stapel).ToDictionary(p => p.Nummer);
        var nieuw = Stand(voorstel).Select(p => stapel.Aangepast && huidig.TryGetValue(p.Nummer, out var h) ? p with { Begin = h.Begin, Weg = h.Weg } : p).ToList();
        inhoud.Tekstlaag = JsonSerializer.Serialize(tekstlaag, Json);
        stapel.Indeling = JsonSerializer.Serialize(nieuw, Json);
        stapel.TeLezen = 0;
        await db.SaveChangesAsync(annulering);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Splitst de stapel volgens de bewaarde knippen en leest elke factuur in bij inkoop of verkoop. Lukt het inlezen van
    /// een factuur niet (bijv. de virusscanner is niet bereikbaar), dan blijft de stapel open staan; opnieuw splitsen
    /// slaat de facturen over die al zijn ingelezen.
    /// </summary>
    public async Task<Uitkomst<Stapelverslag>> Verwerk(Uitvoerder wie, Guid administratie, Guid id, IProgress<(int Gedaan, int Totaal)>? voortgang = null, CancellationToken annulering = default)
    {
        Stapel stapel;
        byte[] bytes;
        Dictionary<int, List<Tekstwoord>> tekstlaag;
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            if (await db.Stapels.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, annulering) is not { } gevonden)
            {
                return Uitkomst<Stapelverslag>.Mislukt("Deze stapel bestaat niet (meer).");
            }

            if (gevonden.Status != Stapelstatus.Open)
            {
                return Uitkomst<Stapelverslag>.Mislukt("Deze stapel is al gesplitst.");
            }

            var inhoud = await db.StapelInhouden.AsNoTracking().SingleAsync(i => i.StapelId == id, annulering);
            (stapel, bytes, tekstlaag) = (gevonden, inhoud.Inhoud, Tekstlaag(inhoud));
        }

        var paginas = Paginas(stapel);
        var groepen = new Stapelweergave(Kop(stapel), paginas, false).Groepen;
        if (groepen.Count == 0)
        {
            return Uitkomst<Stapelverslag>.Mislukt("Er blijft geen pagina over: alles is weggelaten.");
        }

        if (groepen.FirstOrDefault(g => g.Count > PdfTekst.MaximumPaginas) is { } teLang)
        {
            return Uitkomst<Stapelverslag>.Mislukt(
                $"De factuur op pagina {teLang[0]}-{teLang[^1]} heeft {teLang.Count} pagina's; een factuur mag er hoogstens {PdfTekst.MaximumPaginas} hebben. Zet daar een knip.");
        }

        IReadOnlyList<byte[]> delen;
        try
        {
            delen = PdfStapel.Splits(bytes, groepen);
        }
        catch (DocumentException fout)
        {
            return Uitkomst<Stapelverslag>.Mislukt(fout.Message);
        }

        var basis = Path.GetFileNameWithoutExtension(stapel.Bestandsnaam);
        var (geboekt, dubbel, mislukt) = (0, 0, 0);
        var meldingen = new List<string>();
        var klaar = paginas.Where(p => p.Klaar).Select(p => p.Nummer).ToHashSet();
        for (var i = 0; i < delen.Count; i++)
        {
            annulering.ThrowIfCancellationRequested();
            var groep = groepen[i];
            if (groep.All(klaar.Contains))
            {
                voortgang?.Report((i + 1, delen.Count));
                continue;
            }

            var naam = $"{basis} p. {(groep.Count == 1 ? groep[0] : $"{groep[0]}-{groep[^1]}")}.pdf";

            // De woorden van de tekstherkenning gaan mee, met de paginanummers van de losse factuur.
            var woorden = groep.SelectMany((pagina, index) => tekstlaag.GetValueOrDefault(pagina, []).Select(w => w with { Pagina = index + 1 })).ToList();
            var bekend = woorden.Count == 0 ? null : woorden;
            if (stapel.Soort == Factuursoort.Inkoop)
            {
                var uitkomst = await inkoop.Lees(wie, administratie, new MemoryStream(delen[i]), naam, "application/pdf", annulering: annulering, bekendeTekst: bekend);
                (geboekt, dubbel, mislukt) = (geboekt + (uitkomst.IsGeboekt ? 1 : 0), dubbel + (uitkomst.Dubbel ? 1 : 0), mislukt + (uitkomst.DocumentId is null ? 1 : 0));
                if (uitkomst.DocumentId is null || uitkomst.Dubbel)
                {
                    meldingen.Add(uitkomst.Melding);
                }

                if (uitkomst.DocumentId is not null)
                {
                    klaar.UnionWith(groep);
                }
            }
            else
            {
                var uitkomst = await verkoopimport.Lees(wie, administratie, new MemoryStream(delen[i]), naam, "application/pdf", bekend, annulering);
                (geboekt, dubbel, mislukt) = (geboekt + (uitkomst.Geboekt ? 1 : 0), dubbel + (uitkomst.Dubbel ? 1 : 0), mislukt + (uitkomst.Id is null ? 1 : 0));
                if (uitkomst.Id is null || uitkomst.Dubbel)
                {
                    meldingen.Add(uitkomst.Melding);
                }

                if (uitkomst.Id is not null)
                {
                    klaar.UnionWith(groep);
                }
            }

            voortgang?.Report((i + 1, delen.Count));
        }

        var verslag = new Stapelverslag(delen.Count, geboekt, dubbel, mislukt, meldingen);
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            var bewaard = await db.Stapels.SingleAsync(s => s.Id == id, annulering);
            bewaard.Indeling = JsonSerializer.Serialize(paginas.Select(p => p with { Klaar = klaar.Contains(p.Nummer) }), Json);
            if (mislukt == 0)
            {
                (bewaard.Status, bewaard.VerwerktOp, bewaard.Facturen) = (Stapelstatus.Verwerkt, tijd.GetUtcNow(), delen.Count);
            }

            await db.SaveChangesAsync(annulering);
        }

        await audit.Log(wie, AuditActies.StapelGesplitst, stapel.Bestandsnaam,
            $"{Naam(stapel.Soort)}: {delen.Count} facturen uit {stapel.Paginas} pagina's, {paginas.Count(p => p.Weg)} weggelaten{(mislukt > 0 ? $", {mislukt} niet ingelezen" : null)}", administratie);
        return Uitkomst<Stapelverslag>.Met(verslag);
    }

    /// <summary>Gooit een stapel weg die nog niet is gesplitst (verkeerd bestand); een gesplitste stapel blijft als bewijs.</summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Stapels.FirstOrDefaultAsync(s => s.Id == id) is not { } stapel)
        {
            return Uitkomst.Mislukt("Deze stapel bestaat niet (meer).");
        }

        if (stapel.Status != Stapelstatus.Open)
        {
            return Uitkomst.Mislukt("Een gesplitste stapel blijft bewaard.");
        }

        db.Stapels.Remove(stapel);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.StapelVerwijderd, stapel.Bestandsnaam, null, administratie);
        return Uitkomst.Gelukt;
    }

    private static string Naam(Factuursoort soort) => soort == Factuursoort.Inkoop ? "inkoop" : "verkoop";

    private static Stapelkop Kop(Stapel s) => new(s.Id, s.Soort, s.Bestandsnaam, s.Paginas, s.Status, s.IngelezenOp, s.IngelezenDoor, s.TeLezen, s.Facturen);

    private static List<Stapelpaginastand> Paginas(Stapel stapel) => JsonSerializer.Deserialize<List<Stapelpaginastand>>(stapel.Indeling, Json) ?? [];

    /// <summary>Het voorstel als beginstand: een lege pagina wordt weggelaten.</summary>
    private static List<Stapelpaginastand> Stand(IReadOnlyList<Paginavoorstel> voorstel) =>
        [.. voorstel.Select(p => new Stapelpaginastand(p.Nummer, p.Begin, p.Leeg, p.Twijfel, p.Leeg, p.Reden, p.Factuurnummer, p.Naam))];

    private static Dictionary<int, List<Tekstwoord>> Tekstlaag(StapelInhoud inhoud) =>
        inhoud.Tekstlaag is { Length: > 0 } tekst ? JsonSerializer.Deserialize<Dictionary<int, List<Tekstwoord>>>(tekst, Json) ?? [] : [];

    private static async Task<byte[]> LeesBegrensd(Stream stream, CancellationToken annulering)
    {
        using var kopie = new MemoryStream();
        var buffer = new byte[81920];
        int gelezen;
        while ((gelezen = await stream.ReadAsync(buffer, annulering)) > 0)
        {
            if (kopie.Length + gelezen > PdfStapel.MaximumBytes)
            {
                throw new DocumentException($"De stapel is groter dan {PdfStapel.MaximumBytes / 1024 / 1024} MB; lever hem in delen aan.");
            }

            kopie.Write(buffer, 0, gelezen);
        }

        return kopie.ToArray();
    }
}

using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Verkoop;
using Boekerhouden.Documenten;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een lay-out in de keuzelijst.</summary>
public sealed record Layoutregel(Guid Id, string Naam, bool Standaard);

/// <summary>Een lay-out met zijn ontwerp en logo; zonder <see cref="Id"/> is het de standaard die nog niet bewaard is.</summary>
public sealed record Layoutgegevens(Guid? Id, string Naam, bool Standaard, Factuurontwerp Ontwerp, Logo? Logo);

/// <summary>Een bestand om te bewaren of mee te sturen.</summary>
public sealed record Factuurbestand(byte[] Inhoud, string Naam, string Inhoudstype);

/// <summary>
/// De lay-out van de verkoopfactuur (ontwerp en logo, per administratie, één standaard) en de opmaak van een factuur
/// in dat ontwerp: de PDF, de e-factuur (UBL) en een e-mail met beide als bijlage, klaar om te versturen vanuit het
/// eigen e-mailprogramma.
/// </summary>
public sealed class FactuuropmaakDienst(Toegang toegang, AuditDienst audit, TimeProvider tijd, PdfDienst pdf, IEnumerable<IVirusscanner> virusscanners)
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<IReadOnlyList<Layoutregel>> Layouts(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return [.. (await db.Factuurlayouts.AsNoTracking().OrderByDescending(l => l.Standaard).ThenBy(l => l.Naam).Select(l => new { l.Id, l.Naam, l.Standaard }).ToListAsync())
            .Select(l => new Layoutregel(l.Id, l.Naam, l.Standaard))];
    }

    /// <summary>Een lay-out, of de standaardlay-out van de administratie (of het standaardontwerp als er nog geen is).</summary>
    public async Task<Layoutgegevens> Layout(Uitvoerder wie, Guid administratie, Guid? id = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Layout(db, id);
    }

    private static async Task<Layoutgegevens> Layout(AdministratieDbContext db, Guid? id)
    {
        var layout = id is { } gekozen
            ? await db.Factuurlayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == gekozen)
            : null;
        layout ??= await db.Factuurlayouts.AsNoTracking().OrderByDescending(l => l.Standaard).ThenBy(l => l.Naam).FirstOrDefaultAsync();
        return layout is null
            ? new Layoutgegevens(null, "Standaard", true, Factuurontwerp.Standaard, null)
            : new Layoutgegevens(layout.Id, layout.Naam, layout.Standaard, Ontwerp(layout.Ontwerp),
                layout.Logo is { Length: > 0 } logo && layout.Logotype is { } type ? new Logo(logo, type) : null);
    }

    private static Factuurontwerp Ontwerp(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<Factuurontwerp>(json, Json) ?? Factuurontwerp.Standaard).Compleet();
        }
        catch (JsonException)
        {
            return Factuurontwerp.Standaard;
        }
    }

    /// <summary>Bewaart een lay-out (nieuw zonder id); de eerste wordt de standaard.</summary>
    public async Task<Uitkomst<Guid>> Bewaar(Uitvoerder wie, Guid administratie, Guid? id, string naam, Factuurontwerp ontwerp)
    {
        ArgumentNullException.ThrowIfNull(ontwerp);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (string.IsNullOrWhiteSpace(naam))
        {
            return Uitkomst<Guid>.Mislukt("Geef de lay-out een naam.");
        }

        Factuurlayout layout;
        if (id is { } bestaand)
        {
            if (await db.Factuurlayouts.FirstOrDefaultAsync(l => l.Id == bestaand) is not { } gevonden)
            {
                return Uitkomst<Guid>.Mislukt("Deze lay-out bestaat niet (meer).");
            }

            layout = gevonden;
        }
        else
        {
            layout = new Factuurlayout { Id = Guid.NewGuid(), AdministratieId = administratie, Standaard = !await db.Factuurlayouts.AnyAsync() };
            db.Factuurlayouts.Add(layout);
        }

        layout.Naam = naam.Trim().Length > 100 ? naam.Trim()[..100] : naam.Trim();
        layout.Ontwerp = JsonSerializer.Serialize(ontwerp.Compleet(), Json);
        layout.GewijzigdOp = tijd.GetUtcNow();
        layout.GewijzigdDoor = wie.Naam;
        await db.SaveChangesAsync();
        await audit.Log(wie, id is null ? AuditActies.LayoutToegevoegd : AuditActies.LayoutGewijzigd, layout.Naam, null, administratie);
        return Uitkomst<Guid>.Met(layout.Id);
    }

    /// <summary>Zet het logo van een lay-out: alleen PNG of JPEG (op inhoud bepaald), hoogstens 1 MB, na de virusscan.</summary>
    public async Task<Uitkomst> ZetLogo(Uitvoerder wie, Guid administratie, Guid id, byte[] inhoud, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Factuurlayouts.FirstOrDefaultAsync(l => l.Id == id, annulering) is not { } layout)
        {
            return Uitkomst.Mislukt("Bewaar eerst de lay-out.");
        }

        if (inhoud.Length > Logo.MaximumGrootte)
        {
            return Uitkomst.Mislukt("Het logo is groter dan 1 MB; maak het kleiner.");
        }

        if (Logo.Soort(inhoud) is not { } soort)
        {
            return Uitkomst.Mislukt("Een logo is een PNG- of JPEG-afbeelding.");
        }

        if (virusscanners.FirstOrDefault() is { } scanner)
        {
            var scan = await scanner.ScanAsync(new MemoryStream(inhoud), annulering);
            if (scan.Uitslag != Scanuitslag.Schoon)
            {
                return Uitkomst.Mislukt(scan.Uitslag == Scanuitslag.Besmet ? $"Het logo bevat kwaadaardige inhoud ({scan.Toelichting})." : $"{scan.Toelichting} Het logo is niet bewaard.");
            }
        }

        (layout.Logo, layout.Logotype, layout.GewijzigdOp, layout.GewijzigdDoor) = (inhoud, soort, tijd.GetUtcNow(), wie.Naam);
        await db.SaveChangesAsync(annulering);
        await audit.Log(wie, AuditActies.LayoutGewijzigd, layout.Naam, "logo", administratie);
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> VerwijderLogo(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Factuurlayouts.FirstOrDefaultAsync(l => l.Id == id) is not { } layout)
        {
            return Uitkomst.Mislukt("Deze lay-out bestaat niet (meer).");
        }

        (layout.Logo, layout.Logotype) = (null, null);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.LayoutGewijzigd, layout.Naam, "logo verwijderd", administratie);
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> MaakStandaard(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var layouts = await db.Factuurlayouts.ToListAsync();
        if (layouts.FirstOrDefault(l => l.Id == id) is not { } gekozen)
        {
            return Uitkomst.Mislukt("Deze lay-out bestaat niet (meer).");
        }

        layouts.ForEach(l => l.Standaard = l.Id == id);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.LayoutGewijzigd, gekozen.Naam, "standaard", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Verwijdert een lay-out; niet de standaard (kies eerst een andere). Facturen ermee krijgen de standaard.</summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Factuurlayouts.FirstOrDefaultAsync(l => l.Id == id) is not { } layout)
        {
            return Uitkomst.Mislukt("Deze lay-out bestaat niet (meer).");
        }

        if (layout.Standaard)
        {
            return Uitkomst.Mislukt("Dit is de standaardlay-out; maak eerst een andere de standaard.");
        }

        foreach (var factuur in await db.Verkoopdocumenten.Where(v => v.LayoutId == id).ToListAsync())
        {
            factuur.LayoutId = null;
        }

        db.Factuurlayouts.Remove(layout);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.LayoutVerwijderd, layout.Naam, null, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>De factuur in zijn lay-out (voor het voorbeeld op het scherm en de PDF), of leeg als hij niet bestaat.</summary>
    public async Task<(Verkoopbeeld Beeld, Factuurontwerp Ontwerp, Logo? Logo)?> Beeld(Uitvoerder wie, Guid administratie, Guid factuur)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Beeld(db, administratie, factuur);
    }

    private static async Task<(Verkoopbeeld Beeld, Factuurontwerp Ontwerp, Logo? Logo)?> Beeld(AdministratieDbContext db, Guid administratie, Guid factuur)
    {
        if (await VerkoopDienst.Weergave(db, administratie, factuur) is not { } w)
        {
            return null;
        }

        var codes = (await BtwDienst.Lees(db)).Op(w.Factuur.Datum);
        var layout = await Layout(db, w.Invoer.LayoutId);
        return (new Verkoopbeeld(w.Factuur, w.Afzender, codes, w.Invoer.Referentie, w.Invoer.Tekst, w.Status == Verkoopstatus.Concept), layout.Ontwerp, layout.Logo);
    }

    /// <summary>De factuur als HTML-document met de opmaak voor papier (om af te drukken).</summary>
    public async Task<string?> Html(Uitvoerder wie, Guid administratie, Guid factuur) =>
        await Beeld(wie, administratie, factuur) is { } b ? FactuurHtml.Document(b.Beeld, b.Ontwerp, b.Logo) : null;

    /// <summary>De factuur als PDF (een concept met "CONCEPT" erover).</summary>
    public async Task<Uitkomst<Factuurbestand>> Pdf(Uitvoerder wie, Guid administratie, Guid factuur, CancellationToken stoppen = default)
    {
        if (await Beeld(wie, administratie, factuur) is not { } b)
        {
            return Uitkomst<Factuurbestand>.Mislukt("Deze factuur bestaat niet (meer).");
        }

        var uitkomst = await pdf.Maak(FactuurHtml.Document(b.Beeld, b.Ontwerp, b.Logo), stoppen);
        return uitkomst.IsGelukt
            ? Uitkomst<Factuurbestand>.Met(new Factuurbestand(uitkomst.Waarde!, $"{Bestandsnaam(b.Beeld)}.pdf", "application/pdf"))
            : Uitkomst<Factuurbestand>.Mislukt([.. uitkomst.Fouten]);
    }

    /// <summary>Een voorbeeld als PDF (in de ontwerper, met voorbeeldgegevens): alleen opmaak, geen gegevens uit de administratie.</summary>
    public async Task<Uitkomst<byte[]>> VoorbeeldPdf(Verkoopbeeld beeld, Factuurontwerp ontwerp, Logo? logo, CancellationToken stoppen = default) =>
        await pdf.Maak(FactuurHtml.Document(beeld, ontwerp, logo), stoppen);

    /// <summary>De e-factuur (UBL, Peppol BIS 3); alleen van een definitieve factuur.</summary>
    public async Task<Uitkomst<Factuurbestand>> Ubl(Uitvoerder wie, Guid administratie, Guid factuur)
    {
        if (await Beeld(wie, administratie, factuur) is not { } b)
        {
            return Uitkomst<Factuurbestand>.Mislukt("Deze factuur bestaat niet (meer).");
        }

        if (b.Beeld.Concept)
        {
            return Uitkomst<Factuurbestand>.Mislukt("Een e-factuur kan pas als de factuur definitief is.");
        }

        var xml = UblFactuurSchrijver.Schrijf(b.Beeld.Factuur, b.Beeld.Afzender, b.Beeld.Codes, b.Beeld.Referentie, b.Beeld.Tekst);
        return Uitkomst<Factuurbestand>.Met(new Factuurbestand(Encoding.UTF8.GetBytes(xml), $"{Bestandsnaam(b.Beeld)}.xml", "application/xml"));
    }

    /// <summary>
    /// Een e-mail aan de klant met de PDF en de e-factuur als bijlage (een .eml-bestand dat als concept opent in Outlook of
    /// een ander e-mailprogramma, om na te lezen en te versturen). Alleen van een definitieve factuur.
    /// </summary>
    public async Task<Uitkomst<Factuurbestand>> Email(Uitvoerder wie, Guid administratie, Guid factuur, CancellationToken stoppen = default)
    {
        if (await Beeld(wie, administratie, factuur) is not { } b)
        {
            return Uitkomst<Factuurbestand>.Mislukt("Deze factuur bestaat niet (meer).");
        }

        if (b.Beeld.Concept)
        {
            return Uitkomst<Factuurbestand>.Mislukt("Versturen kan pas als de factuur definitief is.");
        }

        var pdfBestand = await Pdf(wie, administratie, factuur, stoppen);
        if (!pdfBestand.IsGelukt)
        {
            return Uitkomst<Factuurbestand>.Mislukt([.. pdfBestand.Fouten]);
        }

        var ubl = (await Ubl(wie, administratie, factuur)).Waarde!;
        var f = b.Beeld.Factuur;
        var afzender = b.Beeld.Afzender;
        var soort = f.IsCredit ? "Creditfactuur" : "Factuur";
        var tekst = new StringBuilder();
        tekst.AppendLine(f.Klant.Contactpersoon is { Length: > 0 } t ? $"Beste {t}," : "Geachte heer, mevrouw,");
        tekst.AppendLine();
        tekst.AppendLine(f.IsCredit
            ? $"In de bijlage vindt u creditfactuur {f.Nummer} bij factuur {f.CreditVoor}."
            : $"In de bijlage vindt u factuur {f.Nummer} van {f.Datum.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("nl-NL"))} voor een bedrag van {Factuurteksten.Vul("#totaal", f, afzender, b.Beeld.Totaal)}.");
        if (!f.IsCredit)
        {
            tekst.AppendLine(Factuurteksten.Vul(Factuurontwerp.StandaardBetaalinstructie, f, afzender, b.Beeld.Totaal));
        }

        tekst.AppendLine();
        tekst.AppendLine("Met vriendelijke groet,");
        tekst.AppendLine(afzender.Naam);
        var eml = Eml(afzender.Email, f.Klant.Email, $"{soort} {f.Nummer} van {afzender.Naam}", tekst.ToString(), [pdfBestand.Waarde!, ubl]);
        await audit.Log(wie, AuditActies.VerkoopfactuurEmail, $"{soort} {f.Nummer}", f.Klant.Email, administratie);
        return Uitkomst<Factuurbestand>.Met(new Factuurbestand(eml, $"{Bestandsnaam(b.Beeld)}.eml", "message/rfc822"));
    }

    /// <summary>Een e-mail als concept (<c>X-Unsent: 1</c>), MIME met bijlagen in base64.</summary>
    internal static byte[] Eml(string? van, string? aan, string onderwerp, string tekst, IReadOnlyList<Factuurbestand> bijlagen)
    {
        var grens = $"=_{Guid.NewGuid():N}";
        var mail = new StringBuilder();
        void Regel(string r) => mail.Append(r).Append("\r\n");
        if (IsAdres(van))
        {
            Regel($"From: {van}");
        }

        if (IsAdres(aan))
        {
            Regel($"To: {aan}");
        }

        Regel($"Subject: =?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(onderwerp))}?=");
        Regel("X-Unsent: 1");
        Regel("MIME-Version: 1.0");
        Regel($"Content-Type: multipart/mixed; boundary=\"{grens}\"");
        Regel(string.Empty);
        Regel($"--{grens}");
        Regel("Content-Type: text/plain; charset=utf-8");
        Regel("Content-Transfer-Encoding: base64");
        Regel(string.Empty);
        Regel(Base64(Encoding.UTF8.GetBytes(tekst)));
        foreach (var b in bijlagen)
        {
            var naam = $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(b.Naam))}?=";
            Regel($"--{grens}");
            Regel($"Content-Type: {b.Inhoudstype}; name=\"{naam}\"");
            Regel("Content-Transfer-Encoding: base64");
            Regel($"Content-Disposition: attachment; filename=\"{naam}\"");
            Regel(string.Empty);
            Regel(Base64(b.Inhoud));
        }

        Regel($"--{grens}--");
        return Encoding.UTF8.GetBytes(mail.ToString());
    }

    private static bool IsAdres(string? adres)
    {
        if (string.IsNullOrWhiteSpace(adres) || adres.Contains('\r', StringComparison.Ordinal) || adres.Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return new MailAddress(adres).Address == adres.Trim();
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Base64 in regels van 76 tekens (MIME).</summary>
    private static string Base64(byte[] inhoud) =>
        string.Join("\r\n", Convert.ToBase64String(inhoud).Chunk(76).Select(c => new string(c)));

    private static string Bestandsnaam(Verkoopbeeld beeld) =>
        string.Concat($"{(beeld.Factuur.IsCredit ? "Creditfactuur" : "Factuur")} {beeld.Factuur.Nummer ?? "concept"} {beeld.Factuur.Klant.Naam}"
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '-' : c)).Trim();
}

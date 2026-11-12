using System.Security.Cryptography;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Verkoop;
using Boekerhouden.Documenten;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat er met een ingelezen verkoopfactuur gebeurd is.</summary>
/// <param name="Id">De verkoopfactuur (leeg als hij niet is ingelezen).</param>
/// <param name="Dubbel">Hetzelfde bestand is al eerder ingelezen: niet opnieuw ingelezen.</param>
public sealed record Verkoopinleesuitkomst(Guid? Id, bool Geboekt, string Melding, bool Dubbel = false);

/// <summary>
/// Het origineel van een ingelezen verkoopfactuur om naast het formulier te tonen en er gebieden op aan te wijzen.
/// </summary>
/// <param name="Pdf">De PDF voor de viewer: het bestand zelf, of een foto als pagina; leeg als er alleen een afbeelding is.</param>
/// <param name="Afbeelding">Een foto of scan waar geen PDF van te maken was (geen tekstherkenning): alleen bekijken.</param>
/// <param name="Woorden">De woorden met hun plek (uit de PDF of van de tekstherkenning); leeg = aanwijzen kan niet.</param>
public sealed record Verkooporigineelbeeld(byte[]? Pdf, (byte[] Inhoud, string Inhoudstype)? Afbeelding, IReadOnlyList<Tekstwoord> Woorden, string Bestandsnaam);

/// <summary>
/// Verkoopfacturen uit een ander pakket inlezen: een PDF, e-factuur (UBL) of scan. Eerst de virusscan, dan dezelfde
/// herkenning als bij inkoop (bij een verkoopfactuur is de eigen onderneming de afzender, dus de andere partij op de
/// factuur is de klant). Herkent het pakket de klant en klopt alles (nummer, datum, sluitende bedragen, niet dubbel),
/// dan boekt het de factuur meteen met het nummer van het andere pakket; anders staat hij als concept bij Verkoop, met
/// de reden. Het origineel blijft bewaard bij de factuur.
/// </summary>
public sealed class VerkoopimportDienst(
    Toegang toegang,
    VerkoopDienst verkoop,
    AuditDienst audit,
    TimeProvider tijd,
    IEnumerable<IVirusscanner> virusscanners,
    IEnumerable<IOcrMotor> ocrMotoren)
{
    public Task<Verkoopinleesuitkomst> Lees(Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, string? inhoudstype, CancellationToken annulering = default) =>
        Lees(wie, administratie, inhoud, bestandsnaam, inhoudstype, null, annulering);

    /// <param name="bekendeTekst">De woorden van een scan die al gelezen zijn (bij het splitsen van een stapel).</param>
    public async Task<Verkoopinleesuitkomst> Lees(
        Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, string? inhoudstype, IReadOnlyList<Tekstwoord>? bekendeTekst, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var naam = Path.GetFileName(bestandsnaam);
        byte[] bytes;
        try
        {
            bytes = await LeesBegrensd(inhoud, annulering);
        }
        catch (DocumentException fout)
        {
            return new(null, false, $"{naam}: {fout.Message}");
        }

        Guid id;
        bool zonderBelemmering;
        string melding;
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (await db.Verkoopbestanden.AsNoTracking().FirstOrDefaultAsync(b => b.Sha256 == hash, annulering) is { } eerder)
            {
                return new(eerder.VerkoopdocumentId, false, $"Dubbele factuur: {naam} is al eerder ingelezen en niet opnieuw ingelezen.", Dubbel: true);
            }

            if (virusscanners.FirstOrDefault() is { } scanner)
            {
                var scan = await scanner.ScanAsync(new MemoryStream(bytes), annulering);
                if (scan.Uitslag != Scanuitslag.Schoon)
                {
                    if (scan.Uitslag == Scanuitslag.Besmet)
                    {
                        await audit.Log(wie, AuditActies.DocumentQuarantaine, naam, scan.Toelichting, administratie);
                    }

                    return new(null, false, scan.Uitslag == Scanuitslag.Besmet
                        ? $"{naam} bevat kwaadaardige inhoud ({scan.Toelichting}) en is niet ingelezen."
                        : $"{scan.Toelichting} {naam} is niet ingelezen; probeer het later opnieuw.");
                }
            }

            var a = await db.Administraties.AsNoTracking().SingleAsync(x => x.Id == administratie, annulering);
            var eigen = new EigenGegevens(a.Ibans, a.BtwNummer is null ? [] : [a.BtwNummer], [a.Naam]);
            var ocr = ocrMotoren.FirstOrDefault() is { } motor ? new OcrLezer(motor) : null;
            var herkenning = await Factuurherkenning.Standaard(eigen, ocr: ocr).HerkenAsync(new MemoryStream(bytes), naam, inhoudstype, bekendeTekst, annulering);
            var herkend = ZonderEigenKvk(herkenning.Factuur ?? new HerkendeFactuur(Herkenningsbron.Tekst), a.KvkNummer);

            var context = await verkoop.Context(wie, administratie);
            var relaties = await db.Relaties.AsNoTracking().ToListAsync(annulering);
            var klanten = relaties.Where(r => r.Klant).ToList();
            var treffer = Verkoopimport.Klant(herkend, klanten.Select(r => r.NaarDomein()))
                ?? Verkoopimport.Klant(herkend, relaties.Select(r => r.NaarDomein()));
            var relatie = treffer is null ? null : relaties.First(r => r.Code == treffer.Relatie.Code);
            var datum = herkend.Factuurdatum ?? DateOnly.FromDateTime(tijd.GetLocalNow().DateTime);
            var codes = context.Btwtabel.Op(datum);
            var omzet = relatie?.Omzetrekening ?? context.Omzetrekening ?? "";
            var regels = Verkoopimport.Regels(herkend, codes, omzet, relatie?.Land ?? herkend.Afnemer?.Land);
            var vervaldatum = herkend.Vervaldatum ?? datum.AddDays(relatie?.Betalingstermijn ?? context.Betalingstermijn);
            var invoer = new Verkoopinvoer(relatie?.Code ?? "", datum, vervaldatum,
                Omschrijving(context.OmschrijvingIngelezen, herkend, relatie?.Naam, datum, vervaldatum), regels, Factuurnummer: herkend.Factuurnummer);
            var klant = relatie ?? new Handelsrelatie
            {
                Code = "",
                Naam = Verkoopimport.Klantnaam(herkend) ?? "",
                BtwNummer = herkend.Afnemer?.BtwNummer,
                KvkNummer = herkend.Afnemer?.KvkNummer,
                Land = herkend.Afnemer?.Land,
            };
            var document = VerkoopDienst.NieuwConcept(administratie, invoer, klant, wie, tijd.GetUtcNow(), ingelezen: true);
            document.Betalingskenmerk = herkend.Betalingskenmerk;

            var belemmeringen = (herkenning.Factuur is null ? [herkenning.Melding ?? "niets herkend"] : Verkoopimport.Belemmeringen(herkend, VerkoopDienst.Domein(document), codes, relatie is not null)).ToList();
            if (herkend.Factuurnummer is { } nummer && await db.Verkoopdocumenten.AnyAsync(v => v.Nummer == nummer || v.ExternNummer == nummer, annulering))
            {
                belemmeringen.Add($"factuur {nummer} staat al in de administratie");
            }

            document.Melding = belemmeringen.Count == 0 ? null : Kort($"Niet vanzelf geboekt: {string.Join("; ", belemmeringen)}.", 2000);
            db.Verkoopdocumenten.Add(document);
            db.Verkoopbestanden.Add(new Verkoopbestand
            {
                VerkoopdocumentId = document.Id,
                AdministratieId = administratie,
                Bestandsnaam = Kort(naam, 260),
                Inhoudstype = Bestandssoort.Extensie(bytes) switch { ".pdf" => "application/pdf", ".xml" => "application/xml", _ => Export.Logo.Soort(bytes) ?? "application/octet-stream" },
                Sha256 = hash,
                Inhoud = bytes,
            });
            await db.SaveChangesAsync(annulering);
            await audit.Log(wie, AuditActies.VerkoopfactuurIngelezen, naam, $"{herkend.Factuurnummer} {klant.Naam}".Trim(), administratie);
            (id, zonderBelemmering, melding) = (document.Id, belemmeringen.Count == 0, document.Melding ?? "");
        }

        if (zonderBelemmering)
        {
            var geboekt = await verkoop.MaakDefinitief(wie, administratie, id);
            if (geboekt.IsGelukt)
            {
                return new(id, true, $"{naam}: factuur {geboekt.Waarde} geboekt.");
            }

            melding = Kort($"Niet vanzelf geboekt: {string.Join(" ", geboekt.Fouten)}", 2000);
            await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
            (await db.Verkoopdocumenten.SingleAsync(v => v.Id == id, annulering)).Melding = melding;
            await db.SaveChangesAsync(annulering);
        }

        return new(id, false, $"{naam}: {melding}");
    }

    /// <summary>
    /// De omschrijving van een ingelezen factuur: de standaard van het kantoor (met variabelen) als die er is, anders
    /// de omschrijving op de factuur. Is de klant nog niet bekend, dan blijven de variabelen staan; ze worden gevuld
    /// bij het definitief maken, als de klant gekozen is.
    /// </summary>
    internal static string Omschrijving(string? standaard, HerkendeFactuur herkend, string? klant, DateOnly datum, DateOnly vervaldatum)
    {
        if (string.IsNullOrWhiteSpace(standaard))
        {
            return herkend.Omschrijving ?? $"Factuur {herkend.Factuurnummer}".Trim();
        }

        // De omschrijving op de factuur is alleen nu bekend: die gaat er meteen in.
        var metOmschrijving = standaard.Replace("#omschrijving", herkend.Omschrijving ?? "", StringComparison.OrdinalIgnoreCase);
        return klant is null || herkend.Factuurnummer is null
            ? metOmschrijving
            : Boekhouding.Facturen.Omschrijvingssjabloon.Vul(metOmschrijving, new(herkend.Factuurnummer, herkend.Omschrijving, klant, datum, vervaldatum, herkend.Betalingskenmerk));
    }

    /// <summary>
    /// Op een eigen verkoopfactuur staat het eigen KvK-nummer: dat is niet van de klant. Net als de eigen naam, het
    /// btw-nummer en de IBAN's telt het niet mee bij het zoeken van de afnemer.
    /// </summary>
    public static HerkendeFactuur ZonderEigenKvk(HerkendeFactuur herkend, string? eigenKvk)
    {
        ArgumentNullException.ThrowIfNull(herkend);
        static string Cijfers(string? tekst) => new((tekst ?? "").Where(char.IsAsciiDigit).ToArray());
        var eigen = Cijfers(eigenKvk);
        if (eigen.Length == 0)
        {
            return herkend;
        }

        return herkend with
        {
            KvkNummer = Cijfers(herkend.KvkNummer) == eigen ? null : herkend.KvkNummer,
            Afnemer = herkend.Afnemer is { } afnemer && Cijfers(afnemer.KvkNummer) == eigen ? afnemer with { KvkNummer = null } : herkend.Afnemer,
        };
    }

    /// <summary>Het origineel van een ingelezen verkoopfactuur.</summary>
    public async Task<(byte[] Inhoud, string Inhoudstype, string Bestandsnaam)?> Bestand(Uitvoerder wie, Guid administratie, Guid factuur)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.Verkoopbestanden.AsNoTracking().FirstOrDefaultAsync(b => b.VerkoopdocumentId == factuur) is { } b
            ? (b.Inhoud, b.Inhoudstype, b.Bestandsnaam)
            : null;
    }

    /// <summary>
    /// Het origineel met de woorden en hun plek, voor het aanwijzen van een gebied (zoals bij inkoop). Een PDF met tekst
    /// geeft zijn eigen woorden; een scan of foto gaat door de tekstherkenning (een foto wordt daarbij een PDF van één
    /// pagina). Een e-factuur (XML) heeft geen beeld: leeg.
    /// </summary>
    public async Task<Verkooporigineelbeeld?> Beeld(Uitvoerder wie, Guid administratie, Guid factuur, CancellationToken annulering = default)
    {
        if (await Bestand(wie, administratie, factuur) is not { } bestand)
        {
            return null;
        }

        var ocr = ocrMotoren.FirstOrDefault() is { } motor ? new OcrLezer(motor) : null;
        if (bestand.Inhoudstype == "application/pdf")
        {
            IReadOnlyList<Tekstwoord> woorden;
            try
            {
                woorden = PdfTekst.Woorden(bestand.Inhoud);
                if (woorden.Count < 5 && ocr is not null && await ocr.LeesAsync(bestand.Inhoud, annulering) is { } laag)
                {
                    woorden = laag.Woorden;
                }
            }
            catch (DocumentException)
            {
                woorden = [];
            }

            return new(bestand.Inhoud, null, woorden, bestand.Bestandsnaam);
        }

        if (!bestand.Inhoudstype.StartsWith("image/", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            if (ocr is not null && await ocr.LeesAsync(bestand.Inhoud, annulering) is { Pdf: { } pdf } foto)
            {
                return new(pdf, null, foto.Woorden, bestand.Bestandsnaam);
            }
        }
        catch (DocumentException)
        {
            // Niet te lezen: dan alleen de afbeelding.
        }

        return new(null, (bestand.Inhoud, bestand.Inhoudstype), [], bestand.Bestandsnaam);
    }

    private static async Task<byte[]> LeesBegrensd(Stream stream, CancellationToken annulering)
    {
        using var kopie = new MemoryStream();
        var buffer = new byte[81920];
        int gelezen;
        while ((gelezen = await stream.ReadAsync(buffer, annulering)) > 0)
        {
            if (kopie.Length + gelezen > PdfTekst.MaximumBytes)
            {
                throw new DocumentException($"Het bestand is groter dan {PdfTekst.MaximumBytes / 1024 / 1024} MB.");
            }

            kopie.Write(buffer, 0, gelezen);
        }

        return kopie.ToArray();
    }

    private static string Kort(string tekst, int lengte) => tekst.Length > lengte ? tekst[..lengte] : tekst;
}

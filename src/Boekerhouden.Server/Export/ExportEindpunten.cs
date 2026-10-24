using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Identiteit;
using Microsoft.AspNetCore.Mvc;

namespace Boekerhouden.Server.Export;

/// <summary>Downloads: de correcties van een dossier als Excel of PDF, het rapport als PDF en bijlagen.</summary>
internal static class ExportEindpunten
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(WebApplication app)
    {
        app.MapGet("/dossiers/{id:guid}/correcties.xlsx", Excel).RequireAuthorization();
        app.MapGet("/dossiers/{id:guid}/correcties.pdf", Pdf).RequireAuthorization();
        app.MapGet("/dossiers/{id:guid}/rapport.pdf", RapportPdf).RequireAuthorization();
        app.MapGet("/bijlagen/{id:guid}", Bijlage).RequireAuthorization();
    }

    private static async Task<IResult> RapportPdf(Guid id, RapportDienst rapporten, PdfDienst pdf, AuditDienst audit, HttpContext context)
    {
        if (await rapporten.Maak(id) is not { } uitkomst)
        {
            return TypedResults.NotFound();
        }

        if (!uitkomst.IsGelukt)
        {
            return TypedResults.Content(string.Join(" ", uitkomst.Fouten), "text/plain; charset=utf-8", statusCode: StatusCodes.Status409Conflict);
        }

        var inhoud = uitkomst.Waarde!;
        var bestand = await pdf.Maak(await RapportHtml.Maak(context.RequestServices, inhoud), context.RequestAborted);
        if (!bestand.IsGelukt)
        {
            return TypedResults.Content(string.Join(" ", bestand.Fouten), "text/plain; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await audit.Log(context.User.AlsUitvoerder(), AuditActies.RapportGemaakt,
            $"{inhoud.Dossier.Klantnummer} {inhoud.Dossier.KlantNaam} – {inhoud.Dossier.Naam}",
            $"{inhoud.Rapport.Titel}{(inhoud.Rapport.Concept ? " (concept)" : null)}, PDF");
        return TypedResults.File(bestand.Waarde!, "application/pdf", RapportHtml.Bestandsnaam(inhoud));
    }

    /// <summary>Een bijlage downloaden; altijd als bestand (nooit in de browser tonen) en zonder type-raden.</summary>
    private static async Task<IResult> Bijlage(Guid id, BijlageDienst bijlagen, HttpContext context)
    {
        if (await bijlagen.Haal(id) is not { } bestand)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(bestand.Inhoud, VeiligType(bestand.ContentType), bestand.Bestandsnaam);
    }

    private static readonly HashSet<string> VeiligeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "text/csv", "text/plain", Xlsx,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel", "application/msword", "message/rfc822",
    };

    private static string VeiligType(string type) => VeiligeTypes.Contains(type) ? type : "application/octet-stream";

    /// <summary>Het downloadadres; met <paramref name="alleenTeVerwerken"/> alleen wat de klant nog moet boeken.</summary>
    public static string Adres(Guid dossierId, string formaat, bool alleenTeVerwerken) =>
        $"/dossiers/{dossierId}/correcties.{formaat}{(alleenTeVerwerken ? "?teVerwerken=true" : null)}";

    private static async Task<IResult> Excel(Guid id, [FromQuery] bool? teVerwerken, CorrectieDienst correcties, AuditDienst audit, TimeProvider tijd, HttpContext context)
    {
        if (await Verzoek(id, teVerwerken, correcties, tijd, context) is not { } verzoek)
        {
            return TypedResults.NotFound();
        }

        var bestand = CorrectieExport.Excel(verzoek);
        await Log(audit, context, verzoek, "Excel");
        return TypedResults.File(bestand, Xlsx, verzoek.Bestandsnaam("xlsx"));
    }

    private static async Task<IResult> Pdf(Guid id, [FromQuery] bool? teVerwerken, CorrectieDienst correcties, PdfDienst pdf, AuditDienst audit, TimeProvider tijd, HttpContext context)
    {
        if (await Verzoek(id, teVerwerken, correcties, tijd, context) is not { } verzoek)
        {
            return TypedResults.NotFound();
        }

        var uitkomst = await pdf.Maak(CorrectieExport.Html(verzoek), context.RequestAborted);
        if (!uitkomst.IsGelukt)
        {
            return TypedResults.Content(string.Join(" ", uitkomst.Fouten), "text/plain; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await Log(audit, context, verzoek, "PDF");
        return TypedResults.File(uitkomst.Waarde!, "application/pdf", verzoek.Bestandsnaam("pdf"));
    }

    private static async Task<ExportVerzoek?> Verzoek(Guid id, bool? teVerwerken, CorrectieDienst correcties, TimeProvider tijd, HttpContext context) =>
        await correcties.Overzicht(id) is { } overzicht
            ? new ExportVerzoek(overzicht, teVerwerken == true, context.User.AlsUitvoerder().Naam, tijd.GetUtcNow())
            : null;

    private static Task Log(AuditDienst audit, HttpContext context, ExportVerzoek verzoek, string formaat) =>
        audit.Log(context.User.AlsUitvoerder(), AuditActies.CorrectiesGeexporteerd,
            $"{verzoek.Overzicht.Dossier.Klantnummer} {verzoek.Overzicht.Dossier.KlantNaam} – {verzoek.Overzicht.Dossier.Naam}",
            $"{formaat}, {verzoek.Boekingen.Count} boeking(en){(verzoek.AlleenTeVerwerken ? ", alleen nog te verwerken door de klant" : null)}");
}

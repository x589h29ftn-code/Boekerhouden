using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Identiteit;
using Microsoft.AspNetCore.Mvc;

namespace Boekerhouden.Server.Export;

/// <summary>Downloads: de correcties van een dossier als Excel of PDF. Elke export komt in het wijzigingslog.</summary>
internal static class ExportEindpunten
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(WebApplication app)
    {
        app.MapGet("/dossiers/{id:guid}/correcties.xlsx", Excel).RequireAuthorization();
        app.MapGet("/dossiers/{id:guid}/correcties.pdf", Pdf).RequireAuthorization();
    }

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

using Boekerhouden.Administratie.Export;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>De grootboekkaart en de kolommenbalans als Excel of PDF (A4 liggend), voor de download.</summary>
public sealed class OverzichtDienst(Toegang toegang, GrootboekDienst grootboek, PdfDienst pdf)
{
    public async Task<byte[]> KaartExcel(Uitvoerder wie, Guid administratie, string rekening, DateOnly van, DateOnly tot) =>
        OverzichtExport.KaartExcel(await grootboek.Kaart(wie, administratie, rekening, van, tot), await Naam(wie, administratie));

    public async Task<Uitkomst<byte[]>> KaartPdf(Uitvoerder wie, Guid administratie, string rekening, DateOnly van, DateOnly tot, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.KaartHtml(await grootboek.Kaart(wie, administratie, rekening, van, tot), await Naam(wie, administratie)), stoppen);

    public async Task<byte[]> KolommenbalansExcel(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot) =>
        OverzichtExport.KolommenbalansExcel(await grootboek.Kolommenbalans(wie, administratie, van, tot), await Naam(wie, administratie));

    public async Task<Uitkomst<byte[]>> KolommenbalansPdf(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.KolommenbalansHtml(await grootboek.Kolommenbalans(wie, administratie, van, tot), await Naam(wie, administratie)), stoppen);

    private async Task<string> Naam(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.Administraties.AsNoTracking().Where(a => a.Id == administratie).Select(a => a.Naam).SingleAsync();
    }
}

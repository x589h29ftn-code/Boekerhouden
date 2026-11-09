using Boekerhouden.Administratie.Export;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>De grootboekkaart en de kolommenbalans als Excel of PDF (A4 liggend), voor de download.</summary>
public sealed class OverzichtDienst(Toegang toegang, GrootboekDienst grootboek, ActivaDienst activa, PdfDienst pdf)
{
    public async Task<byte[]> KaartExcel(Uitvoerder wie, Guid administratie, string rekening, DateOnly van, DateOnly tot) =>
        OverzichtExport.KaartExcel(await grootboek.Kaart(wie, administratie, rekening, van, tot), await Naam(wie, administratie));

    public async Task<Uitkomst<byte[]>> KaartPdf(Uitvoerder wie, Guid administratie, string rekening, DateOnly van, DateOnly tot, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.KaartHtml(await grootboek.Kaart(wie, administratie, rekening, van, tot), await Naam(wie, administratie)), stoppen);

    /// <summary>Alle grootboekkaarten (rekeningen met een saldo of mutaties) onder elkaar, als Excel.</summary>
    public async Task<byte[]> KaartenExcel(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot) =>
        OverzichtExport.KaartenExcel(await grootboek.Kaarten(wie, administratie, van, tot), await Naam(wie, administratie), van, tot);

    /// <summary>Alle grootboekkaarten onder elkaar, als PDF.</summary>
    public async Task<Uitkomst<byte[]>> KaartenPdf(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.KaartenHtml(await grootboek.Kaarten(wie, administratie, van, tot), await Naam(wie, administratie), van, tot), stoppen);

    public async Task<byte[]> KolommenbalansExcel(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot) =>
        OverzichtExport.KolommenbalansExcel(await grootboek.Kolommenbalans(wie, administratie, van, tot), await Naam(wie, administratie));

    public async Task<Uitkomst<byte[]>> KolommenbalansPdf(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.KolommenbalansHtml(await grootboek.Kolommenbalans(wie, administratie, van, tot), await Naam(wie, administratie)), stoppen);

    /// <summary>Een overzicht zoals het op het scherm staat (bijv. de kolommenbalans per maand of uitgebreid), als Excel.</summary>
    public async Task<byte[]> TabelExcel(Uitvoerder wie, Guid administratie, Exporttabel tabel) =>
        OverzichtExport.TabelExcel(tabel, await Naam(wie, administratie));

    /// <summary>Een overzicht zoals het op het scherm staat, als PDF (A4 liggend).</summary>
    public async Task<Uitkomst<byte[]>> TabelPdf(Uitvoerder wie, Guid administratie, Exporttabel tabel, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.TabelHtml(tabel, await Naam(wie, administratie)), stoppen);

    /// <summary>De kolommenbalans met dezelfde periode een jaar eerder ernaast, als Excel.</summary>
    public async Task<byte[]> VergelijkingExcel(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot) =>
        OverzichtExport.VergelijkingExcel(await Vergelijking(wie, administratie, van, tot), van, tot, await Naam(wie, administratie));

    /// <summary>De kolommenbalans met dezelfde periode een jaar eerder ernaast, als PDF.</summary>
    public async Task<Uitkomst<byte[]>> VergelijkingPdf(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.VergelijkingHtml(await Vergelijking(wie, administratie, van, tot), van, tot, await Naam(wie, administratie)), stoppen);

    /// <summary>De activastaat van een jaar als Excel.</summary>
    public async Task<byte[]> ActivastaatExcel(Uitvoerder wie, Guid administratie, int jaar) =>
        OverzichtExport.ActivastaatExcel(await activa.Activastaat(wie, administratie, jaar), jaar, await Naam(wie, administratie));

    /// <summary>De activastaat van een jaar als PDF (A4 liggend).</summary>
    public async Task<Uitkomst<byte[]>> ActivastaatPdf(Uitvoerder wie, Guid administratie, int jaar, CancellationToken stoppen = default) =>
        await pdf.Maak(OverzichtExport.ActivastaatHtml(await activa.Activastaat(wie, administratie, jaar), jaar, await Naam(wie, administratie)), stoppen);

    private async Task<IReadOnlyList<Boekhouding.Grootboek.Vergelijkingsregel>> Vergelijking(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot) =>
        Boekhouding.Grootboek.Jaarvergelijking.Maak(
            await grootboek.Kolommenbalans(wie, administratie, van, tot),
            await grootboek.Kolommenbalans(wie, administratie, van.AddYears(-1), tot.AddYears(-1)));

    private async Task<string> Naam(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.Administraties.AsNoTracking().Where(a => a.Id == administratie).Select(a => a.Naam).SingleAsync();
    }
}

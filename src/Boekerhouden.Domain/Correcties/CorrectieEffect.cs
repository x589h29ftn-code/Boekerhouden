namespace Boekerhouden.Domain.Correcties;

/// <summary>
/// Het effect van correcties op het resultaat en het eigen vermogen, positief = hoger. Regels op een W&amp;V-rekening
/// veranderen het resultaat (en daarmee het eigen vermogen); regels op het eigen vermogen zelf (RGS <c>BEiv…</c>)
/// alleen het eigen vermogen.
/// </summary>
/// <param name="Ongekoppeld">Het aantal regels op rekeningen die nog niet aan RGS zijn gekoppeld; daarvan is het
/// effect nog onbekend.</param>
public readonly record struct CorrectieEffect(decimal Resultaat, decimal EigenVermogen, int Ongekoppeld)
{
    public const string EigenVermogenCode = "BEiv";

    /// <param name="codeVan">De RGS-code waaraan een rekening is gekoppeld, of null als die nog niet gekoppeld is.</param>
    public static CorrectieEffect Bereken(IEnumerable<Boekingsregel> regels, Func<Guid, string?> codeVan)
    {
        ArgumentNullException.ThrowIfNull(regels);
        ArgumentNullException.ThrowIfNull(codeVan);
        decimal resultaat = 0, eigenVermogen = 0;
        var ongekoppeld = 0;
        foreach (var regel in regels)
        {
            var code = codeVan(regel.RekeningId);
            if (code is null)
            {
                ongekoppeld++;
            }
            else if (code.StartsWith('W'))
            {
                // Debet in de W&V is kosten: het resultaat (credit = winst) daalt.
                resultaat -= regel.Bedrag;
            }
            else if (code.StartsWith(EigenVermogenCode, StringComparison.Ordinal))
            {
                eigenVermogen -= regel.Bedrag;
            }
        }

        return new CorrectieEffect(resultaat, eigenVermogen + resultaat, ongekoppeld);
    }

    public CorrectieEffect Plus(CorrectieEffect ander) =>
        new(Resultaat + ander.Resultaat, EigenVermogen + ander.EigenVermogen, Ongekoppeld + ander.Ongekoppeld);
}

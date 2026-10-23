namespace Boekerhouden.Domain.Correcties;

/// <summary>
/// Het saldo van een grootboekrekening volgens de administratie van de klant, de correcties in het dossier en het
/// gecorrigeerde saldo (huidig), met hetzelfde voor het vorige jaar. Null = niet in de saldibalans.
/// </summary>
public readonly record struct GecorrigeerdSaldo(decimal? Administratie, decimal Correcties, decimal? VorigAdministratie, decimal VorigCorrecties)
{
    /// <summary>Administratie + correcties; null als de rekening in dit jaar nergens voorkomt.</summary>
    public decimal? Huidig => Administratie is null && Correcties == 0 ? null : (Administratie ?? 0) + Correcties;

    public decimal? Vorig => VorigAdministratie is null && VorigCorrecties == 0 ? null : (VorigAdministratie ?? 0) + VorigCorrecties;

    /// <summary>
    /// Zet de saldi uit de saldibalansen en de correcties samen, per jaar en per soort afgerond op hele euro's (met
    /// verdeling van afrondingsverschillen). Zo tellen administratie en correcties precies op tot het gecorrigeerde
    /// saldo, en blijft een sluitende saldibalans na correcties sluiten.
    /// </summary>
    public static Dictionary<TSleutel, GecorrigeerdSaldo> Bereken<TSleutel>(
        IReadOnlyDictionary<TSleutel, decimal> administratie,
        IReadOnlyDictionary<TSleutel, decimal> correcties,
        IReadOnlyDictionary<TSleutel, decimal> vorigAdministratie,
        IReadOnlyDictionary<TSleutel, decimal> vorigCorrecties)
        where TSleutel : notnull
    {
        ArgumentNullException.ThrowIfNull(administratie);
        ArgumentNullException.ThrowIfNull(correcties);
        ArgumentNullException.ThrowIfNull(vorigAdministratie);
        ArgumentNullException.ThrowIfNull(vorigCorrecties);
        var adm = Afronding.OpHeleEuros(administratie);
        var cor = Afronding.OpHeleEuros(correcties);
        var vAdm = Afronding.OpHeleEuros(vorigAdministratie);
        var vCor = Afronding.OpHeleEuros(vorigCorrecties);
        return adm.Keys.Union(cor.Keys).Union(vAdm.Keys).Union(vCor.Keys).ToDictionary(
            k => k,
            k => new GecorrigeerdSaldo(
                adm.TryGetValue(k, out var a) ? a : null,
                cor.GetValueOrDefault(k),
                vAdm.TryGetValue(k, out var v) ? v : null,
                vCor.GetValueOrDefault(k)));
    }
}

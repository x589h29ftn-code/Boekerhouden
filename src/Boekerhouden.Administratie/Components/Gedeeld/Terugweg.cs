using Microsoft.AspNetCore.Components;

namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>
/// Doorklikken met de weg terug: het adres waar je nu bent gaat als <c>terug</c> mee, zodat de pijl linksboven op de
/// volgende pagina (bijv. een boeking vanaf de grootboekkaart) weer naar dit scherm leidt, met dezelfde rekening en periode.
/// </summary>
public static class Terugweg
{
    /// <summary>Het adres van deze pagina, met de query (bijv. <c>/a/…/grootboek?rekening=4300&amp;van=…</c>).</summary>
    public static string Hier(this NavigationManager navigatie)
    {
        ArgumentNullException.ThrowIfNull(navigatie);
        return "/" + navigatie.ToBaseRelativePath(navigatie.Uri);
    }

    /// <summary>Het adres met <c>terug</c> naar deze pagina erbij.</summary>
    public static string MetTerug(this NavigationManager navigatie, string adres)
    {
        ArgumentNullException.ThrowIfNull(adres);
        return $"{adres}{(adres.Contains('?', StringComparison.Ordinal) ? '&' : '?')}terug={Uri.EscapeDataString(navigatie.Hier())}";
    }
}

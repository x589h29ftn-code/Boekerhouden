using Microsoft.AspNetCore.Components;

namespace Boekerhouden.Administratie.Identiteit;

/// <summary>Doorverwijzen vanuit de (statische) accountpagina's, zonder open redirects naar andere sites.</summary>
public sealed class Doorverwijzer(NavigationManager navigatie)
{
    public void NaarLokaal(string? uri) => navigatie.NavigateTo(IsLokaal(uri) ? uri! : "/");

    /// <summary>Alleen paden binnen deze site ("/klanten/..."), niet "//andere-site" of "https://...".</summary>
    internal static bool IsLokaal(string? uri) =>
        !string.IsNullOrEmpty(uri) && uri[0] == '/' && (uri.Length == 1 || (uri[1] != '/' && uri[1] != '\\'));
}

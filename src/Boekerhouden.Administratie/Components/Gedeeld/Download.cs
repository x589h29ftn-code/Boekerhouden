using Microsoft.JSInterop;

namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>Een bestand aan de gebruiker geven om te bewaren (Excel, PDF), via <c>download.js</c>.</summary>
public static class Download
{
    public const string Excel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Pdf = "application/pdf";

    public static async Task Bewaar(this IJSRuntime js, byte[] inhoud, string naam, string soort)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./download.js");
        using var stroom = new DotNetStreamReference(new MemoryStream(inhoud));
        await module.InvokeVoidAsync("bewaar", naam, soort, stroom);
    }
}

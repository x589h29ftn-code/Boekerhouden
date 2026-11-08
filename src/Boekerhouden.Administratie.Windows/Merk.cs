namespace Boekerhouden.Administratie.Windows;

/// <summary>Het merkteken van de app (de S met de dubbele streep) voor de titelbalk en de taakbalk van elk venster.</summary>
internal static class Merk
{
    private static readonly Lazy<Icon?> Pictogram = new(() =>
    {
        using var stroom = typeof(Merk).Assembly.GetManifestResourceStream("sifer.ico");
        return stroom is null ? null : new Icon(stroom);
    });

    public static Icon? Icoon => Pictogram.Value;
}

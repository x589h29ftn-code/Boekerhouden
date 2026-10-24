namespace Boekerhouden.Data;

/// <summary>De gegevens en de huisstijl van het kantoor voor rapporten (één rij).</summary>
public sealed class Kantoorinstellingen
{
    public const int VasteId = 1;

    public int Id { get; set; } = VasteId;

    public string Naam { get; set; } = string.Empty;

    public string? Adres { get; set; }

    public string? Postcode { get; set; }

    public string? Plaats { get; set; }

    /// <summary>Huisstijlkleur voor koppen en lijnen, als #rrggbb.</summary>
    public string Kleur { get; set; } = "#1f4e79";

    public byte[]? Logo { get; set; }

    /// <summary>image/png of image/jpeg.</summary>
    public string? LogoType { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

namespace Boekerhouden.Data;

/// <summary>
/// Hoe een grootboekrekening in de specificaties van het rapport staat (per klant, rolt door naar volgende jaren).
/// Een verborgen rekening telt mee in "Overige" van zijn post, zodat de specificatie blijft aansluiten.
/// </summary>
public sealed class RekeningPresentatie
{
    public Guid GrootboekrekeningId { get; set; }

    public Guid KlantId { get; set; }

    /// <summary>Omschrijving in het rapport; null = de omschrijving uit de administratie.</summary>
    public string? Omschrijving { get; set; }

    public bool Verbergen { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>Per post (RGS-code of eigen post) of de specificatie in het rapport komt (per klant).</summary>
public sealed class PostPresentatie
{
    public Guid KlantId { get; set; }

    /// <summary>De sleutel van de post: RGS-code of "eigen:&lt;id&gt;".</summary>
    public string Sleutel { get; set; } = string.Empty;

    public bool ToonSpecificatie { get; set; } = true;

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

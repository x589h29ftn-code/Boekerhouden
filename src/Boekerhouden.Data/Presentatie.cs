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

/// <summary>
/// Per post (RGS-code) hoe hij in het rapport staat (per klant): of de specificatie erin komt, en of de post is
/// uitgeklapt. Bij een hoofdpost gaat uitklappen over de subposten in de balans of W&amp;V, bij een subpost over de
/// rekeningen in de specificatie.
/// </summary>
public sealed class PostPresentatie
{
    public Guid KlantId { get; set; }

    /// <summary>De sleutel van de post: RGS-code of "eigen:&lt;id&gt;".</summary>
    public string Sleutel { get; set; } = string.Empty;

    public bool ToonSpecificatie { get; set; } = true;

    /// <summary>Uitgeklapt of ingeklapt; null = de standaard (balans en specificaties uit-, W&amp;V ingeklapt).</summary>
    public bool? Uitgeklapt { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>Een toelichtende tekst bij een post in het rapport, per dossier (dus per jaar).</summary>
public sealed class DossierToelichting
{
    public Guid DossierId { get; set; }

    /// <summary>De post (RGS-code van de hoofdpost).</summary>
    public string Sleutel { get; set; } = string.Empty;

    public string Tekst { get; set; } = string.Empty;

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>
/// Eén regel van de uitsplitsing van een rekening in de specificatie (per dossier), bijv. "Nog te betalen kosten"
/// in accountantskosten, energie en overige. Bedragen zoals in het rapport: positief aan de normale kant van de post.
/// </summary>
public sealed class UitsplitsingRegel
{
    public Guid Id { get; set; }

    public Guid DossierId { get; set; }

    public Guid GrootboekrekeningId { get; set; }

    public int Volgorde { get; set; }

    public string Omschrijving { get; set; } = string.Empty;

    public decimal Bedrag { get; set; }

    /// <summary>Het bedrag van het vorige jaar; null = niet uitgesplitst.</summary>
    public decimal? BedragVorig { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

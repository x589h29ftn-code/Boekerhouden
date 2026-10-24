namespace Boekerhouden.Data;

/// <summary>
/// Een instelling die geldt vanaf een boekjaar: per sleutel geldt in een jaar de versie met het hoogste
/// <see cref="VanafBoekjaar"/> tot en met dat jaar. Een wijziging in een dossier geldt vanaf het jaar van dat dossier;
/// eerdere jaren blijven zoals ze waren, latere volgen tenzij ze een eigen versie hebben. 0 = alle jaren.
/// </summary>
public interface IJaarversie
{
    int VanafBoekjaar { get; set; }
}

/// <summary>
/// Hoe een grootboekrekening in de specificaties van het rapport staat (per klant, vanaf een boekjaar).
/// Een verborgen rekening telt mee in "Overige" van zijn post, zodat de specificatie blijft aansluiten.
/// </summary>
public sealed class RekeningPresentatie : IJaarversie
{
    public Guid GrootboekrekeningId { get; set; }

    /// <inheritdoc />
    public int VanafBoekjaar { get; set; }

    public Guid KlantId { get; set; }

    /// <summary>Omschrijving in het rapport; null = de omschrijving uit de administratie.</summary>
    public string? Omschrijving { get; set; }

    public bool Verbergen { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>
/// Per post (RGS-code) hoe hij in het rapport staat (per klant, vanaf een boekjaar): of de specificatie erin komt, en
/// of de post is uitgeklapt. Bij een hoofdpost gaat uitklappen over de subposten in de balans of W&amp;V, bij een
/// subpost over de rekeningen in de specificatie.
/// </summary>
public sealed class PostPresentatie : IJaarversie
{
    public Guid KlantId { get; set; }

    /// <summary>De sleutel van de post: RGS-code of "eigen:&lt;id&gt;".</summary>
    public string Sleutel { get; set; } = string.Empty;

    /// <inheritdoc />
    public int VanafBoekjaar { get; set; }

    public bool ToonSpecificatie { get; set; } = true;

    /// <summary>Uitgeklapt of ingeklapt; null = de standaard (balans en specificaties uit-, W&amp;V ingeklapt).</summary>
    public bool? Uitgeklapt { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>
/// Een toelichtende tekst bij een post in het rapport, per dossier. Hij rolt door naar volgende jaren tot een later
/// dossier een eigen tekst heeft; een lege tekst betekent: vanaf dit jaar geen toelichting.
/// </summary>
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

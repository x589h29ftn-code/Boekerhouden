namespace Boekerhouden.Data;

public enum CorrectieSoort
{
    /// <summary>Een correctie op de cijfers van de klant (bijv. een vergeten afschrijving of een overlopende post).</summary>
    Correctie,

    /// <summary>Alleen een andere indeling (bijv. een saldo naar een andere rekening); de klant hoeft niets te boeken.</summary>
    Herrubricering,
}

/// <summary>
/// Een correctieboeking (memoriaal) in een dossier. De regels zijn altijd in evenwicht (controle C08).
/// </summary>
public sealed class Correctieboeking
{
    public Guid Id { get; set; }

    public Guid DossierId { get; set; }

    /// <summary>Volgnummer binnen het dossier: 1, 2, 3, …</summary>
    public int Nummer { get; set; }

    public string Omschrijving { get; set; } = string.Empty;

    public CorrectieSoort Soort { get; set; }

    /// <summary>Komt elk jaar terug; in het volgende dossier over te nemen.</summary>
    public bool Terugkerend { get; set; }

    /// <summary>De klant heeft de boeking ook in de eigen administratie verwerkt.</summary>
    public bool VerwerktBijKlant { get; set; }

    /// <summary>De terugkerende boeking uit het vorige dossier waarvan deze is overgenomen.</summary>
    public Guid? OvergenomenVanId { get; set; }

    public DateTimeOffset AangemaaktOp { get; set; }

    public string AangemaaktDoor { get; set; } = string.Empty;

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;

    public List<Correctieregel> Regels { get; set; } = [];
}

/// <summary>Eén regel van een correctieboeking. Debet positief, credit negatief.</summary>
public sealed class Correctieregel
{
    public Guid Id { get; set; }

    public Guid CorrectieboekingId { get; set; }

    public int Volgorde { get; set; }

    public Guid GrootboekrekeningId { get; set; }

    public Grootboekrekening? Grootboekrekening { get; set; }

    public decimal Bedrag { get; set; }

    public string? Omschrijving { get; set; }
}

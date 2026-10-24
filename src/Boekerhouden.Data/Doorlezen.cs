namespace Boekerhouden.Data;

public enum LeesAfhandeling
{
    /// <summary>Bekeken en in orde; de toelichting zegt waarom.</summary>
    InOrde,

    /// <summary>Gaat mee naar de bespreking met de klant (een <see cref="Bespreekpunt"/>).</summary>
    Bespreekpunt,
}

/// <summary>De afhandeling van een signaal bij het doorlezen. De signalen zelf rekent het systeem steeds opnieuw uit.</summary>
public sealed class DossierLeespunt
{
    public Guid DossierId { get; set; }

    /// <summary>De sleutel van het signaal, bijv. "verschil:BVor".</summary>
    public string Sleutel { get; set; } = string.Empty;

    /// <summary>De titel van het signaal toen het werd afgehandeld (voor het dossier, ook als het signaal later vervalt).</summary>
    public string Titel { get; set; } = string.Empty;

    public LeesAfhandeling Afhandeling { get; set; }

    public string? Toelichting { get; set; }

    public string Door { get; set; } = string.Empty;

    public DateTimeOffset Op { get; set; }
}

/// <summary>
/// Een punt om met de klant te bespreken: uit het doorlezen, een reviewpunt of zelf toegevoegd. Na de bespreking staat
/// de uitkomst erbij (het bespreekverslag).
/// </summary>
public sealed class Bespreekpunt
{
    public Guid Id { get; set; }

    public Guid DossierId { get; set; }

    public string Onderwerp { get; set; } = string.Empty;

    public string? Toelichting { get; set; }

    /// <summary>De sleutel van het leessignaal waar het punt uit voortkomt; null als het zelf is toegevoegd.</summary>
    public string? Signaal { get; set; }

    /// <summary>Een significante aangelegenheid (Standaard 4410): komt in het verslag naar het bestuur.</summary>
    public bool Significant { get; set; }

    /// <summary>Wat er is besproken of afgesproken; null zolang het punt nog niet is besproken.</summary>
    public string? Uitkomst { get; set; }

    public int Volgorde { get; set; }

    public string AangemaaktDoor { get; set; } = string.Empty;

    public DateTimeOffset AangemaaktOp { get; set; }

    public string? GewijzigdDoor { get; set; }

    public DateTimeOffset? GewijzigdOp { get; set; }
}

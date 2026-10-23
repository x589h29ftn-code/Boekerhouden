namespace Boekerhouden.Data;

public enum Fase
{
    Voorbereiding,
    Uitvoering,
    Afronding,
}

public enum OnderdeelSoort
{
    /// <summary>Een onderdeel met stappen, bijv. "Opdracht en acceptatie".</summary>
    Algemeen,

    /// <summary>De planning: per hoofdpost kiezen of die materieel is (docs §10.2).</summary>
    MaterielePosten,

    /// <summary>De uitvoering van één materiële hoofdpost (RGS niveau 2): cijfers, notitie en stappen.</summary>
    Post,
}

public enum StapStatus
{
    Open,
    Gereed,
    NietVanToepassing,
}

/// <summary>Een controle die het systeem doet voordat een stap gereed mag.</summary>
public enum StapControle
{
    Geen,

    /// <summary>Elke hoofdpost met saldo heeft een keuze materieel / niet materieel.</summary>
    MaterielePostenGekozen,

    /// <summary>Alle grootboekrekeningen met saldo zijn aan RGS gekoppeld.</summary>
    AllesGekoppeld,
}

/// <summary>
/// Het werkprogramma van het kantoor voor één soort opdracht. Een dossier krijgt bij de start een kopie; latere
/// wijzigingen in het sjabloon veranderen een lopend dossier niet.
/// </summary>
public sealed class WerkprogrammaSjabloon
{
    public Guid Id { get; set; }

    public SoortOpdracht SoortOpdracht { get; set; }

    public string Naam { get; set; } = string.Empty;

    /// <summary>Versie zoals het kantoor die noemt, bijv. "2026.1"; gaat mee naar het dossier.</summary>
    public string Versie { get; set; } = string.Empty;

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;

    public List<SjabloonOnderdeel> Onderdelen { get; set; } = [];
}

public sealed class SjabloonOnderdeel
{
    public Guid Id { get; set; }

    public Guid SjabloonId { get; set; }

    public Fase Fase { get; set; }

    public OnderdeelSoort Soort { get; set; }

    public string Titel { get; set; } = string.Empty;

    /// <summary>Bij een post: de RGS-hoofdpost (niveau 2), bijv. BMva.</summary>
    public string? HoofdpostCode { get; set; }

    public bool Verplicht { get; set; }

    public int Volgorde { get; set; }

    public List<SjabloonStap> Stappen { get; set; } = [];
}

public sealed class SjabloonStap
{
    public Guid Id { get; set; }

    public Guid OnderdeelId { get; set; }

    public string Titel { get; set; } = string.Empty;

    public string? Instructie { get; set; }

    /// <summary>Kan niet worden verwijderd; alleen op "niet van toepassing" met een reden.</summary>
    public bool Verplicht { get; set; }

    public StapControle Controle { get; set; }

    public int Volgorde { get; set; }
}

/// <summary>Een onderdeel van het werkprogramma in een dossier (kopie uit het sjabloon, of een materiële post).</summary>
public sealed class DossierOnderdeel
{
    public Guid Id { get; set; }

    public Guid DossierId { get; set; }

    public Guid? BronOnderdeelId { get; set; }

    public Fase Fase { get; set; }

    public OnderdeelSoort Soort { get; set; }

    public string Titel { get; set; } = string.Empty;

    public string? HoofdpostCode { get; set; }

    public bool Verplicht { get; set; }

    public int Volgorde { get; set; }

    /// <summary>Vrije notitie: cijferbeoordeling bij een post, toelichting op de keuze bij de planning.</summary>
    public string? Notitie { get; set; }

    public string? GereviewdDoor { get; set; }

    public DateTimeOffset? GereviewdOp { get; set; }

    public List<DossierStap> Stappen { get; set; } = [];
}

public sealed class DossierStap
{
    public Guid Id { get; set; }

    public Guid OnderdeelId { get; set; }

    /// <summary>De sjabloonstap waarvan dit een kopie is; null = eigen stap, in het dossier toegevoegd.</summary>
    public Guid? BronStapId { get; set; }

    public string Titel { get; set; } = string.Empty;

    public string? Instructie { get; set; }

    public bool Verplicht { get; set; }

    public StapControle Controle { get; set; }

    public int Volgorde { get; set; }

    public StapStatus Status { get; set; }

    public string? Antwoord { get; set; }

    /// <summary>Reden bij "niet van toepassing" of bij verwijderen.</summary>
    public string? Reden { get; set; }

    public string? UitgevoerdDoor { get; set; }

    public DateTimeOffset? UitgevoerdOp { get; set; }

    public bool Verwijderd { get; set; }

    public string? VerwijderdDoor { get; set; }

    public DateTimeOffset? VerwijderdOp { get; set; }

    public DateTimeOffset AangemaaktOp { get; set; }

    public string AangemaaktDoor { get; set; } = string.Empty;
}

/// <summary>De keuze per hoofdpost: materieel of (bewust) niet materieel. Geen rij = nog niet gekozen.</summary>
public sealed class DossierPostKeuze
{
    public Guid DossierId { get; set; }

    public string HoofdpostCode { get; set; } = string.Empty;

    public bool Materieel { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

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

/// <summary>
/// De beoordeling van een hoofdpost bij de planning (risicogericht samenstellen): standaard niet van toepassing; een
/// materiële post of een risicopost krijgt het postprogramma in de uitvoering.
/// </summary>
public enum PostBeoordeling
{
    NietVanToepassing,
    Materieel,
    Risicopost,
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

    /// <summary>De beoordeling van de posten (niet van toepassing, materieel of risicopost); standaard n.v.t., dus altijd in orde.</summary>
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

    /// <summary>Het boekjaar waaruit het antwoord is overgenomen (doorrollen); null = in dit dossier ingevuld.</summary>
    public int? OvergenomenVan { get; set; }
}

/// <summary>De beoordeling per hoofdpost. Geen rij = niet van toepassing (de standaard).</summary>
public sealed class DossierPostKeuze
{
    public Guid DossierId { get; set; }

    public string HoofdpostCode { get; set; } = string.Empty;

    public PostBeoordeling Beoordeling { get; set; }

    /// <summary>Bij een risicopost: wat er mis kan gaan, bijv. "onjuiste waardering voorraad".</summary>
    public string? Risico { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>Een reviewpunt bij een onderdeel (eventueel bij één stap). Een onderdeel met open punten kan niet gereviewd.</summary>
public sealed class Reviewpunt
{
    public Guid Id { get; set; }

    public Guid DossierId { get; set; }

    public Guid OnderdeelId { get; set; }

    public Guid? StapId { get; set; }

    public string Tekst { get; set; } = string.Empty;

    public string Door { get; set; } = string.Empty;

    public DateTimeOffset Op { get; set; }

    public bool Opgelost { get; set; }

    /// <summary>De reactie van wie het punt oplost.</summary>
    public string? Reactie { get; set; }

    public string? OpgelostDoor { get; set; }

    public DateTimeOffset? OpgelostOp { get; set; }
}

/// <summary>Een bijlage bij een stap in het dossier. De inhoud staat apart, zodat lijsten die niet hoeven te laden.</summary>
public sealed class Bijlage
{
    public Guid Id { get; set; }

    public Guid DossierId { get; set; }

    public Guid StapId { get; set; }

    public string Bestandsnaam { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Grootte { get; set; }

    /// <summary>SHA-256 van de inhoud, als bewijs van wat er is opgeslagen.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string Door { get; set; } = string.Empty;

    public DateTimeOffset Op { get; set; }

    public BijlageInhoud? Inhoud { get; set; }
}

public sealed class BijlageInhoud
{
    public Guid BijlageId { get; set; }

    public byte[] Data { get; set; } = [];
}

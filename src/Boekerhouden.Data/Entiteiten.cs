namespace Boekerhouden.Data;

public enum Rechtsvorm
{
    BV,
    Eenmanszaak,
}

public enum SoortOpdracht
{
    SamenstellenJaarrekening,
    WinstaangifteOpstellen,
}

public enum DossierStatus
{
    Nieuw,
    InUitvoering,
}

public sealed class Klant
{
    public Guid Id { get; set; }

    /// <summary>Het klantnummer van het kantoor; uniek.</summary>
    public string Klantnummer { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;

    public Rechtsvorm Rechtsvorm { get; set; }

    public string? KvkNummer { get; set; }

    public string? Plaats { get; set; }

    public string? Straat { get; set; }

    public string? Huisnummer { get; set; }

    public string? Toevoeging { get; set; }

    public string? Postcode { get; set; }

    public string? Telefoon { get; set; }

    public string? Email { get; set; }

    public string? Website { get; set; }

    /// <summary>Rechtspersonen en Samenwerkingsverbanden Informatienummer (9 cijfers), o.a. voor de aangifte Vpb.</summary>
    public string? Rsin { get; set; }

    /// <summary>De hoofdactiviteit volgens de Standaard Bedrijfsindeling, bijv. 47.72 of 4772.</summary>
    public string? SbiCode { get; set; }

    /// <summary>De naam volgens de statuten, als die afwijkt van de handelsnaam.</summary>
    public string? StatutaireNaam { get; set; }

    /// <summary>De statutaire zetel, als die afwijkt van de vestigingsplaats.</summary>
    public string? StatutaireZetel { get; set; }

    public DateOnly? Oprichtingsdatum { get; set; }

    public DateTimeOffset AangemaaktOp { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;

    public List<Dossier> Dossiers { get; set; } = [];
}

/// <summary>Eén opdracht voor één boekjaar van een klant.</summary>
public sealed class Dossier
{
    public Guid Id { get; set; }

    public Guid KlantId { get; set; }

    public Klant? Klant { get; set; }

    public int Boekjaar { get; set; }

    public SoortOpdracht SoortOpdracht { get; set; }

    public DossierStatus Status { get; set; }

    public string Naam { get; set; } = string.Empty;

    /// <summary>De versie van het werkprogrammasjabloon waarmee het dossier is gestart; null = nog niet gestart.</summary>
    public string? WerkprogrammaVersie { get; set; }

    /// <summary>Commerciële of fiscale grondslagen (jaarrekening van een BV); null = nog niet gekozen.</summary>
    public Verslaggeving? Grondslag { get; set; }

    public DateTimeOffset AangemaaktOp { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>Een grootboekrekening van een klant. Het nummer is uniek per klant en blijft over de jaren gelijk.</summary>
public sealed class Grootboekrekening
{
    public Guid Id { get; set; }

    public Guid KlantId { get; set; }

    public string Nummer { get; set; } = string.Empty;

    public string Omschrijving { get; set; } = string.Empty;

    public string? RgsCode { get; set; }

    /// <summary>In het dossier aangemaakt voor een correctie; staat (nog) niet in de administratie van de klant.</summary>
    public bool AangemaaktBijCorrectie { get; set; }
}

/// <summary>
/// Eén ingelezen saldibalans van een klant voor een boekjaar. Per klant en boekjaar is er één actieve batch;
/// eerdere imports blijven bewaard voor de historie.
/// </summary>
public sealed class ImportBatch
{
    public Guid Id { get; set; }

    public Guid KlantId { get; set; }

    public int Boekjaar { get; set; }

    public bool Actief { get; set; }

    public string Bestandsnaam { get; set; } = string.Empty;

    /// <summary>SHA-256 van het bestand, als bewijs van wat er is ingelezen.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public int AantalRegels { get; set; }

    public decimal TotaalDebet { get; set; }

    public decimal TotaalCredit { get; set; }

    public DateTimeOffset GeimporteerdOp { get; set; }

    public string GeimporteerdDoor { get; set; } = string.Empty;

    public List<Saldo> Saldi { get; set; } = [];
}

/// <summary>Het saldo van één grootboekrekening in een importbatch. Debet positief, credit negatief.</summary>
public sealed class Saldo
{
    public Guid Id { get; set; }

    public Guid ImportBatchId { get; set; }

    public Guid GrootboekrekeningId { get; set; }

    public Grootboekrekening? Grootboekrekening { get; set; }

    public decimal Bedrag { get; set; }
}

/// <summary>Regel in het wijzigingslog (alleen toevoegen, nooit wijzigen).</summary>
public sealed class AuditRegel
{
    public long Id { get; set; }

    public DateTimeOffset Tijdstip { get; set; }

    public Guid? GebruikerId { get; set; }

    public string Gebruiker { get; set; } = string.Empty;

    public string Actie { get; set; } = string.Empty;

    public string? Onderwerp { get; set; }

    public string? Details { get; set; }
}

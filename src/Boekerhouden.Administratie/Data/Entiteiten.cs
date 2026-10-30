using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.AspNetCore.Identity;

namespace Boekerhouden.Administratie.Data;

public sealed class Gebruiker : IdentityUser<Guid>
{
    public string Naam { get; set; } = string.Empty;

    public string Initialen { get; set; } = string.Empty;

    public bool Actief { get; set; } = true;

    /// <summary>Na aanmaken of resetten door de beheerder moet de gebruiker eerst een eigen wachtwoord kiezen.</summary>
    public bool MoetWachtwoordWijzigen { get; set; }
}

/// <summary>De rollen in het pakket zelf; de rechten in een administratie staan in <see cref="Lidmaatschap"/>.</summary>
public static class Rollen
{
    /// <summary>Gebruikers en leden beheren, het wijzigingslog inzien. Geeft geen inzage in de cijfers.</summary>
    public const string Beheerder = "Beheerder";

    /// <summary>Medewerker van het kantoor: mag administraties aanmaken (en wordt daar lid met de rol Kantoor).</summary>
    public const string Kantoor = "Kantoor";

    public static readonly IReadOnlyList<string> Alle = [Beheerder, Kantoor];
}

/// <summary>De rol van een gebruiker in één administratie.</summary>
public enum Lidmaatschapsrol
{
    /// <summary>Alleen overzichten en documenten bekijken.</summary>
    Inzien,

    /// <summary>Documenten inlezen, factureren, de bank bijwerken.</summary>
    Ondernemer,

    /// <summary>Alles, ook memoriaal, rekeningschema en periodes.</summary>
    Kantoor,
}

/// <summary>De administratie van één onderneming.</summary>
public sealed class Administratie
{
    public Guid Id { get; set; }

    public string Naam { get; set; } = string.Empty;

    public Ondernemingsvorm Ondernemingsvorm { get; set; }

    public string? KvkNummer { get; set; }

    public string? BtwNummer { get; set; }

    /// <summary>De eigen bankrekening(en); staan ook op facturen, maar zijn niet van de leverancier.</summary>
    public List<string> Ibans { get; set; } = [];

    // Instellingen waarin de administratie afwijkt van het kantoor; leeg = de kantoorinstelling.

    /// <summary>Betalingstermijn in dagen als de factuur geen vervaldatum noemt.</summary>
    public int? Betalingstermijn { get; set; }

    /// <summary>Heeft de administratie een eigen limiet (ook "geen limiet")? Anders geldt die van het kantoor.</summary>
    public bool LimietAfwijkend { get; set; }

    /// <summary>Boven dit bedrag (inclusief btw) wordt nooit automatisch geboekt; leeg = geen limiet (besluit B2).</summary>
    public decimal? LimietAutomatisch { get; set; }

    /// <summary>Na hoeveel gelijke bevestigingen op rij een leverancier automatisch wordt verwerkt.</summary>
    public int? DrempelAutomatisch { get; set; }

    public decimal? Bandbreedte { get; set; }

    public bool? Adrescontrole { get; set; }

    /// <summary>Waar een kopie van elk geboekt document komt (met variabelen).</summary>
    public string? Documentmap { get; set; }

    public Boekhouding.Regels.AfwijkendeInstellingen Afwijkend() =>
        new(DrempelAutomatisch, Bandbreedte, LimietAfwijkend, LimietAutomatisch, Betalingstermijn, Adrescontrole, Documentmap);

    public DateTimeOffset AangemaaktOp { get; set; }

    public string AangemaaktDoor { get; set; } = string.Empty;
}

public sealed class Lidmaatschap
{
    public Guid AdministratieId { get; set; }

    public Guid GebruikerId { get; set; }

    public Lidmaatschapsrol Rol { get; set; }
}

/// <summary>Gegevens die bij één administratie horen; de database geeft alleen die van de geopende administratie.</summary>
public interface IVanAdministratie
{
    Guid AdministratieId { get; }
}

public sealed class Grootboekrekening : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public string Nummer { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;

    public Rekeningsoort Soort { get; set; }

    public string? RgsCode { get; set; }

    public Rekeningfunctie Functie { get; set; }

    public Rekening NaarDomein() => new(Nummer, Naam, Soort, RgsCode, Functie);
}

public sealed class Dagboekdefinitie : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;

    public Dagboeksoort Soort { get; set; }

    public string? Rekeningnummer { get; set; }

    /// <summary>Bij een bankdagboek: het IBAN van de rekening; afschriften van dat IBAN komen in dit dagboek.</summary>
    public string? Iban { get; set; }

    public Dagboek NaarDomein() => new(Code, Naam, Soort, Rekeningnummer);
}

/// <summary>
/// Een geboekte journaalpost. Wordt nooit gewijzigd of verwijderd (de database weigert dat); corrigeren gaat met een
/// tegenboeking. Volgnummer en hash vormen de keten (<see cref="Journaalketen"/>).
/// </summary>
public sealed class Boeking : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public long Volgnummer { get; set; }

    public string Dagboek { get; set; } = string.Empty;

    public string Boekstuk { get; set; } = string.Empty;

    public DateOnly Datum { get; set; }

    public string Omschrijving { get; set; } = string.Empty;

    public DateTimeOffset GeboektOp { get; set; }

    public Guid? GeboektDoorId { get; set; }

    public string GeboektDoor { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public string VorigeHash { get; set; } = string.Empty;

    public List<Boekingsregel> Regels { get; set; } = [];

    public Journaalpost NaarDomein() => new(Dagboek, Boekstuk, Datum, Omschrijving,
        [.. Regels.OrderBy(r => r.Regelnummer).Select(r => new Journaalregel(r.Rekeningnummer, r.Bedrag, r.Omschrijving, r.BtwCode, r.Relatie, r.Factuurnummer))]);
}

public sealed class Boekingsregel : IVanAdministratie
{
    public long Id { get; set; }

    public Guid BoekingId { get; set; }

    public Guid AdministratieId { get; set; }

    public int Regelnummer { get; set; }

    public string Rekeningnummer { get; set; } = string.Empty;

    public decimal Bedrag { get; set; }

    public string? Omschrijving { get; set; }

    public string? BtwCode { get; set; }

    public string? Relatie { get; set; }

    public string? Factuurnummer { get; set; }
}

/// <summary>Eén regel in het wijzigingslog; wordt nooit gewijzigd of verwijderd (de database weigert dat).</summary>
public sealed class AuditRegel
{
    public long Id { get; set; }

    public DateTimeOffset Tijdstip { get; set; }

    public Guid? GebruikerId { get; set; }

    public string Gebruiker { get; set; } = string.Empty;

    /// <summary>De administratie waarin het gebeurde, of leeg (inloggen, gebruikersbeheer).</summary>
    public Guid? AdministratieId { get; set; }

    public string Actie { get; set; } = string.Empty;

    public string? Onderwerp { get; set; }

    public string? Details { get; set; }
}

/// <summary>Een leverancier of klant van de administratie.</summary>
public sealed class Handelsrelatie : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;

    public List<string> Ibans { get; set; } = [];

    public string? BtwNummer { get; set; }

    public string? KvkNummer { get; set; }

    /// <summary>De standaard grootboekrekening (bijv. telefoonkosten).</summary>
    public string? Tegenrekening { get; set; }

    public DateTimeOffset AangemaaktOp { get; set; }

    public Boekhouding.Relaties.Relatie NaarDomein() => new(Code, Naam, [.. Ibans], BtwNummer, KvkNummer, Tegenrekening);
}

public enum Documentstatus
{
    /// <summary>Ingelezen; wacht op de gebruiker (zie de melding waarom).</summary>
    TeVerwerken,

    Geboekt,

    /// <summary>De virusscan vond iets: niet geopend, niet verwerkt, inhoud niet bewaard.</summary>
    Quarantaine,

    /// <summary>Bewaard zonder boeking: privé of geen boeking nodig (zie <see cref="Document.Soort"/>).</summary>
    Gearchiveerd,
}

/// <summary>Wat voor document het is; bepaalt hoe het verwerkt wordt.</summary>
public enum Documentsoort
{
    Factuur,

    /// <summary>Een creditnota: de bedragen worden omgekeerd geboekt, eventueel verrekend met een openstaande factuur.</summary>
    Creditnota,

    /// <summary>Privé: niet van de onderneming, dus niet boeken.</summary>
    Prive,

    /// <summary>Geen boeking nodig (bijv. een pakbon of offerte): alleen bewaren.</summary>
    Overig,
}

/// <summary>Een ingelezen document (inkoopfactuur, later ook bon of afschrift). De inhoud staat apart.</summary>
public sealed class Document : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public string Bestandsnaam { get; set; } = string.Empty;

    public string Inhoudstype { get; set; } = string.Empty;

    public long Grootte { get; set; }

    /// <summary>SHA-256 van de inhoud: hetzelfde bestand twee keer inlezen wordt herkend.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public Documentstatus Status { get; set; }

    public Documentsoort Soort { get; set; }

    /// <summary>Wat er herkend is, als JSON (<c>HerkendeFactuur</c>); leeg als er niets herkend is.</summary>
    public string? Herkend { get; set; }

    /// <summary>Waarom het document in Te verwerken staat, of wat de virusscan vond.</summary>
    public string? Melding { get; set; }

    public DateTimeOffset IngelezenOp { get; set; }

    public string IngelezenDoor { get; set; } = string.Empty;

    public Guid? BoekingId { get; set; }

    /// <summary>De bankmutatie waarbij de gebruiker deze factuur uploadde; na het boeken worden ze aan elkaar gekoppeld.</summary>
    public Guid? BanktransactieId { get; set; }

    /// <summary>
    /// De periodeverdeling als de factuur transitorisch geboekt is, als JSON (periode, verdeelrekening, de
    /// kostenrekeningen per regel en de boekstukken per maand); leeg zonder verdeling.
    /// </summary>
    public string? Verdeling { get; set; }
}

public sealed class DocumentInhoud : IVanAdministratie
{
    public Guid DocumentId { get; set; }

    public Guid AdministratieId { get; set; }

    public byte[] Inhoud { get; set; } = [];

    /// <summary>
    /// Bij een scan of foto: de woorden die de OCR vond, met hun plek in PDF-punten (JSON). Leeg bij een PDF met tekst;
    /// die tekst komt uit de PDF zelf.
    /// </summary>
    public string? Tekstlaag { get; set; }
}

/// <summary>
/// Een factuur die (nog) niet (helemaal) betaald is. Bedragen volgen het grootboek: debiteur positief, crediteur
/// negatief. Het openstaande bedrag verandert bij afletteren; de boeking zelf nooit.
/// </summary>
public sealed class OpenstaandePost : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public Boekhouding.Facturen.Factuursoort Soort { get; set; }

    public string Relatie { get; set; } = string.Empty;

    public string Factuurnummer { get; set; } = string.Empty;

    public DateOnly Datum { get; set; }

    public DateOnly Vervaldatum { get; set; }

    public decimal Bedrag { get; set; }

    public decimal Openstaand { get; set; }

    public string? Betalingskenmerk { get; set; }

    public Guid BoekingId { get; set; }

    public Guid? DocumentId { get; set; }

    public Boekhouding.Facturen.OpenPost NaarDomein() =>
        new(Id, Soort, Relatie, Factuurnummer, Datum, Vervaldatum, Bedrag, Openstaand, Betalingskenmerk);
}

/// <summary>Een keuze van de gebruiker (rekening en btw-code) waar het leergeheugen van leert.</summary>
public sealed class Leerkeuze : IVanAdministratie
{
    public long Id { get; set; }

    public Guid AdministratieId { get; set; }

    /// <summary>De sleutel van het leergeheugen, bijv. <c>F|relatie|BELNET</c>.</summary>
    public string Sleutel { get; set; } = string.Empty;

    public string Rekeningnummer { get; set; } = string.Empty;

    public string? BtwCode { get; set; }

    /// <summary>Het totaal van de bevestigde factuur, voor de bandbreedte van automatisch verwerken.</summary>
    public decimal? Bedrag { get; set; }

    public string? Iban { get; set; }

    public List<string>? Postcodes { get; set; }

    /// <summary>Het sjabloon voor de omschrijving (met variabelen), als de gebruiker dat gebruikte.</summary>
    public string? Omschrijving { get; set; }

    public DateTimeOffset Tijdstip { get; set; }

    public Boekhouding.Regels.Bevestiging NaarDomein() =>
        new(new(Rekeningnummer, BtwCode), Bedrag, Iban, Postcodes, Omschrijving);
}

/// <summary>Een regel van de gebruiker voor automatisch verwerken (zie <c>Verwerkingsregel</c> in het domein).</summary>
public sealed class Automatiseringsregel : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public int Volgorde { get; set; }

    public string Naam { get; set; } = string.Empty;

    public string Rekeningnummer { get; set; } = string.Empty;

    public string? BtwCode { get; set; }

    public bool Automatisch { get; set; }

    public Boekhouding.Regels.Verwerkingssoort? Soort { get; set; }

    public string? Relatie { get; set; }

    public string? NaamBevat { get; set; }

    public decimal? BedragVan { get; set; }

    public decimal? BedragTot { get; set; }

    public string? OmschrijvingBevat { get; set; }

    public string? TegenIban { get; set; }

    public Boekhouding.Regels.Regelrichting Richting { get; set; }

    /// <summary>Het sjabloon voor de omschrijving van de boeking, met variabelen zoals <c>#factuurnummer</c>.</summary>
    public string? Omschrijving { get; set; }

    public Boekhouding.Regels.Verwerkingsregel NaarDomein() =>
        new(Naam, Rekeningnummer, BtwCode, Automatisch, Soort, Relatie, NaamBevat, BedragVan, BedragTot, OmschrijvingBevat, TegenIban, Richting, Omschrijving);
}

/// <summary>De instellingen van het kantoor: de standaard voor elke administratie (één rij).</summary>
public sealed class Kantoorinstellingen
{
    public const int EnigeId = 1;

    public int Id { get; set; } = EnigeId;

    public int DrempelAutomatisch { get; set; } = 2;

    public decimal Bandbreedte { get; set; } = Boekhouding.Regels.Leergeheugen.StandaardBandbreedte;

    public decimal? LimietAutomatisch { get; set; }

    public int Betalingstermijn { get; set; } = 30;

    public bool Adrescontrole { get; set; } = true;

    public string? Documentmap { get; set; }

    public Boekhouding.Regels.Verwerkingsinstellingen NaarDomein() =>
        new(DrempelAutomatisch, Bandbreedte, LimietAutomatisch, Betalingstermijn, Adrescontrole, Documentmap);
}

/// <summary>Een aangewezen plek op de factuur van een leverancier (zoektemplate), per veld de laatste.</summary>
public sealed class Zoekplek : IVanAdministratie
{
    public long Id { get; set; }

    public Guid AdministratieId { get; set; }

    /// <summary>De code van de relatie.</summary>
    public string Relatie { get; set; } = string.Empty;

    public Boekhouding.Herkenning.Factuurveld Veld { get; set; }

    public int Pagina { get; set; }

    public double X0 { get; set; }

    public double Y0 { get; set; }

    public double X1 { get; set; }

    public double Y1 { get; set; }

    public bool Voorrang { get; set; }

    public DateTimeOffset Tijdstip { get; set; }

    public Boekhouding.Herkenning.Zoekgebied NaarDomein() => new(Veld, Pagina, X0, Y0, X1, Y1, Voorrang);
}


/// <summary>Een ingelezen bankafschrift: de saldi, om het aansluiten op het volgende afschrift te controleren.</summary>
public sealed class Afschrift : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public string Dagboek { get; set; } = string.Empty;

    public string Iban { get; set; } = string.Empty;

    public string? Nummer { get; set; }

    public DateOnly? Begindatum { get; set; }

    public decimal Beginsaldo { get; set; }

    public DateOnly? Einddatum { get; set; }

    public decimal Eindsaldo { get; set; }

    public string Bestandsnaam { get; set; } = string.Empty;

    /// <summary>Bijv. dat het beginsaldo niet aansluit op het vorige afschrift.</summary>
    public string? Melding { get; set; }

    public DateTimeOffset IngelezenOp { get; set; }

    public string IngelezenDoor { get; set; } = string.Empty;
}

public enum Banktransactiestatus
{
    TeVerwerken,
    Geboekt,
}

/// <summary>Een mutatie van een bankafschrift. Wordt één keer geboekt; de sleutel voorkomt dat hij twee keer wordt ingelezen.</summary>
public sealed class Banktransactie : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public Guid AfschriftId { get; set; }

    /// <summary>De plaats op het afschrift.</summary>
    public int Volgorde { get; set; }

    public string Dagboek { get; set; } = string.Empty;

    public string Iban { get; set; } = string.Empty;

    public DateOnly Datum { get; set; }

    /// <summary>Ontvangst positief, betaling negatief.</summary>
    public decimal Bedrag { get; set; }

    public string Omschrijving { get; set; } = string.Empty;

    public string? TegenIban { get; set; }

    public string? TegenNaam { get; set; }

    public string? Betalingskenmerk { get; set; }

    public string? EndToEndId { get; set; }

    /// <summary>SHA-256 van <see cref="Boekhouding.Bank.Bankmutatie.Sleutel"/>: dezelfde mutatie nog eens inlezen wordt herkend.</summary>
    public string Sleutel { get; set; } = string.Empty;

    public Banktransactiestatus Status { get; set; }

    /// <summary>Waarom de mutatie nog te verwerken is, of hoe hij geboekt is.</summary>
    public string? Melding { get; set; }

    public Guid? BoekingId { get; set; }

    public Boekhouding.Bank.Bankmutatie NaarDomein() => new(Datum, Bedrag, Omschrijving, TegenIban, TegenNaam, Betalingskenmerk, EndToEndId);
}

/// <summary>Een btw-code van het kantoor: de soort bepaalt de rubrieken in de aangifte, het tarief het percentage.</summary>
public sealed class Btwcodedefinitie
{
    public string Code { get; set; } = string.Empty;

    public string Omschrijving { get; set; } = string.Empty;

    public Boekhouding.Btw.Btwsoort Soort { get; set; }

    public Boekhouding.Btw.Tarief Tarief { get; set; }

    public bool Actief { get; set; } = true;

    public int Volgorde { get; set; }

    public Boekhouding.Btw.BtwCode NaarDomein() => new(Code, Omschrijving, Soort, Tarief, Actief: Actief);
}

/// <summary>Een btw-percentage vanaf een datum (bijv. hoog 21% vanaf 1-10-2012).</summary>
public sealed class Btwtariefrecord
{
    public int Id { get; set; }

    public Boekhouding.Btw.Tarief Tarief { get; set; }

    public DateOnly Vanaf { get; set; }

    public decimal Percentage { get; set; }

    public Boekhouding.Btw.Btwtariefregel NaarDomein() => new(Tarief, Vanaf, Percentage);
}

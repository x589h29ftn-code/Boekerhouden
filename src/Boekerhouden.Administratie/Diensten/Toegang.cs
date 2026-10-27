using Boekerhouden.Administratie.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat iemand in een administratie wil doen.</summary>
public enum Recht
{
    /// <summary>Overzichten en documenten bekijken.</summary>
    Lezen,

    /// <summary>Inkoop, verkoop en bank verwerken.</summary>
    Invoeren,

    /// <summary>Memoriaal, rekeningschema, periodes: alleen het kantoor.</summary>
    Boekhouden,
}

/// <summary>Geen lid van de administratie, of niet het recht. Bewust zonder onderscheid met "bestaat niet".</summary>
public sealed class GeenToegangException : Exception
{
    public GeenToegangException()
        : base("Deze administratie bestaat niet of je hebt er geen toegang toe.")
    {
    }

    public GeenToegangException(string melding)
        : base(melding)
    {
    }

    public GeenToegangException(string melding, Exception binnen)
        : base(melding, binnen)
    {
    }
}

/// <summary>
/// De enige weg naar de gegevens van een administratie: controleert het lidmaatschap en het recht, en geeft een
/// databasecontext die alleen die administratie laat zien (en alleen daarin kan opslaan).
/// </summary>
public sealed class Toegang(IDbContextFactory<AdministratieDbContext> dbFactory)
{
    public static bool Mag(Lidmaatschapsrol rol, Recht recht) => recht switch
    {
        Recht.Lezen => true,
        Recht.Invoeren => rol is Lidmaatschapsrol.Ondernemer or Lidmaatschapsrol.Kantoor,
        _ => rol == Lidmaatschapsrol.Kantoor,
    };

    /// <summary>De rol van de gebruiker in de administratie, of leeg als hij er geen lid van is (of niet meer actief).</summary>
    public async Task<Lidmaatschapsrol?> Rol(Uitvoerder wie, Guid administratie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (wie.Id is not { } gebruiker)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        return await (from l in db.Lidmaatschappen
                      join g in db.Users on l.GebruikerId equals g.Id
                      where l.AdministratieId == administratie && l.GebruikerId == gebruiker && g.Actief
                      select (Lidmaatschapsrol?)l.Rol).SingleOrDefaultAsync();
    }

    /// <summary>Opent de administratie; een <see cref="GeenToegangException"/> zonder lidmaatschap of recht.</summary>
    public async Task<AdministratieDbContext> Open(Uitvoerder wie, Guid administratie, Recht recht)
    {
        var rol = await Rol(wie, administratie);
        if (rol is null)
        {
            throw new GeenToegangException();
        }

        if (!Mag(rol.Value, recht))
        {
            throw new GeenToegangException("Je hebt in deze administratie niet het recht om dit te doen.");
        }

        var db = await dbFactory.CreateDbContextAsync();
        db.Geopend = administratie;
        return db;
    }
}

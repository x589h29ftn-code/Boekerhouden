using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Boekerhouden.Administratie.Data;

/// <summary>Fouten van de database herkennen, gelijk voor PostgreSQL en SQLite.</summary>
public static class Databasefout
{
    /// <summary>
    /// Bestaat er al een rij met dezelfde unieke sleutel? Dat gebeurt als twee gebruikers op hetzelfde moment hetzelfde
    /// vastleggen (hetzelfde boekstuknummer, dezelfde nieuwe relatie): de controle vooraf zag de ander nog niet.
    /// </summary>
    public static bool IsDubbel(DbUpdateException fout)
    {
        ArgumentNullException.ThrowIfNull(fout);
        return fout.GetBaseException() switch
        {
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,
            SqliteException { SqliteErrorCode: 19 } sqlite => sqlite.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}

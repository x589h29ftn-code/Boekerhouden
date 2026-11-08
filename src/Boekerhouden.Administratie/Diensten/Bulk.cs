namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat een handeling op meerdere regels tegelijk deed: hoeveel lukten, en per mislukte regel waarom.</summary>
public sealed record Bulkuitkomst(int Gelukt, IReadOnlyList<string> Fouten);

/// <summary>
/// Een handeling op meerdere regels (bijv. concepten definitief maken): regel voor regel via de gewone dienst, zodat
/// elke regel dezelfde controles, rechten en hetzelfde wijzigingslog krijgt als één voor één. Een mislukte regel houdt
/// de rest niet tegen; de reden komt met de naam van de regel in de uitkomst.
/// </summary>
public static class Bulk
{
    public static async Task<Bulkuitkomst> Voer<T>(IEnumerable<T> regels, Func<T, Task<Uitkomst>> handeling, Func<T, string> naam)
    {
        ArgumentNullException.ThrowIfNull(regels);
        ArgumentNullException.ThrowIfNull(handeling);
        ArgumentNullException.ThrowIfNull(naam);
        var gelukt = 0;
        var fouten = new List<string>();
        foreach (var regel in regels)
        {
            Uitkomst uitkomst;
            try
            {
                uitkomst = await handeling(regel);
            }
            catch (GeenToegangException fout)
            {
                uitkomst = Uitkomst.Mislukt(fout.Message);
            }

            if (uitkomst.IsGelukt)
            {
                gelukt++;
            }
            else
            {
                fouten.Add($"{naam(regel)}: {string.Join(" ", uitkomst.Fouten)}");
            }
        }

        return new Bulkuitkomst(gelukt, fouten);
    }
}

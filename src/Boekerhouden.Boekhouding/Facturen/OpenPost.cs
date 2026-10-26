namespace Boekerhouden.Boekhouding.Facturen;

/// <summary>
/// Een openstaande post: een factuur die (nog) niet (helemaal) betaald is. Bedragen volgen het grootboek: een
/// debiteur is positief (te ontvangen), een crediteur negatief (te betalen). Een bankmutatie met hetzelfde bedrag
/// (ontvangst positief, betaling negatief) maakt de post nihil.
/// </summary>
public sealed record OpenPost(
    Guid Id,
    Factuursoort Soort,
    string Relatie,
    string Factuurnummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    decimal Bedrag,
    decimal Openstaand,
    string? Betalingskenmerk = null)
{
    public bool IsBetaald => Openstaand == 0;

    /// <summary>Het aantal dagen na de vervaldatum, of 0 als de post nog niet vervallen is.</summary>
    public int DagenVervallen(DateOnly peildatum) => Math.Max(0, peildatum.DayNumber - Vervaldatum.DayNumber);
}

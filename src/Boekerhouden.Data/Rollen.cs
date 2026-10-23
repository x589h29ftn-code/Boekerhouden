namespace Boekerhouden.Data;

/// <summary>De rollen in de applicatie (zie docs/onderzoek §7.8). Een gebruiker kan meerdere rollen hebben.</summary>
public static class Rollen
{
    public const string Medewerker = "Medewerker";
    public const string Reviewer = "Reviewer";
    public const string Accountant = "Accountant";
    public const string Beheerder = "Beheerder";

    public static readonly IReadOnlyList<string> Alle = [Medewerker, Reviewer, Accountant, Beheerder];
}

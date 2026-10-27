namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wie een handeling uitvoert; komt in het wijzigingslog.</summary>
public sealed record Uitvoerder(Guid? Id, string Naam)
{
    public static readonly Uitvoerder Systeem = new(null, "Systeem");
}

/// <summary>Uitkomst van een handeling: gelukt, of een lijst met fouten in gewone taal.</summary>
public record Uitkomst(IReadOnlyList<string> Fouten)
{
    public bool IsGelukt => Fouten.Count == 0;

    public static Uitkomst Gelukt { get; } = new([]);

    public static Uitkomst Mislukt(params string[] fouten) => new(fouten);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Fabrieksmethoden maken de aanroepen leesbaar: Uitkomst<Guid>.Met(id).")]
public sealed record Uitkomst<T>(T? Waarde, IReadOnlyList<string> Fouten) : Uitkomst(Fouten)
{
    public static Uitkomst<T> Met(T waarde) => new(waarde, []);

    public static new Uitkomst<T> Mislukt(params string[] fouten) => new(default, fouten);
}

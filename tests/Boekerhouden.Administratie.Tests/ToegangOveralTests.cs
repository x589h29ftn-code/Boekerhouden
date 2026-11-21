using System.Collections;
using System.Reflection;
using Boekerhouden.Administratie.Diensten;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Toegang over de hele breedte: elke openbare methode van elke dienst die een administratie aanneemt, wordt
/// aangeroepen door iemand die er geen lid van is (met het ID van een bestaande administratie vol gegevens). Hij hoort
/// een <see cref="GeenToegangException"/> te krijgen; een methode die gewoon iets teruggeeft (behalve leeg of een
/// mislukte uitkomst), is een lek. Zo vergeet een nieuwe dienst de controle niet.
/// </summary>
public sealed class ToegangOveralTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Een_buitenstaander_komt_via_geen_enkele_dienst_bij_een_administratie()
    {
        var w = await Praktijk.Werkadministratie.Maak(app, "Toegang Overal B.V.");
        await w.Concept(new(2025, 3, 1), 100m);
        var buitenstaander = await app.Gebruiker("Ellen Elders");
        var lekken = new List<string>();
        var getest = 0;
        var diensten = typeof(Toegang).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true, IsAbstract: false } && t.Namespace == typeof(Toegang).Namespace && t.Name.EndsWith("Dienst", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        await using var scope = app.Services.CreateAsyncScope();
        foreach (var type in diensten)
        {
            if (scope.ServiceProvider.GetService(type) is not { } dienst)
            {
                continue;
            }

            foreach (var methode in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetParameters() is [{ ParameterType: var p0 }, { ParameterType: var p1, Name: "administratie" }, ..] && p0 == typeof(Uitvoerder) && p1 == typeof(Guid)))
            {
                getest++;
                var aan = methode;
                if (methode.IsGenericMethodDefinition)
                {
                    try
                    {
                        aan = methode.MakeGenericMethod([.. methode.GetGenericArguments().Select(_ => typeof(string))]);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                }

                var args = aan.GetParameters().Select((p, i) => i switch
                {
                    0 => buitenstaander,
                    1 => w.Id,
                    _ => Standaard(p),
                }).ToArray();
                var naam = $"{type.Name}.{methode.Name}({string.Join(", ", methode.GetParameters().Skip(2).Select(p => p.ParameterType.Name))})";
                try
                {
                    var resultaat = aan.Invoke(dienst, args);
                    if (resultaat is Task taak)
                    {
                        await taak;
                        resultaat = taak.GetType().GetProperty("Result")?.GetValue(taak);
                    }

                    if (!IsLeeg(resultaat))
                    {
                        lekken.Add($"{naam}: gaf {Beschrijf(resultaat)}");
                    }
                }
                catch (Exception e) when (Binnenste(e) is GeenToegangException)
                {
                }
                catch (Exception e) when (Binnenste(e) is ArgumentException or NullReferenceException or InvalidOperationException or FormatException)
                {
                    // Geweigerd op de invoer vóór de toegang: geen lek, maar wel melden als het geen toegangsfout is.
                    uitvoer.WriteLine($"{naam}: {Binnenste(e).GetType().Name} ({Binnenste(e).Message.Split('\n')[0]})");
                }
            }
        }

        uitvoer.WriteLine($"{getest} methoden geprobeerd, {lekken.Count} lekken.");
        foreach (var l in lekken)
        {
            uitvoer.WriteLine("  - " + l);
        }

        Assert.True(getest > 100, $"Maar {getest} methoden gevonden.");
        Assert.True(lekken.Count == 0, string.Join("\n", lekken));
    }

    private static Exception Binnenste(Exception e) => e is TargetInvocationException { InnerException: { } i } ? Binnenste(i) : e is AggregateException { InnerException: { } a } ? Binnenste(a) : e;

    private static object? Standaard(ParameterInfo p)
    {
        if (p.HasDefaultValue)
        {
            return p.DefaultValue;
        }

        var t = p.ParameterType;
        if (t == typeof(string))
        {
            return "x";
        }

        if (t == typeof(Stream))
        {
            return new MemoryStream([1, 2, 3]);
        }

        if (t == typeof(DateOnly))
        {
            return new DateOnly(2025, 12, 31);
        }

        if (t == typeof(int))
        {
            return 2025;
        }

        if (t.IsArray)
        {
            return Array.CreateInstance(t.GetElementType()!, 0);
        }

        if (t.IsGenericType && t.GetGenericTypeDefinition() is var g && (g == typeof(IReadOnlyList<>) || g == typeof(IEnumerable<>) || g == typeof(IList<>) || g == typeof(List<>) || g == typeof(IReadOnlyCollection<>)))
        {
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(t.GetGenericArguments()[0]));
        }

        return t.IsValueType ? Activator.CreateInstance(t) : Maak(t, 0);
    }

    /// <summary>Een record of klasse via zijn grootste openbare constructor, gevuld met standaardwaarden.</summary>
    private static object? Maak(Type t, int diepte)
    {
        if (diepte > 3 || t.IsAbstract || t.IsInterface || t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault() is not { } constructor)
        {
            return null;
        }

        try
        {
            return constructor.Invoke([.. constructor.GetParameters().Select(p => p.ParameterType.IsValueType || p.ParameterType == typeof(string) || p.ParameterType.IsArray || p.ParameterType.IsGenericType
                ? Standaard(p)
                : Maak(p.ParameterType, diepte + 1))]);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static bool IsLeeg(object? resultaat) => resultaat switch
    {
        null => true,
        Uitkomst u => !u.IsGelukt,
        string s => s.Length == 0,
        ICollection c => c.Count == 0,
        IEnumerable e when e is not string => !e.Cast<object>().Any(),
        _ => resultaat.GetType().GetProperty("IsGelukt")?.GetValue(resultaat) is false,
    };

    private static string Beschrijf(object? resultaat) =>
        resultaat is IEnumerable e and not string ? $"{e.Cast<object>().Count()} items" : resultaat?.ToString()?.Split('\n')[0] ?? "null";
}

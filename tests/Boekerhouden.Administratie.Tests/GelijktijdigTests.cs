using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Twee gebruikers die op hetzelfde moment hetzelfde doen: één lukt, de ander krijgt een gewone melding.</summary>
public sealed class GelijktijdigTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Hetzelfde_boekstuk_tegelijk_boeken_lukt_een_keer_en_geeft_de_ander_een_melding()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Tegelijk B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();

        for (var ronde = 1; ronde <= 10; ronde++)
        {
            var boekstuk = $"2026-{ronde:0000}";
            var uitkomsten = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
                grootboek.Boek(wie, id, new Journaalpost("MEM", boekstuk, new(2026, 3, ronde), $"Tegelijk {i}", [new("4300", 10m), new("1100", -10m)])))));

            Assert.Single(uitkomsten, u => u.IsGelukt);
            Assert.All(uitkomsten.Where(u => !u.IsGelukt), u => Assert.Equal([$"Boekstuk {boekstuk} bestaat al in dagboek MEM."], u.Fouten));
        }

        Assert.Equal(10, (await grootboek.Posten(wie, id, "MEM")).Count);
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
    }
}

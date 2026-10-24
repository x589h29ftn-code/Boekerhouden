using Boekerhouden.Server.Components.Layout;
using Boekerhouden.Server.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Boekerhouden.Server.Tests;

public sealed class HulpTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly string Id = Guid.NewGuid().ToString();

    public static TheoryData<string, string> Paginas => new()
    {
        { "", "klanten" },
        { $"klanten/{Id}", "klanten" },
        { $"dossiers/{Id}", "dossier" },
        { $"dossiers/{Id}/rubriceren", "rubriceren" },
        { $"dossiers/{Id}/correcties", "correcties" },
        { $"dossiers/{Id}/proefbalans?correcties=true", "proefbalans" },
        { $"dossiers/{Id}/werkprogramma?post=BMva", "werkprogramma" },
        { $"dossiers/{Id}/rapport#h1", "rapport" },
        { "beheer/gebruikers", "beheer-gebruikers" },
        { "beheer/rgs", "beheer-rgs" },
        { "beheer/koppelsjabloon", "beheer-koppelsjabloon" },
        { "beheer/werkprogramma", "beheer-werkprogramma" },
        { "beheer/grondslagen", "beheer-grondslagen" },
        { "beheer/kantoor", "beheer-kantoor" },
        { "beheer/wijzigingslog", "beheer-wijzigingslog" },
    };

    [Theory]
    [MemberData(nameof(Paginas))]
    public async Task Het_vraagteken_wijst_naar_een_bestaand_deel_van_de_documentatie(string pad, string sectie)
    {
        Assert.Equal(sectie, HoofdLayout.Sectie(pad));

        using var scope = server.Services.CreateScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<Hulp>(ParameterView.Empty)).ToHtmlString());
        Assert.Contains($"id=\"{sectie}\"", html, StringComparison.Ordinal);
    }
}

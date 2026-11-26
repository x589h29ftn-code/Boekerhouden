using System.Net;
using Boekerhouden.Administratie.Identiteit;
using Microsoft.AspNetCore.Http;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De rem op inloggen per IP-adres: alleen het versturen van een inlogformulier, niet van de eigen pc.</summary>
public sealed class InlogremTests
{
    [Theory]
    [InlineData("POST", "/inloggen", "203.0.113.5", "203.0.113.5")]
    [InlineData("POST", "/inloggen/tweede-stap", "203.0.113.5", "203.0.113.5")]
    [InlineData("POST", "/account/passkey/opties-inloggen", "2001:db8::1", "2001:db8::1")]
    [InlineData("POST", "/eerste-gebruiker", "203.0.113.5", "203.0.113.5")]
    [InlineData("GET", "/inloggen", "203.0.113.5", null)]
    [InlineData("POST", "/a/123/bank", "203.0.113.5", null)]
    [InlineData("POST", "/inloggen", "127.0.0.1", null)]
    [InlineData("POST", "/inloggen", "::1", null)]
    public void Telt_alleen_inlogpogingen_van_buiten(string methode, string pad, string ip, string? sleutel)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = methode;
        context.Request.Path = pad;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        Assert.Equal(sleutel, Inlogrem.Sleutel(context));
    }

    [Fact]
    public void Zonder_adres_geen_rem()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/inloggen";
        Assert.Null(Inlogrem.Sleutel(context));
    }
}

using System.Text.RegularExpressions;
using FluentAssertions;

namespace TransportBidPortal.Tests.Unit;

/// <summary>
/// Contratos estáticos do SPA: grid de lanes em Criar BID deve usar &lt;select&gt; para Origem (CDs do shipper), não input texto livre.
/// </summary>
public class FrontendBidOriginContractTests
{
    private static string AppJsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wwwroot", "app.js"));

    private static string IndexHtmlPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wwwroot", "index.html"));

    [Fact]
    public void AppJs_BidLaneOrigin_UsesSelect_NotFreeTextInput()
    {
        File.Exists(AppJsPath).Should().BeTrue($"app.js deve existir em {AppJsPath}");

        var js = File.ReadAllText(AppJsPath);

        js.Should().MatchRegex(@"<select\s+data-key=""Origin""", "Origem na grid deve ser <select data-key=\"Origin\">");

        var freeTextOrigin = Regex.IsMatch(js, @"<input[^>]*data-key=""Origin""", RegexOptions.IgnoreCase);
        freeTextOrigin.Should().BeFalse("não deve existir <input ... data-key=\"Origin\"> na grid de lanes");
    }

    [Fact]
    public void AppJs_BidCollectLanes_ResolvesOriginFromFacilitySelect()
    {
        var js = File.ReadAllText(AppJsPath);

        js.Should().Contain("el.tagName === \"SELECT\"", "bidCollectLanes deve tratar SELECT para montar texto canônico da origem");
        js.Should().Contain("facilitiesCache.find", "resolução da origem deve usar facilitiesCache");
    }

    [Fact]
    public void IndexHtml_AppJs_HasCacheBustQuery()
    {
        File.Exists(IndexHtmlPath).Should().BeTrue($"index.html deve existir em {IndexHtmlPath}");

        var html = File.ReadAllText(IndexHtmlPath);
        html.Should().MatchRegex(@"app\.js\?v=\d+", "script app.js deve incluir ?v=N para cache bust");
    }
}

namespace TransportBidPortal.Services;

/// <summary>
/// Quando <c>ShipperDeliveryPoint.Region</c> está vazio, derivamos a macro-região a partir da UF
/// para exibição na coluna Região das lanes e filtros de dashboard (N, NE, CO, SE, S).
/// </summary>
public static class BrazilMacroRegion
{
    public static string FromUf(string? uf)
    {
        if (string.IsNullOrWhiteSpace(uf)) return string.Empty;
        var u = uf.Trim().ToUpperInvariant();
        if (u.Length != 2) return string.Empty;

        // Norte
        if (u is "AC" or "AM" or "AP" or "PA" or "RO" or "RR" or "TO") return "N";
        // Nordeste
        if (u is "AL" or "BA" or "CE" or "MA" or "PB" or "PE" or "PI" or "RN" or "SE") return "NE";
        // Centro-Oeste
        if (u is "DF" or "GO" or "MT" or "MS") return "CO";
        // Sudeste
        if (u is "ES" or "MG" or "RJ" or "SP") return "SE";
        // Sul
        if (u is "PR" or "RS" or "SC") return "S";
        return string.Empty;
    }

    /// <summary>Região efetiva: cadastro explícito ou fallback pela UF.</summary>
    public static string EffectiveRegion(string? storedRegion, string? uf)
    {
        var r = storedRegion?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(r)) return r;
        return FromUf(uf);
    }
}

namespace TransportBidPortal.Services;

/// <summary>
/// Estimativa de pedágio em rotas. Primeira entrega: valor zero (sem API paga).
/// Substituir implementação por integração (ex.: provedor de rota com pedágio) sem alterar consumidores.
/// </summary>
public interface ITollEstimator
{
    /// <param name="originLat">Latitude origem (graus).</param>
    /// <param name="originLon">Longitude origem.</param>
    /// <param name="destLat">Latitude destino.</param>
    /// <param name="destLon">Longitude destino.</param>
    Task<decimal> EstimateTollAsync(
        decimal? originLat, decimal? originLon,
        decimal? destLat, decimal? destLon,
        CancellationToken ct);
}

public sealed class NoOpTollEstimator(ILogger<NoOpTollEstimator> logger) : ITollEstimator
{
    public Task<decimal> EstimateTollAsync(
        decimal? originLat, decimal? originLon,
        decimal? destLat, decimal? destLon,
        CancellationToken ct)
    {
        logger.LogDebug("Toll estimate stub (zero) — orig ({OrigLat},{OrigLon}) dest ({DestLat},{DestLon})",
            originLat, originLon, destLat, destLon);
        return Task.FromResult(0m);
    }
}

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;


namespace TransportBidPortal.Services;

public record CepResult(
    string Cep,
    string Logradouro,
    string Complemento,
    string Bairro,
    string Localidade,
    string Uf,
    string Ibge);

public interface ICepService
{
    Task<CepResult?> LookupAsync(string cep, CancellationToken ct);
}

public class CepService(
    IHttpClientFactory httpClientFactory,
    AppDbContext db,
    ILogger<CepService> logger) : ICepService
{
    private static readonly ConcurrentDictionary<string, CepResult> MemoryCache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CepResult?> LookupAsync(string cep, CancellationToken ct)
    {
        try
        {
            var normalized = NormalizeCep(cep);
            if (normalized is null)
                return null;

            if (MemoryCache.TryGetValue(normalized, out var cached))
                return cached;

            var dbEntry = await db.CepCaches
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Cep == normalized, ct);

            if (dbEntry is not null)
            {
                var result = MapToResult(dbEntry);
                MemoryCache.TryAdd(normalized, result);
                return result;
            }

            var apiResult = await FetchFromViaCepAsync(normalized, ct);
            if (apiResult is null)
                return null;

            db.CepCaches.Add(new CepCache
            {
                Cep = apiResult.Cep,
                Logradouro = apiResult.Logradouro,
                Complemento = apiResult.Complemento,
                Bairro = apiResult.Bairro,
                Localidade = apiResult.Localidade,
                Uf = apiResult.Uf,
                Ibge = apiResult.Ibge,
                CachedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            MemoryCache.TryAdd(normalized, apiResult);
            return apiResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to lookup CEP {Cep}", cep);
            return null;
        }
    }

    private static string? NormalizeCep(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 8 ? digits : null;
    }

    private async Task<CepResult?> FetchFromViaCepAsync(string cep, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("ViaCEP");
        using var response = await client.GetAsync($"https://viacep.com.br/ws/{cep}/json/", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("erro", out var erroProp) && erroProp.GetBoolean())
            return null;

        var dto = JsonSerializer.Deserialize<ViaCepResponse>(json, JsonOptions);
        if (dto is null)
            return null;

        var normalizedCep = NormalizeCep(dto.Cep) ?? cep;
        return new CepResult(
            normalizedCep,
            dto.Logradouro ?? string.Empty,
            dto.Complemento ?? string.Empty,
            dto.Bairro ?? string.Empty,
            dto.Localidade ?? string.Empty,
            dto.Uf ?? string.Empty,
            dto.Ibge ?? string.Empty);
    }

    private static CepResult MapToResult(CepCache entity) =>
        new(entity.Cep,
            entity.Logradouro,
            entity.Complemento,
            entity.Bairro,
            entity.Localidade,
            entity.Uf,
            entity.Ibge);

    private sealed class ViaCepResponse
    {
        public string? Cep { get; set; }
        public string? Logradouro { get; set; }
        public string? Complemento { get; set; }
        public string? Bairro { get; set; }
        public string? Localidade { get; set; }
        public string? Uf { get; set; }
        public string? Ibge { get; set; }
    }
}

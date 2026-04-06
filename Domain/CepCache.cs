using System.ComponentModel.DataAnnotations;

namespace TransportBidPortal.Domain;

public class CepCache
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(9)] public string Cep { get; set; } = string.Empty;
    [MaxLength(200)] public string Logradouro { get; set; } = string.Empty;
    [MaxLength(80)] public string Complemento { get; set; } = string.Empty;
    [MaxLength(80)] public string Bairro { get; set; } = string.Empty;
    [MaxLength(120)] public string Localidade { get; set; } = string.Empty;
    [MaxLength(2)] public string Uf { get; set; } = string.Empty;
    [MaxLength(10)] public string Ibge { get; set; } = string.Empty;
    public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
}

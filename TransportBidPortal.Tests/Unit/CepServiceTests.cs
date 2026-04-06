using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TransportBidPortal.Domain;
using TransportBidPortal.Services;
using TransportBidPortal.Tests.Helpers;

namespace TransportBidPortal.Tests.Unit;

public class CepServiceTests : IDisposable
{
    private readonly Data.AppDbContext _db;

    public CepServiceTests()
    {
        _db = InMemoryDbHelper.Create();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task LookupAsync_NullCep_ReturnsNull()
    {
        var sut = CreateService(HttpStatusCode.OK, "{}");
        var result = await sut.LookupAsync(null!, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_InvalidCepLength_ReturnsNull()
    {
        var sut = CreateService(HttpStatusCode.OK, "{}");
        var result = await sut.LookupAsync("123", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_DbCacheHit_ReturnsCachedResult()
    {
        _db.CepCaches.Add(new CepCache
        {
            Cep = "01001000",
            Logradouro = "Praca da Se",
            Bairro = "Se",
            Localidade = "Sao Paulo",
            Uf = "SP",
            Ibge = "3550308"
        });
        await _db.SaveChangesAsync();

        var sut = CreateService(HttpStatusCode.OK, "{}");
        var result = await sut.LookupAsync("01001-000", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Localidade.Should().Be("Sao Paulo");
        result.Uf.Should().Be("SP");
    }

    [Fact]
    public async Task LookupAsync_ApiSuccess_SavesToDatabaseAndReturns()
    {
        var apiJson = """
        {
            "cep": "01001-000",
            "logradouro": "Praca da Se",
            "complemento": "lado impar",
            "bairro": "Se",
            "localidade": "Sao Paulo",
            "uf": "SP",
            "ibge": "3550308"
        }
        """;

        var sut = CreateService(HttpStatusCode.OK, apiJson);
        var result = await sut.LookupAsync("01001000", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Logradouro.Should().Be("Praca da Se");

        var dbEntry = _db.CepCaches.FirstOrDefault(c => c.Cep == "01001000");
        dbEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task LookupAsync_ApiReturnsErro_ReturnsNull()
    {
        var apiJson = """{"erro": true}""";
        var sut = CreateService(HttpStatusCode.OK, apiJson);
        var result = await sut.LookupAsync("99999999", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_ApiHttpError_ReturnsNull()
    {
        var sut = CreateService(HttpStatusCode.InternalServerError, "Server Error");
        var result = await sut.LookupAsync("88888888", CancellationToken.None);
        result.Should().BeNull();
    }

    private CepService CreateService(HttpStatusCode statusCode, string responseBody)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseBody)
            });

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("ViaCEP")).Returns(client);

        var logger = Mock.Of<ILogger<CepService>>();
        return new CepService(factoryMock.Object, _db, logger);
    }
}

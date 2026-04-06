using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportBidPortal.Services;

namespace TransportBidPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CepController(ICepService cepService) : ControllerBase
{
    [HttpGet("{cep}")]
    public async Task<ActionResult<CepResult>> Lookup(string cep, CancellationToken ct)
    {
        var result = await cepService.LookupAsync(cep, ct);
        if (result is null)
            return NotFound("CEP não encontrado.");
        return Ok(result);
    }
}

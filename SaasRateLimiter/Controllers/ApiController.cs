using Microsoft.AspNetCore.Mvc;

namespace SaasRateLimiter.Controllers;

[ApiController]
[Route("api")]
public sealed class ApiController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        status = "ok",
        service = "saas-rate-limiter",
    });

    [HttpGet("items")]
    public IActionResult IndexItems() => Ok(new
    {
        data = new[]
        {
            new { id = 1, name = "Widget" },
            new { id = 2, name = "Gadget" },
        },
    });

    [HttpPost("items")]
    public IActionResult StoreItem() => StatusCode(StatusCodes.Status201Created, new
    {
        id = 3,
        name = "New item",
    });

    [HttpDelete("items/{id}")]
    public IActionResult DestroyItem(string id) => Ok(new
    {
        deleted = id,
    });
}

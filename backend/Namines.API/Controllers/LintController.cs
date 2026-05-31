using Microsoft.AspNetCore.Mvc;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LintController : ControllerBase
{
    private readonly ILinterService _linterService;

    public LintController(ILinterService linterService)
    {
        _linterService = linterService;
    }

    [HttpPost]
    public IActionResult LintSchema([FromBody] DatabaseSchema schema)
    {
        var result = _linterService.Lint(schema);
        return Ok(result);
    }
}

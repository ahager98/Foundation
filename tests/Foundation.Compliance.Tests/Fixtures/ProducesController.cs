using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class ProducesController : ControllerBase
{
    [ProducesResponseType(typeof(PersonRecord), 200)]
    public Task<IActionResult> Get() => throw new NotSupportedException();
}

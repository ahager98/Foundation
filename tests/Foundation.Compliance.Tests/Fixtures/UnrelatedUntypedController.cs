using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class UnrelatedUntypedController : ControllerBase
{
    public IActionResult Health() => throw new NotSupportedException();
}

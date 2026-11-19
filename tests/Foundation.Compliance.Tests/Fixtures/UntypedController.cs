using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class UntypedController : ControllerBase
{
    [Authorize(Policy = "CanReadPeople")]
    public PersonRecord Typed() => throw new NotSupportedException();

    public IActionResult Untyped() => throw new NotSupportedException();
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class MissingAuthController : ControllerBase
{
    public Task<ActionResult<List<PersonRecord>>> List() => throw new NotSupportedException();

    [Authorize]
    public PersonRecord PlainAuthorize() => throw new NotSupportedException();

    [AllowAnonymous]
    public PersonRecord Anonymous() => throw new NotSupportedException();

    [Authorize(Roles = "Admin")]
    public int Unrelated() => 1;

    public int NoClassifiedType() => 1;

    [NonAction]
    public PersonRecord Hidden() => throw new NotSupportedException();
}

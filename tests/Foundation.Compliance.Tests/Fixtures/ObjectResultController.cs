using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class ObjectResultController : ControllerBase
{
    [Authorize(Policy = "CanReadPeople")]
    public PersonRecord Typed() => throw new NotSupportedException();

    public OkObjectResult Hidden() => throw new NotSupportedException();
}

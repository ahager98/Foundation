using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class ExplicitPolicyController : ControllerBase
{
    [Authorize(Policy = "CanReadPeople")]
    public PersonRecord Get(int id) => throw new NotSupportedException();
}

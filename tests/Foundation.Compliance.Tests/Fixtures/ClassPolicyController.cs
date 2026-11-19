using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

[Authorize(Policy = "ClassWide")]
public class ClassPolicyController : ControllerBase
{
    public PersonRecord Get() => throw new NotSupportedException();
}

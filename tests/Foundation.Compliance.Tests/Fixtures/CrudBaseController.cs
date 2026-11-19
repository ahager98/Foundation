using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public abstract class CrudBaseController : ControllerBase
{
    public PersonRecord GetInherited() => throw new NotSupportedException();
}

using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class StructReturningController : ControllerBase
{
    public PersonSummary Get() => throw new NotSupportedException();
}

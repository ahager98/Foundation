using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

public class NestedExposureController : ControllerBase
{
    public CaseView Get() => throw new NotSupportedException();
}

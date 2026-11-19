using Microsoft.AspNetCore.Http;

namespace Foundation.Compliance.Tests;

public static class MinimalHandlers
{
    public static PersonRecord GetPerson() => throw new NotSupportedException();

    public static Task<IResult> GetPersonResult() => throw new NotSupportedException();
}

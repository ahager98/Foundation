using Foundation.DataAccess;

namespace Foundation.Compliance.Tests;

public class NoRetentionNote : BaseEntity
{
    [Phi] public string Body { get; set; } = "";
}

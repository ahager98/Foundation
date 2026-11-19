using Foundation.DataAccess;

namespace Foundation.Compliance.Tests;

[Retention(0, "")]
public class BadRetentionNote : BaseEntity
{
    [Phi] public string Body { get; set; } = "";
}

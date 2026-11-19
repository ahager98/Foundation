using Foundation.DataAccess;

namespace Foundation.Compliance.Tests;

[Retention(7, "anonymize")]
public class BlankReasonCase : BaseEntity
{
    [NotClassified("  ")] public string Mystery { get; set; } = "";
    [NotClassified("lookup code, no personal data")] public string Code { get; set; } = "";
}

using Foundation.DataAccess;

namespace Foundation.Compliance.Tests;

[Retention(7, "anonymize")]
public class PersonRecord : BaseEntity, ITenantScoped
{
    public int TenantId { get; set; }
    [Pii] public string LastName { get; set; } = "";
    [Phi] public string CareLevel { get; set; } = "";
    [NotClassified("lookup code, no personal data")] public string StatusCode { get; set; } = "";
}

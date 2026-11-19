using Foundation.DataAccess;

namespace Foundation.Compliance.Tests;

[Retention(7, "anonymize")]
public class PersonRecordWithNewField : BaseEntity
{
    [Pii] public string LastName { get; set; } = "";
    public string Diagnosis { get; set; } = ""; // the seeded violation: no classification
}

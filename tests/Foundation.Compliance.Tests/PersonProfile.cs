using Foundation.Compliance;
using Foundation.DataAccess;

namespace SystemAdmin.People;

[Retention(7, "anonymize")]
public class PersonProfile : BaseEntity
{
    [Pii] public string Name { get; set; } = "";
    public string Diagnosis { get; set; } = ""; // the seeded violation, in a namespace that merely starts with "System"
}

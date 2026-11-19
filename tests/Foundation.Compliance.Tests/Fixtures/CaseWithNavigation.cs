using Foundation.DataAccess;

namespace Foundation.Compliance.Tests;

public class CaseWithNavigation : BaseEntity
{
    [NotClassified("count only")] public int Visits { get; set; }
    public PersonRecord? Person { get; set; }
    public List<PersonRecord> Others { get; set; } = [];
}

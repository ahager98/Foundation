namespace Foundation.Compliance.Tests;

[Retention(7, "anonymize")]
public class InheritingPersonDto : SharedPersonBase
{
    [Pii] public string LastName { get; set; } = "";
}

// No namespace declaration on purpose: an application type in the global namespace must still be scanned.
using Foundation.Compliance;

[Retention(7, "anonymize")]
public class GlobalPersonDto
{
    [Phi] public string CareLevel { get; set; } = "";
}

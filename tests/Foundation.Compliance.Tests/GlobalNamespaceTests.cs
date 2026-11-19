// No namespace declaration on purpose: an application type in the global namespace must still be scanned.
using Foundation.Compliance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class GlobalNamespaceTests
{
    [TestMethod]
    public void GlobalNamespaceDtoWithClassifiedMember_IsInTheManifest()
    {
        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Types = [typeof(GlobalPersonDto)] });

        CollectionAssert.AreEqual(new[] { "GlobalPersonDto.CareLevel (Phi)" }, report.ClassifiedMembers.ToArray());
        Assert.IsNull(typeof(GlobalPersonDto).Namespace);
    }
}

using Foundation.DataAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance.Tests;

[TestClass]
public class HipaaCheckTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    private static HipaaCheckOptions Options(Type[] types, Action<List<string>>? files = null) => new()
    {
        Types = types,
        Today = Today
    };

    private static string TempFile(string name, string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "hipaa-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // Classification ----------------------------------------------------------------------------------------

    [TestMethod]
    public void UnclassifiedFieldOnPersonType_FailsClosedAndNamesTypeAndField()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonRecordWithNewField)]));

        Assert.IsFalse(report.Passed);
        HipaaFinding finding = report.Findings.Single(f => f.Rule == HipaaRules.UnclassifiedField);
        Assert.AreEqual("Foundation.Compliance.Tests.PersonRecordWithNewField.Diagnosis", finding.Location);
    }

    [TestMethod]
    public void FullyClassifiedType_PassesAndListsManifest()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonRecord)]));

        Assert.IsTrue(report.Passed, report.ToMarkdown());
        CollectionAssert.AreEqual(
            new[] { "Foundation.Compliance.Tests.PersonRecord.CareLevel (Phi)", "Foundation.Compliance.Tests.PersonRecord.LastName (Pii)" },
            report.ClassifiedMembers.ToArray());
    }

    [TestMethod]
    public void InfrastructureMembersAndNavigations_AreNotFlagged()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(CaseWithNavigation)]));
        Assert.IsTrue(report.Passed, report.ToMarkdown());
    }

    [TestMethod]
    public void ClassifiedTypeWithoutRetention_IsFlagged()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(NoRetentionNote)]));

        Assert.AreEqual(HipaaRules.MissingRetention, report.Findings.Single().Rule);
        Assert.AreEqual("Foundation.Compliance.Tests.NoRetentionNote", report.Findings.Single().Location);
    }

    [TestMethod]
    public void Assemblies_AreScannedForTypes()
    {
        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Assemblies = [typeof(PersonRecord).Assembly], Today = Today });

        Assert.IsTrue(report.Findings.Any(f => f.Location == "Foundation.Compliance.Tests.PersonRecordWithNewField.Diagnosis"));
    }

    [TestMethod]
    public void UnclassifiedInheritedPropertyAndPublicField_FailClosed()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(InheritingPersonDto)]));

        string[] flagged = report.Findings.Where(f => f.Rule == HipaaRules.UnclassifiedField).Select(f => f.Location).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "Foundation.Compliance.Tests.InheritingPersonDto.FullName", "Foundation.Compliance.Tests.InheritingPersonDto.Notes" }, flagged);
    }

    [TestMethod]
    public void StructDtoWithClassifiedMember_IsScannedForRetention()
    {
        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Assemblies = [typeof(PersonSummary).Assembly], Today = Today });

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.MissingRetention && f.Location == "Foundation.Compliance.Tests.PersonSummary"));
        Assert.IsTrue(report.ClassifiedMembers.Contains("Foundation.Compliance.Tests.PersonSummary.LastName (Pii)"));
    }

    [TestMethod]
    public void SensitiveNamedExceptionSubclass_FrameworkMembersAreNotFlagged()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(CaseNotFoundException)]));
        Assert.IsTrue(report.Passed, report.ToMarkdown());
    }

    // Access control ---------------------------------------------------------------------------------------

    [TestMethod]
    public void EndpointWithoutExplicitPolicy_IsRejected()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonRecord), typeof(MissingAuthController)]));

        string[] flagged = report.Findings.Where(f => f.Rule == HipaaRules.EndpointAuthorization).Select(f => f.Location).Order().ToArray();
        CollectionAssert.AreEqual(
            new[] { "Foundation.Compliance.Tests.MissingAuthController.Anonymous", "Foundation.Compliance.Tests.MissingAuthController.List", "Foundation.Compliance.Tests.MissingAuthController.PlainAuthorize" },
            flagged);
    }

    [TestMethod]
    public void EndpointWithPolicyOrRoles_IsAccepted_IncludingClassLevel()
    {
        HipaaReport report = HipaaCheck.Run(Options(
            [typeof(PersonRecord), typeof(ExplicitPolicyController), typeof(ClassPolicyController)]));

        Assert.IsFalse(report.Findings.Any(f => f.Rule == HipaaRules.EndpointAuthorization), report.ToMarkdown());
    }

    [TestMethod]
    public void ActionInheritedFromAbstractBaseController_IsChecked()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonRecord), typeof(ConcreteCrudController)]));

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.EndpointAuthorization && f.Location == "Foundation.Compliance.Tests.ConcreteCrudController.GetInherited"));
    }

    [TestMethod]
    public void ClassifiedDataExposedThroughAnOrdinaryResponseDto_IsStillRejected()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonRecord), typeof(CaseView), typeof(NestedExposureController)]));

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.EndpointAuthorization && f.Location == "Foundation.Compliance.Tests.NestedExposureController.Get"));
    }

    [TestMethod]
    public void StructReturningEndpointWithoutPolicy_IsRejected()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonSummary), typeof(StructReturningController)]));

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.EndpointAuthorization && f.Location == "Foundation.Compliance.Tests.StructReturningController.Get"));
    }

    // Logging ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void ClassifiedMemberInLogStatement_IsRejectedAtLocationWithoutLeakingContent()
    {
        string file = TempFile("Service.cs",
            "class S {\n  void A(PersonRecord p) {\n    _logger.LogInformation(\"saved {Count}\", 1);\n"
            + "    _logger.LogWarning(\"bad record for {LastName} sentinel-value-123\", p.LastName);\n  }\n}\n");

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Types = [typeof(PersonRecord)], SourceFiles = [file], SourceRoot = Path.GetDirectoryName(file), Today = Today });

        HipaaFinding finding = report.Findings.Single(f => f.Rule == HipaaRules.LogLeakage);
        Assert.AreEqual("Service.cs:4", finding.Location);
        Assert.IsFalse(report.ToMarkdown().Contains("sentinel-value-123"));
    }

    [TestMethod]
    public void LogStatementWithoutClassifiedMember_Passes()
    {
        string file = TempFile("Clean.cs", "class S { void A(PersonRecord p) { _logger.LogInformation(\"saved {Id}\", p.Id); } }");

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Types = [typeof(PersonRecord)], SourceFiles = [file], Today = Today });

        Assert.IsTrue(report.Passed, report.ToMarkdown());
    }

    // Egress + configuration -------------------------------------------------------------------------------

    private static BaaInventory Inventory() => BaaInventory.FromJson(
        """
        { "vendors": [
          { "name": "Twilio", "hosts": ["api.twilio.com", "*.twilio.com"], "baaReference": "BAA-2025-014" },
          { "name": "NoAgreementYet", "hosts": ["api.pending.example"], "baaReference": "" }
        ] }
        """);

    [TestMethod]
    public void EgressTargetMissingFromInventory_IsRejectedAndNamed()
    {
        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            EgressTargets = ["https://api.twilio.com/v1", "https://llm.unlisted.example/v1/chat", "https://api.pending.example"],
            BaaInventory = Inventory(),
            Today = Today
        });

        string[] unlisted = report.Findings.Where(f => f.Rule == HipaaRules.UnlistedEgress).Select(f => f.Location).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "api.pending.example", "llm.unlisted.example" }, unlisted);
    }

    [TestMethod]
    public void ListedWildcardAndLoopbackEgress_Pass()
    {
        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            EgressTargets = ["https://eu.twilio.com", "http://localhost:5000", "127.0.0.1"],
            BaaInventory = Inventory(),
            Today = Today
        });

        Assert.IsTrue(report.Passed, report.ToMarkdown());
    }

    [TestMethod]
    public void ConfigurationFile_FlagsEgressInsecureTransportStorageAndLiteralSecrets()
    {
        string file = TempFile("appsettings.json",
            """
            {
              "Vendor": { "BaseUrl": "http://insecure.vendor.example/api", "ApiKey": "super-secret-literal" },
              "Ok": { "Url": "https://api.twilio.com", "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://kv.vault.azure.net/secrets/x)" },
              "ConnectionStrings": { "Db": "Server=x;Database=y;Encrypt=False;" },
              "Kestrel": { "SslProtocols": "Tls11" }
            }
            """);

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { ConfigurationFiles = [file], BaaInventory = Inventory(), Today = Today });

        string[] config = report.Findings.Where(f => f.Rule == HipaaRules.InsecureConfiguration).Select(f => f.Location).ToArray();
        CollectionAssert.Contains(config, "appsettings.json:insecure.vendor.example");
        CollectionAssert.Contains(config, "appsettings.json:ApiKey");
        CollectionAssert.DoesNotContain(config, "appsettings.json:ClientSecret");
        Assert.AreEqual(2, config.Count(c => c == "appsettings.json"));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.UnlistedEgress && f.Location == "insecure.vendor.example"));
        Assert.IsFalse(report.ToMarkdown().Contains("super-secret-literal"));
    }

    [TestMethod]
    public void ConfigurationFile_FlagsLiteralCredentialInsideConnectionString_ButNotKeyVaultOrPlaceholder()
    {
        string file = TempFile("appsettings.json",
            """
            {
              "ConnectionStrings": {
                "Default": "Server=x;User Id=sa;Password=hunter2;",
                "Vaulted": "Server=x;Password=@Microsoft.KeyVault(SecretUri=https://kv.vault.azure.net/secrets/x);",
                "Templated": "Server=x;Password={db-password};"
              }
            }
            """);

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { ConfigurationFiles = [file], BaaInventory = Inventory(), Today = Today });

        string[] config = report.Findings.Where(f => f.Rule == HipaaRules.InsecureConfiguration).Select(f => f.Location).ToArray();
        CollectionAssert.Contains(config, "appsettings.json:Default");
        CollectionAssert.DoesNotContain(config, "appsettings.json:Vaulted");
        CollectionAssert.DoesNotContain(config, "appsettings.json:Templated");
        Assert.IsFalse(report.ToMarkdown().Contains("hunter2"));
    }

    [TestMethod]
    public void ConfigurationFile_DoesNotFlagKeysThatMerelyContainASecretWord()
    {
        string file = TempFile("appsettings.json",
            """
            { "Auth": { "KeySecretName": "kv-signing", "RequirePasswordComplexity": "true", "DbPassword": "literal" } }
            """);

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { ConfigurationFiles = [file], BaaInventory = Inventory(), Today = Today });

        string[] config = report.Findings.Where(f => f.Rule == HipaaRules.InsecureConfiguration).Select(f => f.Location).ToArray();
        CollectionAssert.AreEqual(new[] { "appsettings.json:DbPassword" }, config);
    }

    [TestMethod]
    public void Report_StatesHowMuchWasScanned_SoAnEmptyRunIsNotMistakenForAPass()
    {
        HipaaReport empty = HipaaCheck.Run(new HipaaCheckOptions { Today = Today });
        Assert.AreEqual(0, empty.TypesScanned);
        Assert.IsTrue(empty.ToMarkdown().Contains("Scanned 0 type(s) and 0 file(s)."));

        string file = TempFile("appsettings.json", "{}");
        HipaaReport real = HipaaCheck.Run(new HipaaCheckOptions
        {
            Types = [typeof(PersonRecordWithNewField)], ConfigurationFiles = [file], Today = Today
        });
        Assert.AreEqual(1, real.TypesScanned);
        Assert.AreEqual(1, real.FilesScanned);
    }

    [TestMethod]
    public void PublicConstructors_RejectNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new BaaInventory(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new HipaaReport(null!, [], []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new HipaaReport([], null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new HipaaReport([], [], null!));
    }

    // Waivers ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void ActiveWaiver_SuppressesFindingAndAppearsInReport()
    {
        HipaaWaiver waiver = new(HipaaRules.UnclassifiedField, "Foundation.Compliance.Tests.PersonRecordWithNewField.Diagnosis",
            "classification pending review", "compliance-owner", Today.AddDays(30));

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Types = [typeof(PersonRecordWithNewField)], Waivers = [waiver], Today = Today });

        Assert.IsTrue(report.Passed, report.ToMarkdown());
        Assert.AreEqual(1, report.AppliedWaivers.Count);
        StringAssert.Contains(report.ToMarkdown(), "classification pending review");
    }

    [TestMethod]
    public void ExpiredWaiver_NoLongerSuppressesAndIsItselfReported()
    {
        HipaaWaiver waiver = new(HipaaRules.UnclassifiedField, "Foundation.Compliance.Tests.PersonRecordWithNewField.Diagnosis",
            "old", "compliance-owner", Today.AddDays(-1));

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Types = [typeof(PersonRecordWithNewField)], Waivers = [waiver], Today = Today });

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.UnclassifiedField));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.ExpiredWaiver));
        Assert.AreEqual(0, report.AppliedWaivers.Count);
    }

    // Review follow-ups ------------------------------------------------------------------------------------

    [TestMethod]
    public void NotClassifiedWithoutReason_IsAFinding_AndWithReason_IsListedInTheReport()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(BlankReasonCase)]));

        Assert.AreEqual("Foundation.Compliance.Tests.BlankReasonCase.Mystery",
            report.Findings.Single(f => f.Rule == HipaaRules.UnclassifiedField).Location);
        CollectionAssert.AreEqual(new[] { "Foundation.Compliance.Tests.BlankReasonCase.Code: lookup code, no personal data" },
            report.NotClassifiedMembers.ToArray());
        StringAssert.Contains(report.ToMarkdown(), "lookup code, no personal data");
    }

    [TestMethod]
    public void SensitivePattern_MatchesWholeWords_NotSubstrings()
    {
        Assert.IsTrue(HipaaCheck.Run(Options([typeof(IndividualNote)])).Findings.Any(f => f.Rule == HipaaRules.UnclassifiedField));
        Assert.IsTrue(HipaaCheck.Run(Options([typeof(PlatformOptions)])).Passed);
    }

    [TestMethod]
    public void NamespaceStartingWithSystem_IsStillScanned()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(SystemAdmin.People.PersonProfile)]));

        Assert.AreEqual("SystemAdmin.People.PersonProfile.Diagnosis",
            report.Findings.Single(f => f.Rule == HipaaRules.UnclassifiedField).Location);
    }

    [TestMethod]
    public void ProducesResponseTypeOnAnIActionResultAction_CountsAsTouchingClassifiedData()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonRecord), typeof(ProducesController)]));

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.EndpointAuthorization
            && f.Location == "Foundation.Compliance.Tests.ProducesController.Get"));
    }

    [TestMethod]
    public void UntypedResult_IsHeldToTheRule_OnlyInControllersThatTouchClassifiedData()
    {
        HipaaReport report = HipaaCheck.Run(Options(
            [typeof(PersonRecord), typeof(UntypedController), typeof(UnrelatedUntypedController)]));

        string[] flagged = report.Findings.Where(f => f.Rule == HipaaRules.EndpointAuthorization).Select(f => f.Location).ToArray();
        CollectionAssert.AreEqual(new[] { "Foundation.Compliance.Tests.UntypedController.Untyped" }, flagged);
    }

    private static Endpoint MinimalEndpoint(params object[] metadata) => new(
        _ => Task.CompletedTask,
        new EndpointMetadataCollection(new object[] { typeof(MinimalHandlers).GetMethod(nameof(MinimalHandlers.GetPerson))! }.Concat(metadata).ToArray()),
        "GET /people");

    private static Endpoint ResultEndpoint(params object[] metadata) => new(
        _ => Task.CompletedTask,
        new EndpointMetadataCollection(new object[] { typeof(MinimalHandlers).GetMethod(nameof(MinimalHandlers.GetPersonResult))! }.Concat(metadata).ToArray()),
        "GET /people/{id}");

    [TestMethod]
    public void UntypedMinimalApiHandler_IsHeldToTheRule_WhenClassifiedDataExists()
    {
        HipaaCheckOptions Run(Endpoint endpoint) => new() { Types = [typeof(PersonRecord)], Endpoints = [endpoint], Today = Today };

        Assert.IsTrue(HipaaCheck.Run(Run(ResultEndpoint())).Findings.Any(f => f.Rule == HipaaRules.EndpointAuthorization && f.Location == "GET /people/{id}"));
        Assert.IsTrue(HipaaCheck.Run(Run(ResultEndpoint(new AuthorizeAttribute { Policy = "CanReadPeople" }))).Passed);
    }

    [TestMethod]
    public void ObjectResultReturn_IsTreatedAsUntyped()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(PersonRecord), typeof(ObjectResultController)]));

        string[] flagged = report.Findings.Where(f => f.Rule == HipaaRules.EndpointAuthorization).Select(f => f.Location).ToArray();
        CollectionAssert.AreEqual(new[] { "Foundation.Compliance.Tests.ObjectResultController.Hidden" }, flagged);
    }

    [TestMethod]
    public void UnqualifiedLoggerMessageCall_AndCamelCaseArguments_AreScanned()
    {
        string file = TempFile("Generated.cs",
            "class S {\n  void A(string lastName) {\n    LogSaved(_logger, lastName);\n"
            + "    _logger.LogInformation(\"saved {lastName}\");\n    _logger.LogInformation(\"saved\", lastName);\n"
            + "    _logger.LogInformation(\"saved {Count}\", 3);\n  }\n}\n");

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            Types = [typeof(PersonRecord)], SourceFiles = [file], SourceRoot = Path.GetDirectoryName(file), Today = Today
        });

        string[] lines = report.Findings.Where(f => f.Rule == HipaaRules.LogLeakage).Select(f => f.Location).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "Generated.cs:3", "Generated.cs:4", "Generated.cs:5" }, lines);
    }

    [TestMethod]
    public void ConfigurationFindings_AreRootRelative_WhenASourceRootIsGiven()
    {
        string root = Path.Combine(Path.GetTempPath(), "hipaa-tests-" + Guid.NewGuid().ToString("N"));
        string a = Path.Combine(root, "svcA");
        string b = Path.Combine(root, "svcB");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        string fa = Path.Combine(a, "appsettings.json");
        string fb = Path.Combine(b, "appsettings.json");
        File.WriteAllText(fa, "{ \"ApiKey\": \"literal\" }");
        File.WriteAllText(fb, "{ \"ApiKey\": \"literal\" }");

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            ConfigurationFiles = [fa, fb], SourceRoot = root, BaaInventory = Inventory(), Today = Today
        });

        string[] config = report.Findings.Where(f => f.Rule == HipaaRules.InsecureConfiguration).Select(f => f.Location).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "svcA/appsettings.json:ApiKey", "svcB/appsettings.json:ApiKey" }, config);
    }

    [TestMethod]
    public void UrlWithUserinfo_UsesTheRealHost_AndIsReportedForEmbeddingCredentials()
    {
        string file = TempFile("appsettings.json",
            """
            { "Hook": "https://api.twilio.com@evil.example/x", "Feed": "https://user:pass@api.twilio.com/v1" }
            """);

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { ConfigurationFiles = [file], BaaInventory = Inventory(), Today = Today });

        // The spoofed URL's real host is not covered; the credentials in both URLs are reported without being shown.
        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.UnlistedEgress && f.Location == "evil.example"));
        Assert.IsFalse(report.Findings.Any(f => f.Rule == HipaaRules.UnlistedEgress && f.Location == "user"));
        Assert.AreEqual(2, report.Findings.Count(f => f.Rule == HipaaRules.InsecureConfiguration && f.Message.Contains("userinfo")));
        Assert.IsFalse(report.ToMarkdown().Contains("user:pass"));
    }

    [TestMethod]
    public void MaxWaiverDays_MustBePositiveAndBounded()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HipaaCheck.Run(new HipaaCheckOptions { MaxWaiverDays = 0, Today = Today }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HipaaCheck.Run(new HipaaCheckOptions { MaxWaiverDays = int.MaxValue, Today = Today }));
    }

    [TestMethod]
    public void CommonSecretKeyNames_AreFlagged()
    {
        string file = TempFile("appsettings.json",
            """
            { "AwsSecretAccessKey": "x", "SecretValue": "x", "PasswordValue": "x", "AccessToken": "x",
              "SubscriptionKey": "x", "PrivateKey": "x", "KeySecretName": "kv-name" }
            """);

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { ConfigurationFiles = [file], BaaInventory = Inventory(), Today = Today });

        string[] config = report.Findings.Where(f => f.Rule == HipaaRules.InsecureConfiguration).Select(f => f.Location).Order().ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "appsettings.json:AccessToken", "appsettings.json:AwsSecretAccessKey", "appsettings.json:PasswordValue",
            "appsettings.json:PrivateKey", "appsettings.json:SecretValue", "appsettings.json:SubscriptionKey"
        }, config);
    }

    [TestMethod]
    public void MinimalApiEndpoint_WithoutPolicy_IsRejected_WithPolicyPasses_AnonymousIsRejected()
    {
        HipaaCheckOptions Run(Endpoint endpoint) => new() { Types = [typeof(PersonRecord)], Endpoints = [endpoint], Today = Today };

        Assert.IsTrue(HipaaCheck.Run(Run(MinimalEndpoint())).Findings.Any(f => f.Rule == HipaaRules.EndpointAuthorization && f.Location == "GET /people"));
        Assert.IsTrue(HipaaCheck.Run(Run(MinimalEndpoint(new AuthorizeAttribute { Policy = "CanReadPeople" }))).Passed);
        Assert.IsFalse(HipaaCheck.Run(Run(MinimalEndpoint(new AuthorizeAttribute { Policy = "CanReadPeople" }, new AllowAnonymousAttribute()))).Passed);
    }

    [TestMethod]
    public void LogOverloadWithLogLevel_IsScanned()
    {
        string file = TempFile("Generic.cs", "class S { void A(PersonRecord p) { _logger.Log(LogLevel.Error, ex, \"bad {LastName}\", p.LastName); } }");

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Types = [typeof(PersonRecord)], SourceFiles = [file], Today = Today });

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.LogLeakage));
    }

    [TestMethod]
    public void LogLocations_AreRelativeToSourceRoot_SoSameNamedFilesStayDistinct()
    {
        string root = Path.Combine(Path.GetTempPath(), "hipaa-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "A"));
        Directory.CreateDirectory(Path.Combine(root, "B"));
        string leak = "class S { void M(PersonRecord p) { _logger.LogInformation(\"{LastName}\", p.LastName); } }";
        string a = Path.Combine(root, "A", "Service.cs");
        string b = Path.Combine(root, "B", "Service.cs");
        File.WriteAllText(a, leak);
        File.WriteAllText(b, leak);

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            Types = [typeof(PersonRecord)], SourceFiles = [a, b], SourceRoot = root, Today = Today
        });

        CollectionAssert.AreEqual(new[] { "A/Service.cs:1", "B/Service.cs:1" },
            report.Findings.Where(f => f.Rule == HipaaRules.LogLeakage).Select(f => f.Location).Order().ToArray());
    }

    [TestMethod]
    public void BareHostWithPort_IsParsedAndChecked()
    {
        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            EgressTargets = ["api.twilio.com:443", "10.0.0.1:8080", "[::1]:5000", "localhost:5000"],
            BaaInventory = Inventory(),
            Today = Today
        });

        CollectionAssert.AreEqual(new[] { "10.0.0.1" },
            report.Findings.Where(f => f.Rule == HipaaRules.UnlistedEgress).Select(f => f.Location).ToArray());
    }

    [TestMethod]
    public void ConfigurationUrls_UppercaseSchemesEscapedSlashesAndOtherSchemes_AreDiscovered_TemplatesAreFindings()
    {
        string file = TempFile("appsettings.json",
            """
            {
              "A": "HTTPS://shout.example/api",
              "B": "https:\/\/escaped.example\/api",
              "C": "wss://socket.example/hub",
              "D": "https://{tenant}.vendor.example/api"
            }
            """);

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { ConfigurationFiles = [file], BaaInventory = Inventory(), Today = Today });

        string[] egress = report.Findings.Where(f => f.Rule == HipaaRules.UnlistedEgress).Select(f => f.Location).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "shout.example", "escaped.example", "socket.example" }, egress);
        Assert.IsTrue(egress.Contains("appsettings.json:{tenant}.vendor.example"));
    }

    [TestMethod]
    public void WaiverWithoutReasonOrApprover_OrBeyondTheMaximum_IsNotHonored()
    {
        string location = "Foundation.Compliance.Tests.PersonRecordWithNewField.Diagnosis";
        HipaaWaiver[] bad =
        [
            new(HipaaRules.UnclassifiedField, location, "", "compliance-owner", Today.AddDays(30)),
            new(HipaaRules.UnclassifiedField, location, "reason", " ", Today.AddDays(30)),
            new(HipaaRules.UnclassifiedField, location, "reason", "compliance-owner", DateOnly.MaxValue)
        ];

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions { Types = [typeof(PersonRecordWithNewField)], Waivers = bad, Today = Today });

        Assert.IsTrue(report.Findings.Any(f => f.Rule == HipaaRules.UnclassifiedField));
        Assert.AreEqual(3, report.Findings.Count(f => f.Rule == HipaaRules.ExpiredWaiver));
        Assert.AreEqual(0, report.AppliedWaivers.Count);
    }

    [TestMethod]
    public void Report_StatesWhatItDoesNotCover()
    {
        string md = HipaaCheck.Run(Options([typeof(PersonRecord)])).ToMarkdown();

        StringAssert.Contains(md, "Not covered by this check");
        StringAssert.Contains(md, "minimal-API");
    }

    [TestMethod]
    public void NonPositiveRetentionOrBlankDeletion_IsAFinding()
    {
        HipaaReport report = HipaaCheck.Run(Options([typeof(BadRetentionNote)]));

        Assert.AreEqual(HipaaRules.MissingRetention, report.Findings.Single().Rule);
        StringAssert.Contains(report.Findings.Single().Message, "positive number of years");
    }

    [TestMethod]
    public void ATypeListedInBothAssembliesAndTypes_IsCheckedOnce()
    {
        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            Assemblies = [typeof(PersonRecord).Assembly],
            Types = [typeof(PersonRecordWithNewField)],
            Today = Today
        });

        Assert.AreEqual(1, report.Findings.Count(f => f.Location == "Foundation.Compliance.Tests.PersonRecordWithNewField.Diagnosis"));
        Assert.AreEqual(1, report.ClassifiedMembers.Count(m => m == "Foundation.Compliance.Tests.PersonRecord.CareLevel (Phi)"));
    }

    [TestMethod]
    public void BaaInventory_AcceptsBracketedIpv6Loopback_AndRejectsANullHost()
    {
        BaaInventory inventory = BaaInventory.FromJson("{}");

        Assert.IsTrue(inventory.Covers("[::1]"));
        Assert.ThrowsExactly<ArgumentNullException>(() => inventory.Covers(null!));

        HipaaReport report = HipaaCheck.Run(new HipaaCheckOptions
        {
            ConfigurationFiles = [TempFile("appsettings.json", "{ \"A\": \"http://[::1]:5000/api\" }")],
            Today = Today
        });
        Assert.IsTrue(report.Passed, report.ToMarkdown());
    }

    [TestMethod]
    public void Output_IsDeterministic_RegardlessOfTypeDiscoveryOrder()
    {
        Type[] forward = [typeof(PersonRecordWithNewField), typeof(InheritingPersonDto), typeof(BlankReasonCase)];
        HipaaReport a = HipaaCheck.Run(Options(forward));
        HipaaReport b = HipaaCheck.Run(Options(forward.Reverse().ToArray()));

        CollectionAssert.AreEqual(a.Findings.Select(f => $"{f.Rule} {f.Location}").ToArray(), b.Findings.Select(f => $"{f.Rule} {f.Location}").ToArray());
        CollectionAssert.AreEqual(a.ClassifiedMembers.ToArray(), b.ClassifiedMembers.ToArray());
        CollectionAssert.AreEqual(a.Findings.Select(f => f.Rule).ToArray(), a.Findings.Select(f => f.Rule).Order(StringComparer.Ordinal).ToArray());
    }

    // Report + guards -------------------------------------------------------------------------------------

    [TestMethod]
    public void Report_RendersResultFindingsAndDisclaimer()
    {
        string md = HipaaCheck.Run(Options([typeof(PersonRecordWithNewField)])).ToMarkdown();

        StringAssert.Contains(md, "FAILED");
        StringAssert.Contains(md, "HIPAA-CLASSIFY");
        StringAssert.Contains(md, "not a HIPAA certification");
        StringAssert.Contains(HipaaCheck.Run(Options([typeof(PersonRecord)])).ToMarkdown(), "PASSED");
    }

    [TestMethod]
    public void Run_NullOptions_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => HipaaCheck.Run(null!));

    [TestMethod]
    public void BaaInventory_EmptyJson_CoversOnlyLoopback()
    {
        BaaInventory inventory = BaaInventory.FromJson("{}");

        Assert.IsTrue(inventory.Covers("localhost"));
        Assert.IsTrue(inventory.Covers("app.localhost"));
        Assert.IsFalse(inventory.Covers("api.twilio.com"));
    }

    // Generic defaults ---------------------------------------------------------------------------------------

    [Retention(7, "anonymize")]
    private sealed class PersonSiteRecord : BaseEntity
    {
        [Pii] public string LastName { get; set; } = "";
        public int SiteId { get; set; }
    }

    private sealed class ResidentSummary : BaseEntity
    {
        public string Detail { get; set; } = "";
    }

    [TestMethod]
    public void ServiceScopingColumn_IsFlaggedUntilAddedToExemptMemberNames()
    {
        HipaaReport flagged = HipaaCheck.Run(Options([typeof(PersonSiteRecord)]));
        Assert.IsTrue(flagged.Findings.Any(f => f.Rule == HipaaRules.UnclassifiedField && f.Location.EndsWith(".SiteId", StringComparison.Ordinal)));

        HipaaCheckOptions exempted = new()
        {
            Types = [typeof(PersonSiteRecord)],
            Today = Today,
            ExemptMemberNames = new HashSet<string>(StringComparer.Ordinal) { "Id", "TenantId", "RowVersion", "IsActive", "SiteId" }
        };
        Assert.IsTrue(HipaaCheck.Run(exempted).Passed);
    }

    [TestMethod]
    public void DefaultTypePattern_IsGeneric_AndServicesCanWidenIt()
    {
        Assert.IsTrue(HipaaCheck.Run(Options([typeof(ResidentSummary)])).Passed);

        HipaaCheckOptions widened = new()
        {
            Types = [typeof(ResidentSummary)],
            Today = Today,
            SensitiveTypePattern = new System.Text.RegularExpressions.Regex(@"(Resident)s?(?![a-z])")
        };
        Assert.IsFalse(HipaaCheck.Run(widened).Passed);
    }
}

using System.Text;

namespace Foundation.Compliance;

/// <summary>Result of a run. Contains only locations and rule ids, never PHI or PII values.</summary>
public sealed class HipaaReport(
    IReadOnlyList<HipaaFinding> findings,
    IReadOnlyList<HipaaWaiver> appliedWaivers,
    IReadOnlyList<string> classifiedMembers,
    IReadOnlyList<string>? notClassifiedMembers = null,
    IReadOnlyList<string>? scopeNotes = null)
{
    public IReadOnlyList<HipaaFinding> Findings { get; } = findings ?? throw new ArgumentNullException(nameof(findings));

    public IReadOnlyList<HipaaWaiver> AppliedWaivers { get; } =
        appliedWaivers ?? throw new ArgumentNullException(nameof(appliedWaivers));

    /// <summary>"Type.Member (Phi|Pii)" for every classified member: the generated classification manifest.</summary>
    public IReadOnlyList<string> ClassifiedMembers { get; } =
        classifiedMembers ?? throw new ArgumentNullException(nameof(classifiedMembers));

    /// <summary>"Type.Member: reason" for every member exempted with [NotClassified], so each exemption is visible in the evidence.</summary>
    public IReadOnlyList<string> NotClassifiedMembers { get; } = notClassifiedMembers ?? [];

    /// <summary>What this check does not cover, repeated in every report so a pass is not read as more than it is.</summary>
    public IReadOnlyList<string> ScopeNotes { get; } = scopeNotes ?? [];

    /// <summary>How many types were examined. Zero means the run had nothing to look at, so a pass proves nothing.</summary>
    public int TypesScanned { get; internal set; }

    /// <summary>How many source and configuration files were scanned.</summary>
    public int FilesScanned { get; internal set; }

    public bool Passed => Findings.Count == 0;

    /// <summary>Evidence for the PR and the Jira story: what was classified, what was checked, waivers.</summary>
    public string ToMarkdown()
    {
        StringBuilder sb = new();
        sb.AppendLine("## HIPAA technical-safeguard check");
        sb.AppendLine();
        sb.AppendLine(Passed ? "**Result: PASSED**" : $"**Result: FAILED ({Findings.Count} finding(s))**");
        sb.AppendLine();
        sb.AppendLine("Technical guardrail only; not a HIPAA certification or legal advice. Compliance owner confirms the mapping.");

        sb.AppendLine().AppendLine($"Scanned {TypesScanned} type(s) and {FilesScanned} file(s).");
        sb.AppendLine().AppendLine($"### Findings ({Findings.Count})");
        foreach (HipaaFinding f in Findings)
            sb.AppendLine($"- `{f.Rule}` {f.Location}: {f.Message}");

        sb.AppendLine().AppendLine($"### Waivers applied ({AppliedWaivers.Count})");
        foreach (HipaaWaiver w in AppliedWaivers)
            sb.AppendLine($"- `{w.Rule}` {w.Location}: {w.Reason} (approved by {w.ApprovedBy}, expires {w.Expires:yyyy-MM-dd})");

        sb.AppendLine().AppendLine($"### Classified members ({ClassifiedMembers.Count})");
        foreach (string m in ClassifiedMembers)
            sb.AppendLine($"- {m}");

        sb.AppendLine().AppendLine($"### Exempted with [NotClassified] ({NotClassifiedMembers.Count})");
        foreach (string m in NotClassifiedMembers)
            sb.AppendLine($"- {m}");

        sb.AppendLine().AppendLine("### Not covered by this check");
        foreach (string note in ScopeNotes)
            sb.AppendLine($"- {note}");

        return sb.ToString();
    }
}

namespace Foundation.Compliance.Tests;

public abstract class SharedPersonBase
{
    public string FullName { get; set; } = ""; // inherited and unclassified: the seeded violation
    public string Notes = "";                   // public field, also unclassified
}

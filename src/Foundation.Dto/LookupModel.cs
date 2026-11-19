using System.Diagnostics.CodeAnalysis;

namespace Foundation.Dto;

[ExcludeFromCodeCoverage]
public class LookupModel
{
    public int Id { get; set; }
    public required string Description { get; set; }
}

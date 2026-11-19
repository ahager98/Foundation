using System.Diagnostics.CodeAnalysis;

namespace Foundation.Dto;

/// <summary>
/// A single page of results plus the paging metadata a caller needs to request further pages.
/// </summary>
/// <typeparam name="T">The element type of the page.</typeparam>
[ExcludeFromCodeCoverage]
public class PagedResultModel<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];

    /// <summary>The 1-based page number these items belong to.</summary>
    public int PageNumber { get; set; }

    /// <summary>The maximum number of items per page used for this query.</summary>
    public int PageSize { get; set; }

    /// <summary>Total number of items across all pages (after filtering, before paging).</summary>
    public int TotalCount { get; set; }

    /// <summary>Total number of pages available for the current page size.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    /// <summary>True when a page after <see cref="PageNumber"/> exists.</summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>True when a page before <see cref="PageNumber"/> exists.</summary>
    public bool HasPreviousPage => PageNumber > 1;
}

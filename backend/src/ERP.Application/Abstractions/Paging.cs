namespace ERP.Application.Abstractions;

/// <summary>One page of a longer list, and enough to ask for the next.</summary>
/// <typeparam name="T">What the list holds.</typeparam>
/// <param name="Items">The rows on this page.</param>
/// <param name="Page">Which page this is, from one.</param>
/// <param name="PageSize">How many rows a page holds.</param>
/// <param name="TotalCount">How many rows the filter matched in total.</param>
/// <remarks>
/// <para>
/// Introduced with the sales list, which is the first list in this system that grows
/// without limit - the chart of accounts is a few hundred rows and a stock document list
/// is bounded by its date range, but a busy shop raises thousands of invoices a month.
/// The shape is here rather than in the sales module because every list that follows it -
/// the reports of §12.10, purchase, service - will want the same one, and two answers to
/// "how does this API page" is one more than a client should have to learn.
/// </para>
/// <para>
/// The total is counted rather than inferred. A client that only knew whether more rows
/// existed could not show "page 3 of 40", and counting a filtered set is one cheap query
/// beside the one that fetches the page.
/// </para>
/// </remarks>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Gets how many pages the filter matched.</summary>
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Gets whether another page follows this one.</summary>
    public bool HasMore => Page < TotalPages;
}

/// <summary>Builds pages.</summary>
/// <remarks>
/// The factory sits beside the type rather than on it, so a caller writes
/// <c>PagedResult.Empty&lt;T&gt;(…)</c> without the analyser complaining about static
/// members hanging off a generic type - and without the type's own shape growing a member
/// that is not part of what a page is.
/// </remarks>
public static class PagedResult
{
    /// <summary>An empty page, for a filter that matched nothing.</summary>
    /// <typeparam name="T">What the list would have held.</typeparam>
    /// <param name="page">The page asked for.</param>
    /// <param name="pageSize">The size asked for.</param>
    /// <returns>The empty page.</returns>
    public static PagedResult<T> Empty<T>(int page, int pageSize) =>
        new([], page, pageSize, 0);
}

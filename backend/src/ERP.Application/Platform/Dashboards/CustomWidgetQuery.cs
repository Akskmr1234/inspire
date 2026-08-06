using System.Text.RegularExpressions;
using ERP.SharedKernel.Results;

namespace ERP.Application.Platform.Dashboards;

/// <summary>
/// Validates the statement behind a custom dashboard widget.
/// </summary>
/// <remarks>
/// <para>
/// This is the first of four guards and the weakest of them, which is worth stating
/// plainly because parser-style validation is so often mistaken for the whole defence.
/// A blocklist of keywords can be evaded by anyone determined enough, and nothing here
/// should be relied on alone.
/// </para>
/// <para>
/// The guarantees that actually hold are the other three. The statement runs inside a
/// <c>READ ONLY</c> transaction, so a write that got past this cannot commit - the
/// database refuses it. It runs under a statement timeout and a row cap, so it cannot
/// hold a connection or return a million rows. And it runs as the ordinary application
/// role, which means PostgreSQL row-level security applies exactly as it does to every
/// other query in the system: a statement naming another tenant's rows returns nothing,
/// and no amount of cleverness in the SQL changes that, because the check is in the
/// database rather than in this file.
/// </para>
/// <para>
/// What this does buy is a clear error at the point somebody writes the query, rather
/// than a database exception at the point somebody else opens the dashboard.
/// </para>
/// </remarks>
public static partial class CustomWidgetQuery
{
    /// <summary>The columns a custom query must return.</summary>
    public const string LabelColumn = "label";

    /// <summary>The value column a custom query must return.</summary>
    public const string ValueColumn = "value";

    /// <summary>
    /// Statements and routines refused outright, as defence in depth.
    /// </summary>
    /// <remarks>
    /// Matched on word boundaries, so a column called <c>created_at</c> or a table
    /// called <c>updates</c> is not caught by the rule against <c>CREATE</c> and
    /// <c>UPDATE</c>. The file-and-sleep entries are there because they are the usual
    /// next thing tried once writing is off the table.
    /// </remarks>
    private static readonly string[] ForbiddenWords =
    [
        "insert", "update", "delete", "merge", "truncate", "drop", "alter", "create",
        "grant", "revoke", "copy", "call", "do", "vacuum", "analyze", "reindex",
        "cluster", "listen", "notify", "unlisten", "lock", "prepare", "execute",
        "deallocate", "declare", "fetch", "move", "set", "reset", "discard", "refresh",
        "comment", "reassign", "security", "pg_sleep", "pg_read_file",
        "pg_read_binary_file", "pg_ls_dir", "lo_import", "lo_export", "dblink",
        "pg_terminate_backend", "pg_cancel_backend",
    ];

    /// <summary>Checks that a statement is a single, read-only select.</summary>
    /// <param name="query">The statement as written.</param>
    /// <returns>The trimmed statement, or the reason it was refused.</returns>
    public static Result<string> Validate(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result.Failure<string>(Error.Validation(
                "CustomWidget.QueryRequired", "A custom widget must carry a query."));
        }

        string trimmed = query.Trim();

        // Comments are refused rather than stripped. Stripping them correctly means
        // handling nesting, dollar quoting and string literals that contain the
        // delimiters - and getting that subtly wrong is how a blocklist is evaded.
        // Nobody needs a comment in a dashboard widget.
        if (trimmed.Contains("--", StringComparison.Ordinal)
            || trimmed.Contains("/*", StringComparison.Ordinal))
        {
            return Result.Failure<string>(Error.Validation(
                "CustomWidget.CommentsNotAllowed",
                "A widget query cannot contain comments."));
        }

        // One statement. A trailing semicolon is ordinary enough to allow, but a
        // second statement is the classic way to append a write to a read.
        string withoutTrailing = trimmed.TrimEnd(';').TrimEnd();

        if (withoutTrailing.Contains(';', StringComparison.Ordinal))
        {
            return Result.Failure<string>(Error.Validation(
                "CustomWidget.SingleStatementOnly",
                "A widget query must be a single statement."));
        }

        if (!withoutTrailing.StartsWith("select", StringComparison.OrdinalIgnoreCase)
            && !withoutTrailing.StartsWith("with", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<string>(Error.Validation(
                "CustomWidget.SelectOnly",
                "A widget query must begin with SELECT or WITH."));
        }

        string? forbidden = ForbiddenWords
            .FirstOrDefault(word => WordPattern(word).IsMatch(withoutTrailing));

        if (forbidden is not null)
        {
            return Result.Failure<string>(Error.Validation(
                "CustomWidget.ForbiddenKeyword",
                $"A widget query cannot use '{forbidden.ToUpperInvariant()}'."));
        }

        // Not a security rule: the reader looks for these two columns, and a query
        // without them would render an empty panel with nothing to explain why.
        if (!withoutTrailing.Contains(LabelColumn, StringComparison.OrdinalIgnoreCase)
            || !withoutTrailing.Contains(ValueColumn, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<string>(Error.Validation(
                "CustomWidget.ColumnsRequired",
                $"A widget query must return columns named '{LabelColumn}' and "
                + $"'{ValueColumn}'."));
        }

        return Result.Success(withoutTrailing);
    }

    /// <summary>Builds a word-boundary matcher for a forbidden word.</summary>
    /// <param name="word">The word.</param>
    /// <returns>The compiled pattern.</returns>
    private static Regex WordPattern(string word) =>
        new($@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
}

/// <summary>Runs the statement behind a custom dashboard widget.</summary>
/// <remarks>
/// Separate from <see cref="IDashboardMetricReader"/> because the two have opposite
/// risk profiles. A metric is code somebody reviewed; a custom query is text somebody
/// typed, and it runs under a read-only transaction, a statement timeout, and a row cap
/// that the metric reader has no need of.
/// </remarks>
public interface ICustomWidgetExecutor
{
    /// <summary>Runs one custom query and returns its rows.</summary>
    /// <param name="query">The statement, already validated.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The label and value pairs, or the reason the statement failed.</returns>
    /// <remarks>
    /// A failure is returned rather than thrown. A widget whose query no longer runs -
    /// a renamed column, a timeout - must not take the whole dashboard down with it:
    /// the panel reports that it failed and the rest of the screen still draws.
    /// </remarks>
    Task<Result<IReadOnlyList<MetricPoint>>> ExecuteAsync(
        string query,
        CancellationToken cancellationToken = default);
}

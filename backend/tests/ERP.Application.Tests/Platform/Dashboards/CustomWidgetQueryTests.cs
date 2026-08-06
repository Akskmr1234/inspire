using ERP.Application.Platform.Dashboards;
using ERP.SharedKernel.Results;

namespace ERP.Application.Tests.Platform.Dashboards;

/// <summary>
/// Tests for <see cref="CustomWidgetQuery"/>.
/// </summary>
/// <remarks>
/// This validator is the weakest of the four guards on custom SQL and is tested as
/// such. Passing every case here does not mean a statement is safe - the read-only
/// transaction, the timeout, and row-level security are what make that true, and they
/// are tested where they live. What these cover is that the obvious attempts are
/// refused with a message naming the problem, at the moment somebody writes the query
/// rather than when a colleague opens the dashboard.
/// </remarks>
public sealed class CustomWidgetQueryTests
{
    private const string Valid =
        "SELECT code AS label, SUM(amount) AS value FROM ledgers GROUP BY code";

    [Fact]
    public void A_plain_select_returning_label_and_value_is_accepted()
    {
        Result<string> result = CustomWidgetQuery.Validate(Valid);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Valid);
    }

    [Fact]
    public void A_common_table_expression_is_accepted()
    {
        Result<string> result = CustomWidgetQuery.Validate(
            "WITH totals AS (SELECT 1 AS value) SELECT 'a' AS label, value FROM totals");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_trailing_semicolon_is_tolerated_and_removed()
    {
        Result<string> result = CustomWidgetQuery.Validate(Valid + ";");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotEndWith(";");
    }

    [Theory]
    [InlineData("INSERT INTO ledgers (label, value) VALUES (1, 2)")]
    [InlineData("UPDATE ledgers SET label = 'x', value = 1")]
    [InlineData("DELETE FROM ledgers WHERE label = 'x' AND value = 1")]
    [InlineData("DROP TABLE ledgers")]
    [InlineData("TRUNCATE ledgers")]
    public void A_statement_that_writes_is_refused(string query)
    {
        CustomWidgetQuery.Validate(query).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void A_write_appended_after_a_read_is_refused()
    {
        // The classic shape: a perfectly ordinary select, and then something else.
        Result<string> result = CustomWidgetQuery.Validate(
            Valid + "; DROP TABLE ledgers");

        result.Error.Code.ShouldBeOneOf(
            "CustomWidget.SingleStatementOnly", "CustomWidget.ForbiddenKeyword");
    }

    [Fact]
    public void A_data_modifying_common_table_expression_is_refused()
    {
        // WITH ... AS (DELETE ...) starts with WITH and reads like a query. The
        // keyword check is what catches it here; the read-only transaction is what
        // would stop it if this check ever failed.
        Result<string> result = CustomWidgetQuery.Validate(
            "WITH gone AS (DELETE FROM ledgers RETURNING id) "
            + "SELECT id AS label, 1 AS value FROM gone");

        result.Error.Code.ShouldBe("CustomWidget.ForbiddenKeyword");
    }

    [Theory]
    [InlineData("SELECT 'a' AS label, 1 AS value -- and then something")]
    [InlineData("SELECT 'a' AS label, /* hidden */ 1 AS value")]
    public void A_query_carrying_comments_is_refused(string query)
    {
        // Refused rather than stripped: stripping comments correctly means handling
        // nesting, dollar quoting, and delimiters inside string literals, and getting
        // that subtly wrong is precisely how a blocklist is evaded.
        CustomWidgetQuery.Validate(query).Error.Code
            .ShouldBe("CustomWidget.CommentsNotAllowed");
    }

    [Theory]
    [InlineData("SELECT pg_sleep(30) AS value, 'a' AS label")]
    [InlineData("SELECT pg_read_file('/etc/passwd') AS value, 'a' AS label")]
    [InlineData("SELECT 'a' AS label, 1 AS value FROM dblink('', '') AS t(x int)")]
    public void The_usual_next_attempts_after_writing_is_blocked_are_refused(string query)
    {
        CustomWidgetQuery.Validate(query).Error.Code
            .ShouldBe("CustomWidget.ForbiddenKeyword");
    }

    [Fact]
    public void A_column_whose_name_contains_a_forbidden_word_is_not_caught()
    {
        // Word boundaries matter: "created_at" contains "create" and a table called
        // "updates" contains "update". Refusing those would make the feature unusable
        // on the very schema it reads.
        Result<string> result = CustomWidgetQuery.Validate(
            "SELECT created_at AS label, updated_count AS value FROM ledgers");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_query_without_the_expected_columns_is_refused()
    {
        // Not a security rule - the reader looks for these two, and a query without
        // them would draw an empty panel with nothing to explain why.
        CustomWidgetQuery.Validate("SELECT code, name FROM ledgers").Error.Code
            .ShouldBe("CustomWidget.ColumnsRequired");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_is_refused(string? query)
    {
        CustomWidgetQuery.Validate(query).Error.Code
            .ShouldBe("CustomWidget.QueryRequired");
    }
}

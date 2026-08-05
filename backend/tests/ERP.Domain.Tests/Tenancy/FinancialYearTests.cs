using System.Globalization;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Tenancy;

/// <summary>Tests for <see cref="FinancialYear"/>.</summary>
public sealed class FinancialYearTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    // ---------------------------------------------------------------- creation

    [Fact]
    public void A_year_can_span_an_arbitrary_range()
    {
        // The legacy system's year runs 01-10-2021 to 31-12-2026. Nothing may
        // assume a 12-month period or an April/January start.
        FinancialYear year = CreateYear("LEGACY", "2021-10-01", "2026-12-31");

        year.StartDate.ShouldBe(new DateOnly(2021, 10, 1));
        year.EndDate.ShouldBe(new DateOnly(2026, 12, 31));
        year.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_single_day_year_is_valid()
    {
        // Degenerate but legitimate - a stub period during a migration cutover.
        Result<FinancialYear> result = Create("STUB", "2026-04-01", "2026-04-01");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Contains(new DateOnly(2026, 4, 1)).ShouldBeTrue();
    }

    [Fact]
    public void An_end_date_before_the_start_is_rejected()
    {
        Result<FinancialYear> result = Create("BAD", "2026-12-31", "2026-01-01");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FinancialYear.EndBeforeStart");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_code_is_rejected(string code)
    {
        Result<FinancialYear> result = Create(code, "2026-01-01", "2026-12-31");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FinancialYear.CodeRequired");
    }

    [Fact]
    public void The_code_is_trimmed()
    {
        Create("  2026  ", "2026-01-01", "2026-12-31").Value.Code.ShouldBe("2026");
    }

    // ---------------------------------------------------------------- overlap

    [Theory]
    // Identical range.
    [InlineData("2026-01-01", "2026-12-31", true)]
    // Fully contained within the existing year.
    [InlineData("2026-06-01", "2026-06-30", true)]
    // Overlaps the start by a single day.
    [InlineData("2025-06-01", "2026-01-01", true)]
    // Overlaps the end by a single day.
    [InlineData("2026-12-31", "2027-06-30", true)]
    // Entirely encloses the existing year.
    [InlineData("2025-01-01", "2027-12-31", true)]
    // Abuts the start with no shared day.
    [InlineData("2025-01-01", "2025-12-31", false)]
    // Abuts the end with no shared day.
    [InlineData("2027-01-01", "2027-12-31", false)]
    public void Overlap_detection_is_inclusive_at_both_ends(
        string start,
        string end,
        bool expectedOverlap)
    {
        FinancialYear existing = CreateYear("2026", "2026-01-01", "2026-12-31");

        existing.OverlapsWith(D(start), D(end)).ShouldBe(expectedOverlap);
    }

    [Fact]
    public void An_overlapping_year_cannot_be_created()
    {
        FinancialYear existing = CreateYear("2026", "2026-01-01", "2026-12-31");

        Result<FinancialYear> result = FinancialYear.Create(
            Tenant, Firm, "2026-B",
            new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31),
            [existing]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FinancialYear.Overlaps");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void A_consecutive_non_overlapping_year_is_accepted()
    {
        FinancialYear existing = CreateYear("2026", "2026-01-01", "2026-12-31");

        Result<FinancialYear> result = FinancialYear.Create(
            Tenant, Firm, "2027",
            new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            [existing]);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_duplicate_code_is_rejected_case_insensitively()
    {
        FinancialYear existing = CreateYear("FY26", "2026-01-01", "2026-12-31");

        Result<FinancialYear> result = FinancialYear.Create(
            Tenant, Firm, "fy26",
            new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            [existing]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FinancialYear.DuplicateCode");
    }

    // ---------------------------------------------------------------- posting gate

    [Fact]
    public void Posting_is_permitted_on_the_boundary_dates()
    {
        FinancialYear year = CreateYear("2026", "2026-01-01", "2026-12-31");

        year.CanPostOn(new DateOnly(2026, 1, 1)).IsSuccess.ShouldBeTrue();
        year.CanPostOn(new DateOnly(2026, 12, 31)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2025-12-31")]
    [InlineData("2027-01-01")]
    public void Posting_outside_the_range_is_refused(string date)
    {
        FinancialYear year = CreateYear("2026", "2026-01-01", "2026-12-31");

        Result result = year.CanPostOn(D(date));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FinancialYear.DateOutOfRange");
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public void A_closed_year_refuses_postings_but_can_be_reopened()
    {
        FinancialYear year = CreateYear("2026", "2026-01-01", "2026-12-31");
        DateOnly inRange = new(2026, 6, 15);

        year.Close().IsSuccess.ShouldBeTrue();
        year.CanPostOn(inRange).Error.Code.ShouldBe("FinancialYear.Closed");

        year.Reopen().IsSuccess.ShouldBeTrue();
        year.CanPostOn(inRange).IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public void A_year_must_be_closed_before_it_can_be_locked()
    {
        FinancialYear year = CreateYear("2026", "2026-01-01", "2026-12-31");

        // Guards against a mis-click permanently sealing a period that still
        // needs adjusting entries - locking has no reverse.
        Result premature = year.Lock();
        premature.IsFailure.ShouldBeTrue();
        premature.Error.Code.ShouldBe("FinancialYear.MustCloseBeforeLocking");

        year.Close();
        year.Lock().IsSuccess.ShouldBeTrue();
        year.Status.ShouldBe(FinancialYearStatus.Locked);
    }

    [Fact]
    public void A_locked_year_can_never_be_reopened_closed_or_posted_to()
    {
        FinancialYear year = CreateYear("2026", "2026-01-01", "2026-12-31");
        year.Close();
        year.Lock();

        year.Reopen().Error.Code.ShouldBe("FinancialYear.LockedCannotReopen");
        year.Close().Error.Code.ShouldBe("FinancialYear.AlreadyLocked");
        year.CanPostOn(new DateOnly(2026, 6, 15)).Error.Code.ShouldBe("FinancialYear.Locked");
        year.Status.ShouldBe(FinancialYearStatus.Locked);
    }

    // ---------------------------------------------------------------- events

    [Fact]
    public void Lifecycle_transitions_raise_domain_events()
    {
        FinancialYear year = CreateYear("2026", "2026-01-01", "2026-12-31");

        year.Close();
        year.Reopen();
        year.Close();
        year.Lock();

        year.DomainEvents.Select(e => e.GetType()).ShouldBe(
        [
            typeof(FinancialYearClosed),
            typeof(FinancialYearReopened),
            typeof(FinancialYearClosed),
            typeof(FinancialYearLocked),
        ]);
    }

    [Fact]
    public void Creating_a_year_raises_no_events()
    {
        // Events describe transitions. Creation is persisted by the repository,
        // so an extra "created" event would only duplicate what the insert
        // already records.
        CreateYear("2026", "2026-01-01", "2026-12-31").DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Events_can_be_cleared_after_dispatch()
    {
        FinancialYear year = CreateYear("2026", "2026-01-01", "2026-12-31");
        year.Close();

        year.ClearDomainEvents();

        year.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Parses an ISO date independently of the machine's locale.</summary>
    private static DateOnly D(string isoDate) =>
        DateOnly.Parse(isoDate, CultureInfo.InvariantCulture);

    private static Result<FinancialYear> Create(string code, string start, string end) =>
        FinancialYear.Create(Tenant, Firm, code, D(start), D(end), []);

    private static FinancialYear CreateYear(string code, string start, string end) =>
        Create(code, start, end).Value;
}

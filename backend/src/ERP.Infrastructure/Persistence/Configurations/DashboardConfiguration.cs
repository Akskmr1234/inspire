using ERP.Domain.Identity;
using ERP.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Dashboard"/> to the <c>dashboards</c> table.</summary>
public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("dashboards");
        builder.HasKey(dashboard => dashboard.Id);
        builder.Property(dashboard => dashboard.Id).ValueGeneratedNever();

        builder.Property(dashboard => dashboard.Code)
            .HasMaxLength(Dashboard.MaximumCodeLength).IsRequired();
        builder.Property(dashboard => dashboard.Name)
            .HasMaxLength(Dashboard.MaximumNameLength).IsRequired();
        builder.Property(dashboard => dashboard.NameArabic)
            .HasMaxLength(Dashboard.MaximumNameLength);
        builder.Property(dashboard => dashboard.SortOrder).IsRequired();
        builder.Property(dashboard => dashboard.IsSystem).IsRequired();

        builder
            .HasIndex(dashboard => new { dashboard.FirmId, dashboard.Code })
            .IsUnique()
            .HasDatabaseName("ix_dashboards_code");

        builder.HasIndex(dashboard => dashboard.TenantId)
            .HasDatabaseName("ix_dashboards_tenant");

        // Panels and assignments have no meaning apart from the dashboard they belong
        // to, so both cascade - unlike the menu, where a heading's children are screens
        // that outlive it.
        builder.HasMany(dashboard => dashboard.Widgets)
            .WithOne()
            .HasForeignKey(widget => widget.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(dashboard => dashboard.Widgets)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_widgets");

        builder.HasMany(dashboard => dashboard.Roles)
            .WithOne()
            .HasForeignKey(assignment => assignment.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(dashboard => dashboard.Roles)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_roles");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="DashboardWidget"/> to the <c>dashboard_widgets</c> table.</summary>
public sealed class DashboardWidgetConfiguration : IEntityTypeConfiguration<DashboardWidget>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DashboardWidget> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("dashboard_widgets");
        builder.HasKey(widget => widget.Id);
        builder.Property(widget => widget.Id).ValueGeneratedNever();

        // Neither is required on its own: a panel names a metric or carries a query,
        // and exactly one of them is set. Which one is enforced by the aggregate's two
        // factory methods rather than by a check constraint, there being no way to
        // construct a widget except through them.
        builder.Property(widget => widget.MetricCode)
            .HasMaxLength(DashboardWidget.MaximumMetricCodeLength);

        builder.Property(widget => widget.Query)
            .HasMaxLength(DashboardWidget.MaximumQueryLength);

        // Projected from whether a query is present rather than stored beside it,
        // which could disagree with it.
        builder.Ignore(widget => widget.IsCustom);
        builder.Property(widget => widget.Title)
            .HasMaxLength(DashboardWidget.MaximumTitleLength).IsRequired();
        builder.Property(widget => widget.TitleArabic)
            .HasMaxLength(DashboardWidget.MaximumTitleLength);
        builder.Property(widget => widget.Kind).HasConversion<int>().IsRequired();
        builder.Property(widget => widget.SortOrder).IsRequired();
        builder.Property(widget => widget.Span).IsRequired();

        builder.HasIndex(widget => widget.TenantId)
            .HasDatabaseName("ix_dashboard_widgets_tenant");
    }
}

/// <summary>Maps <see cref="DashboardRole"/> to the <c>dashboard_roles</c> table.</summary>
public sealed class DashboardRoleConfiguration : IEntityTypeConfiguration<DashboardRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DashboardRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("dashboard_roles");

        // The pair is the identity: a dashboard is shown to a role once or not at all.
        builder.HasKey(assignment => new { assignment.DashboardId, assignment.RoleId });

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(assignment => assignment.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // How the dashboard list is read: every dashboard assigned to any of the
        // roles the signed-in user holds.
        builder.HasIndex(assignment => assignment.RoleId)
            .HasDatabaseName("ix_dashboard_roles_role");

        builder.HasIndex(assignment => assignment.TenantId)
            .HasDatabaseName("ix_dashboard_roles_tenant");
    }
}

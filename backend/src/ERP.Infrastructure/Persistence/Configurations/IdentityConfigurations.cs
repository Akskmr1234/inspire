using ERP.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Permission"/> to the <c>permissions</c> table.</summary>
public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Module).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Resource).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Verb).HasConversion<int>().IsRequired();
        builder.Property(p => p.Code).HasMaxLength(160).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(256);

        // The catalogue is global, not tenant-scoped, so the code is globally
        // unique and authorisation can resolve a permission with one indexed
        // lookup.
        builder.HasIndex(p => p.Code).IsUnique().HasDatabaseName("ix_permissions_code");
        builder.HasIndex(p => p.Module).HasDatabaseName("ix_permissions_module");
    }
}

/// <summary>Maps <see cref="Role"/> to the <c>roles</c> table.</summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(256);
        builder.Property(r => r.IsSystemRole).IsRequired();
        builder.Property(r => r.GrantsAllPermissions).IsRequired();
        builder.Property(r => r.IsDeleted).IsRequired();

        builder
            .HasIndex(r => new { r.TenantId, r.Name })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_roles_tenant_name");

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Permissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_permissions");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="RolePermission"/> to the <c>role_permissions</c> table.</summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("role_permissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rp => rp.TenantId).HasDatabaseName("ix_role_permissions_tenant");
    }
}

/// <summary>Maps <see cref="User"/> to the <c>users</c> table.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.UserName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.MobileNumber).HasMaxLength(32);
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.TotpSecret).HasMaxLength(128);
        builder.Property(u => u.MfaMethod).HasConversion<int>().IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.MustChangePassword).IsRequired();
        builder.Property(u => u.IsDeleted).IsRequired();

        // Sign-in names and email addresses are unique per tenant, not globally:
        // two unrelated customers may each employ an "admin" or a "j.smith".
        builder
            .HasIndex(u => new { u.TenantId, u.UserName })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_users_tenant_username");

        builder
            .HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_users_tenant_email");

        builder.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.Roles)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_roles");

        builder.HasMany(u => u.FirmAccess)
            .WithOne()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.FirmAccess)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_firmAccess");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="UserRole"/> to the <c>user_roles</c> table.</summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_roles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ur => ur.TenantId).HasDatabaseName("ix_user_roles_tenant");
    }
}

/// <summary>Maps <see cref="UserFirmAccess"/> to the <c>user_firm_access</c> table.</summary>
public sealed class UserFirmAccessConfiguration : IEntityTypeConfiguration<UserFirmAccess>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserFirmAccess> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_firm_access");

        // A surrogate key rather than a composite one, because BranchId is
        // nullable and PostgreSQL treats NULLs in a primary key as impermissible.
        builder.HasKey("UserId", "FirmId", "BranchId");

        builder.HasIndex(a => a.TenantId).HasDatabaseName("ix_user_firm_access_tenant");
    }
}

/// <summary>Maps <see cref="RefreshToken"/> to the <c>refresh_tokens</c> table.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.RevocationReason).HasConversion<int>().IsRequired();
        builder.Property(t => t.UserAgent).HasMaxLength(512);
        builder.Property(t => t.IpAddress).HasMaxLength(64);

        // Every refresh looks the token up by its hash, so this is the hot path.
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_hash");

        // Reuse detection revokes an entire family at once, which needs the family
        // to be indexed rather than scanned.
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family");

        builder.HasIndex(t => new { t.UserId, t.RevokedAtUtc })
            .HasDatabaseName("ix_refresh_tokens_user_active");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

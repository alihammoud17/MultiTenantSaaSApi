using Domain.Entites;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly ITenantContext? _tenantContext;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext? tenantContext = null)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public ApplicationDbContext()
            : base()
        {
            _tenantContext = null;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddUserSecrets<ApplicationDbContext>()  // Add User Secrets!
                    .AddEnvironmentVariables()
                    .Build();

                var connectionString = configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException(
                        "Connection string 'DefaultConnection' not found. " +
                        "Set it via User Secrets: dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"your-connection-string\"");
                }

                optionsBuilder.UseNpgsql(connectionString);
            }
        }

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Plan> Plans => Set<Plan>();
        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<BillingEventInbox> BillingEventInboxes => Set<BillingEventInbox>();
        //public DbSet<Product> Products => Set<Product>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<EntitlementDefinition> EntitlementDefinitions => Set<EntitlementDefinition>();
        public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();
        public DbSet<AddOnDefinition> AddOnDefinitions => Set<AddOnDefinition>();
        public DbSet<AddOnEntitlement> AddOnEntitlements => Set<AddOnEntitlement>();
        public DbSet<TenantAddOnAssignment> TenantAddOnAssignments => Set<TenantAddOnAssignment>();
        public DbSet<TenantEntitlementOverride> TenantEntitlementOverrides => Set<TenantEntitlementOverride>();
        public DbSet<UserInvite> UserInvites => Set<UserInvite>();
        public DbSet<UserVerificationToken> UserVerificationTokens => Set<UserVerificationToken>();
        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
        public DbSet<UserMfaEnrollmentChallenge> UserMfaEnrollmentChallenges => Set<UserMfaEnrollmentChallenge>();
        public DbSet<UserStepUpSession> UserStepUpSessions => Set<UserStepUpSession>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Tenant
            builder.Entity<Tenant>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Subdomain).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Subdomain).IsRequired().HasMaxLength(50);
            });

            // User
            builder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(32);
                entity.Property(e => e.MfaSecret).HasMaxLength(128);
                entity.HasOne(e => e.Tenant)
                      .WithMany(t => t.Users)
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<UserMfaEnrollmentChallenge>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.UserId, e.ExpiresAt });
                entity.HasIndex(e => e.EnrollmentTokenHash).IsUnique();
                entity.Property(e => e.EnrollmentTokenHash).IsRequired().HasMaxLength(128);
                entity.Property(e => e.Secret).IsRequired().HasMaxLength(128);
            });

            builder.Entity<UserStepUpSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.UserId, e.Purpose, e.ExpiresAt });
                entity.HasIndex(e => e.SessionTokenHash).IsUnique();
                entity.Property(e => e.Purpose).IsRequired().HasMaxLength(64);
                entity.Property(e => e.SessionTokenHash).IsRequired().HasMaxLength(128);
            });

            builder.Entity<UserInvite>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.Email, e.AcceptedAt });
                entity.HasIndex(e => e.TokenHash).IsUnique();
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(32);
                entity.Property(e => e.RbacRoleName).HasMaxLength(100);
                entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
                entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<UserVerificationToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TokenHash).IsUnique();
                entity.HasIndex(e => new { e.TenantId, e.UserId, e.UsedAt });
                entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
                entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TokenHash).IsUnique();
                entity.HasIndex(e => new { e.TenantId, e.UserId, e.UsedAt });
                entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
                entity.Property(e => e.RequestedByIp).HasMaxLength(64);
                entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            // Plan
            builder.Entity<Plan>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            });

            // Subscription
            builder.Entity<Subscription>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TenantId).IsUnique(); // One subscription per tenant
                entity.HasOne(e => e.Tenant)
                      .WithOne(t => t.Subscription)
                      .HasForeignKey<Subscription>(e => e.TenantId);
                entity.HasOne(e => e.Plan)
                      .WithMany()
                      .HasForeignKey(e => e.PlanId);
                entity.Property(e => e.ScheduledPlanId).HasMaxLength(64);
            });

            builder.Entity<BillingEventInbox>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.EventId).IsUnique();
                entity.HasIndex(e => new { e.TenantId, e.SubscriptionId });
                entity.Property(e => e.EventId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ContractVersion).IsRequired().HasMaxLength(32);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(80);
                entity.Property(e => e.Provider).IsRequired().HasMaxLength(40);
                entity.Property(e => e.ProviderEventId).IsRequired().HasMaxLength(120);
                entity.Property(e => e.CorrelationId).IsRequired().HasMaxLength(120);
                entity.Property(e => e.TargetPlanId).HasMaxLength(64);
            });

            builder.Entity<EntitlementDefinition>(entity =>
            {
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).HasMaxLength(160);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Description).HasMaxLength(512);
                entity.Property(e => e.DefaultValue).HasMaxLength(1024);
            });

            builder.Entity<PlanEntitlement>(entity =>
            {
                entity.HasKey(e => new { e.PlanId, e.EntitlementKey });
                entity.Property(e => e.EntitlementKey).HasMaxLength(160);
                entity.Property(e => e.Value).IsRequired().HasMaxLength(1024);
                entity.HasOne(e => e.Plan).WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.EntitlementDefinition).WithMany(e => e.PlanEntitlements).HasForeignKey(e => e.EntitlementKey).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<AddOnDefinition>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(120);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Description).HasMaxLength(512);
                entity.Property(e => e.BillingProviderProductRef).HasMaxLength(200);
            });

            builder.Entity<AddOnEntitlement>(entity =>
            {
                entity.HasKey(e => new { e.AddOnId, e.EntitlementKey });
                entity.Property(e => e.EntitlementKey).HasMaxLength(160);
                entity.Property(e => e.Value).IsRequired().HasMaxLength(1024);
                entity.HasOne(e => e.AddOn).WithMany(e => e.Entitlements).HasForeignKey(e => e.AddOnId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.EntitlementDefinition).WithMany().HasForeignKey(e => e.EntitlementKey).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TenantAddOnAssignment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.Status, e.EffectiveFromUtc });
                entity.HasIndex(e => new { e.TenantId, e.AddOnId, e.Status });
                entity.Property(e => e.AddOnId).IsRequired().HasMaxLength(120);
                entity.Property(e => e.ExternalReference).HasMaxLength(200);
                entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.AddOn).WithMany().HasForeignKey(e => e.AddOnId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TenantEntitlementOverride>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.EntitlementKey, e.EffectiveFromUtc });
                entity.Property(e => e.EntitlementKey).HasMaxLength(160);
                entity.Property(e => e.Value).IsRequired().HasMaxLength(1024);
                entity.Property(e => e.Reason).IsRequired().HasMaxLength(300);
                entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.EntitlementDefinition).WithMany().HasForeignKey(e => e.EntitlementKey).OnDelete(DeleteBehavior.Cascade);
            });



            // Role
            builder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(256);

                entity.HasOne(e => e.Tenant)
                    .WithMany(t => t.Roles)
                    .HasForeignKey(e => e.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                if (_tenantContext != null)
                {
                    entity.HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
                }
            });

            // Permission
            builder.Entity<Permission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Name).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Description).HasMaxLength(256);
            });

            // UserRole
            builder.Entity<UserRole>(entity =>
            {
                entity.HasKey(e => new { e.TenantId, e.UserId, e.RoleId });
                entity.HasIndex(e => e.RoleId);

                entity.HasOne(e => e.Tenant)
                    .WithMany(t => t.UserRoles)
                    .HasForeignKey(e => e.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);

                if (_tenantContext != null)
                {
                    entity.HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
                }
            });

            // RolePermission
            builder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(e => new { e.RoleId, e.PermissionId });

                entity.HasOne(e => e.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Permission)
                    .WithMany(p => p.RolePermissions)
                    .HasForeignKey(e => e.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // RefreshToken
            builder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TokenHash).IsUnique();
                entity.HasIndex(e => new { e.TenantId, e.UserId, e.ExpiresAt });
                entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
                entity.Property(e => e.CreatedByIp).HasMaxLength(64);
                entity.Property(e => e.RevokedByIp).HasMaxLength(64);
                entity.Property(e => e.RevocationReason).HasMaxLength(200);

                entity.HasOne(e => e.Tenant)
                    .WithMany(t => t.RefreshTokens)
                    .HasForeignKey(e => e.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                if (_tenantContext != null)
                {
                    entity.HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
                }
            });

            // Product (CRITICAL: Global query filter for tenant isolation)
            //builder.Entity<Product>(entity =>
            //{
            //    entity.HasKey(e => e.Id);
            //    entity.HasIndex(e => new { e.TenantId, e.Id }); // Composite index for performance
            //    entity.Property(e => e.Name).IsRequired().HasMaxLength(200);

            //    // TENANT ISOLATION: Automatically filter by tenant
            //    entity.HasQueryFilter(e =>
            //        _tenantContext == null || e.TenantId == _tenantContext.TenantId);
            //});

            // AuditLog
            if (_tenantContext != null)
                builder.Entity<AuditLog>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasIndex(e => new { e.TenantId, e.Timestamp });
                    entity.HasIndex(e => e.UserId);

                    // Also filtered by tenant
                    entity.HasQueryFilter(e =>
                        _tenantContext == null || e.TenantId == _tenantContext.TenantId);
                });

            // Seed Plans
            builder.Entity<Plan>().HasData(
                new Plan
                {
                    Id = "plan-free",
                    Name = "Free",
                    MonthlyPrice = 0,
                    ApiCallsPerMonth = 1000,
                    MaxUsers = 1,
                    DisplayOrder = 1
                },
                new Plan
                {
                    Id = "plan-pro",
                    Name = "Pro",
                    MonthlyPrice = 99,
                    ApiCallsPerMonth = 50000,
                    MaxUsers = 10,
                    DisplayOrder = 2
                }
            );

            // Seed Permissions
            builder.Entity<Permission>().HasData(
                new Permission { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "tenants.read", Description = "View tenant data" },
                new Permission { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "tenants.manage", Description = "Manage tenant settings" },
                new Permission { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "users.read", Description = "View tenant users" },
                new Permission { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "users.manage", Description = "Manage tenant users" },
                new Permission { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Name = "billing.manage", Description = "Manage billing" },
                new Permission { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), Name = "auditlogs.read", Description = "View tenant audit logs" }
            );

            builder.Entity<EntitlementDefinition>().HasData(
                new EntitlementDefinition
                {
                    Key = "feature.billing.invoices.read",
                    DisplayName = "Billing invoice list access",
                    Description = "Allows tenant users to read tenant-scoped billing invoice summaries.",
                    ValueType = EntitlementValueType.Boolean,
                    Category = EntitlementCategory.Feature,
                    IsActive = true,
                    DefaultValue = "false",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new EntitlementDefinition
                {
                    Key = "feature.billing.subscription.manage",
                    DisplayName = "Billing subscription self-service management",
                    Description = "Allows cancellation and reactivation actions for tenant subscriptions.",
                    ValueType = EntitlementValueType.Boolean,
                    Category = EntitlementCategory.Feature,
                    IsActive = true,
                    DefaultValue = "false",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new EntitlementDefinition
                {
                    Key = "feature.billing.plan.upgrade",
                    DisplayName = "Billing plan upgrade flow",
                    Description = "Allows tenant billing managers to perform plan upgrades.",
                    ValueType = EntitlementValueType.Boolean,
                    Category = EntitlementCategory.Feature,
                    IsActive = true,
                    DefaultValue = "false",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new EntitlementDefinition
                {
                    Key = "feature.analytics.audit_logs.read",
                    DisplayName = "Tenant analytics and audit visibility",
                    Description = "Allows tenant users to access analytics-oriented audit log read surfaces.",
                    ValueType = EntitlementValueType.Boolean,
                    Category = EntitlementCategory.Feature,
                    IsActive = true,
                    DefaultValue = "false",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new EntitlementDefinition
                {
                    Key = "feature.admin.advanced.user_management",
                    DisplayName = "Advanced tenant user management",
                    Description = "Allows privileged tenant administrators to access advanced user management capabilities.",
                    ValueType = EntitlementValueType.Boolean,
                    Category = EntitlementCategory.Feature,
                    IsActive = true,
                    DefaultValue = "false",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new EntitlementDefinition
                {
                    Key = "feature.modules.future.hooks",
                    DisplayName = "Future module gating hook",
                    Description = "Reserved module-level gate to support progressive rollout for future protected modules.",
                    ValueType = EntitlementValueType.Boolean,
                    Category = EntitlementCategory.Feature,
                    IsActive = true,
                    DefaultValue = "false",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new EntitlementDefinition
                {
                    Key = "quota.api.calls.monthly",
                    DisplayName = "Monthly API call quota",
                    Description = "Plan-level monthly API call quota baseline.",
                    ValueType = EntitlementValueType.Integer,
                    Category = EntitlementCategory.Quota,
                    IsActive = true,
                    DefaultValue = "0",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new EntitlementDefinition
                {
                    Key = "quota.users.max",
                    DisplayName = "Maximum tenant users",
                    Description = "Plan-level max allowed tenant users.",
                    ValueType = EntitlementValueType.Integer,
                    Category = EntitlementCategory.Quota,
                    IsActive = true,
                    DefaultValue = "1",
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                });

            builder.Entity<PlanEntitlement>().HasData(
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "feature.billing.invoices.read",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "feature.billing.invoices.read",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "feature.billing.subscription.manage",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "feature.billing.subscription.manage",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "feature.billing.plan.upgrade",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "feature.billing.plan.upgrade",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "feature.analytics.audit_logs.read",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "feature.analytics.audit_logs.read",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "feature.admin.advanced.user_management",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "feature.admin.advanced.user_management",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "feature.modules.future.hooks",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "feature.modules.future.hooks",
                    Value = "true",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "quota.api.calls.monthly",
                    Value = "1000",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "quota.api.calls.monthly",
                    Value = "50000",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-free",
                    EntitlementKey = "quota.users.max",
                    Value = "1",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                },
                new PlanEntitlement
                {
                    PlanId = "plan-pro",
                    EntitlementKey = "quota.users.max",
                    Value = "10",
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
                });
        }
    }
}

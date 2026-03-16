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
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();

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
                entity.HasOne(e => e.Tenant)
                      .WithMany(t => t.Users)
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Role
            builder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(500);

                entity.HasOne(e => e.Tenant)
                      .WithMany(t => t.Roles)
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Permission
            builder.Entity<Permission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.Property(e => e.Code).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(500);
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

            // UserRole
            builder.Entity<UserRole>(entity =>
            {
                entity.HasKey(e => new { e.TenantId, e.UserId, e.RoleId });
                entity.HasIndex(e => new { e.TenantId, e.RoleId });

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

            builder.Entity<Permission>().HasData(
                PermissionCodes.All.Select(code => new Permission
                {
                    Id = PermissionCodes.SeedIds[code],
                    Code = code,
                    Description = code,
                    CreatedAt = DateTime.UnixEpoch
                })
            );
        }
    }
}

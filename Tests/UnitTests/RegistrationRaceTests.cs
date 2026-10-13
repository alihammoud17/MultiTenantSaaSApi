using System.Data.Common;
using Application.Services;
using Domain.DTOs;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Outputs;
using Domain.Responses;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Integration;

namespace Tests.UnitTests;

/// <summary>
/// Two registrations for the same subdomain or email can both pass the pre-insert checks; the database unique index
/// then rejects the loser. These tests make that happen deterministically: an interceptor commits a competing
/// registration through a second connection after the checks and before the registration's transaction begins.
/// A real database is needed (shared-cache SQLite), because the EF in-memory provider enforces no unique indexes.
/// </summary>
public sealed class RegistrationRaceTests : IDisposable
{
    private const string Password = "Passw0rd!";
    private readonly string _connectionString = $"Data Source=file:registration-race-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;

    public RegistrationRaceTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        using var db = CreateContext(_keepAlive);
        db.Database.EnsureCreated(); // seeds plan-free, which registration subscribes to
    }

    [Fact]
    public async Task LosingTheSubdomainRace_ShouldReturnSubdomainAlreadyTaken_AndPersistNothing()
    {
        var subdomain = $"race-{Guid.NewGuid():N}";
        var email = $"loser-{Guid.NewGuid():N}@example.com";
        var outcome = await RegisterWithCompetitorAsync(
            new RegisterTenantRequest("Loser Co", subdomain, email, Password),
            competitorSubdomain: subdomain,
            competitorEmail: $"winner-{Guid.NewGuid():N}@example.com");

        outcome.Result.Error.Should().Be(AuthFlowError.SubdomainAlreadyTaken);

        using var db = CreateContext(_keepAlive);
        (await db.Tenants.CountAsync(t => t.Subdomain == subdomain)).Should().Be(1);
        (await db.Users.AnyAsync(u => u.Email == email)).Should().BeFalse();
    }

    [Fact]
    public async Task LosingTheEmailRace_ShouldAnswerLikeAnExistingAccount_AndPersistNothing()
    {
        var subdomain = $"race-{Guid.NewGuid():N}";
        var email = $"race-{Guid.NewGuid():N}@example.com";
        var noticesBefore = CapturingIdentityNotificationService.GetExistingAccountNoticeCount(email);

        var outcome = await RegisterWithCompetitorAsync(
            new RegisterTenantRequest("Loser Co", subdomain, email.ToUpperInvariant(), Password),
            competitorSubdomain: $"winner-{Guid.NewGuid():N}",
            competitorEmail: email);

        outcome.Result.Succeeded.Should().BeTrue();
        outcome.Result.Error.Should().Be(AuthFlowError.None);
        outcome.Result.Response!.Status.Should().Be(RegistrationPendingResponse.PendingVerificationStatus);
        CapturingIdentityNotificationService.GetExistingAccountNoticeCount(email).Should().Be(noticesBefore + 1);

        using var db = CreateContext(_keepAlive);
        (await db.Tenants.AnyAsync(t => t.Id == outcome.Result.Response.TenantId || t.Subdomain == subdomain)).Should().BeFalse();
        (await db.Users.CountAsync(u => u.Email == email)).Should().Be(1);
    }

    [Fact]
    public async Task InsertFailureThatIsNotAConflict_ShouldStillReturnRegistrationFailed()
    {
        var subdomain = $"broken-{Guid.NewGuid():N}";
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var db = CreateContext(connection, new FailingSaveInterceptor());
        var sut = CreateService(db);

        var result = await sut.RegisterAsync(new RegisterTenantRequest("Broken Co", subdomain, $"broken-{Guid.NewGuid():N}@example.com", Password), null);

        result.Error.Should().Be(AuthFlowError.RegistrationFailed);
        using var check = CreateContext(_keepAlive);
        (await check.Tenants.AnyAsync(t => t.Subdomain == subdomain)).Should().BeFalse();
    }

    private async Task<(RegisterAuthResult Result, CompetingRegistrationInterceptor Interceptor)> RegisterWithCompetitorAsync(
        RegisterTenantRequest request,
        string competitorSubdomain,
        string competitorEmail)
    {
        var interceptor = new CompetingRegistrationInterceptor(_connectionString, competitorSubdomain, competitorEmail);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var db = CreateContext(connection, interceptor);
        var sut = CreateService(db);

        var result = await sut.RegisterAsync(request, null);

        interceptor.Fired.Should().BeTrue("the competing registration must be committed before the insert");
        return (result, interceptor);
    }

    private static AuthOrchestrationService CreateService(ApplicationDbContext db)
    {
        var tenantContext = new StubTenantContext();
        return new AuthOrchestrationService(
            db,
            jwtService: null!,
            refreshTokenService: null!,
            new AuditService(db, tenantContext, new HttpContextAccessor()),
            tenantContext,
            mfaService: null!,
            new CapturingIdentityNotificationService(),
            NullLogger<AuthOrchestrationService>.Instance);
    }

    private static ApplicationDbContext CreateContext(DbConnection connection, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options;
        return new ApplicationDbContext(options, new StubTenantContext());
    }

    public void Dispose() => _keepAlive.Dispose();

    // Commits another tenant + admin through a separate connection the first time the registration starts its
    // transaction, i.e. after its duplicate checks have passed.
    private sealed class CompetingRegistrationInterceptor(string connectionString, string subdomain, string email)
        : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var competitorConnection = new SqliteConnection(connectionString);
                await competitorConnection.OpenAsync(cancellationToken);
                await using var competitor = CreateContext(competitorConnection);
                var now = DateTime.UtcNow;
                var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Winner Co", Subdomain = subdomain, Status = TenantStatus.PendingVerification, CreatedAt = now, UpdatedAt = now };
                competitor.Tenants.Add(tenant);
                competitor.Users.Add(new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = email, PasswordHash = "x", Role = "ADMIN", CreatedAt = now });
                await competitor.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    // Simulates an insert failure unrelated to a duplicate (e.g. a different constraint or a provider error).
    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("Simulated failure that is not a unique-index conflict.");
        }
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public void SetTenantId(Guid tenantId) => TenantId = tenantId;
    }
}

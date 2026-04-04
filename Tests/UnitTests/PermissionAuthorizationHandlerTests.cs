using Domain.Authorization;
using Domain.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Presentation.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Tests.UnitTests;

public class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task HandleRequirementAsync_ShouldSucceed_WhenNameIdentifierClaimIsPresent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requirement = new PermissionRequirement(RbacPermissions.AuditLogsRead);
        var principal = BuildPrincipal(new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("tenant_id", tenantId.ToString()));
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);

        var sut = new PermissionAuthorizationHandler(new StubRbacAuthorizationService(true));

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldSucceed_WhenSubClaimIsPresent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requirement = new PermissionRequirement(RbacPermissions.BillingManage);
        var principal = BuildPrincipal(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim("tenant_id", tenantId.ToString()));
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);

        var sut = new PermissionAuthorizationHandler(new StubRbacAuthorizationService(true));

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldFail_WhenTenantClaimMissing()
    {
        var requirement = new PermissionRequirement(RbacPermissions.UsersManage);
        var principal = BuildPrincipal(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);

        var sut = new PermissionAuthorizationHandler(new StubRbacAuthorizationService(true));

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldFail_WhenRbacServiceDeniesPermission()
    {
        var requirement = new PermissionRequirement(RbacPermissions.UsersManage);
        var principal = BuildPrincipal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()));
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);

        var sut = new PermissionAuthorizationHandler(new StubRbacAuthorizationService(false));

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private class StubRbacAuthorizationService : IRbacAuthorizationService
    {
        private readonly bool _hasPermission;

        public StubRbacAuthorizationService(bool hasPermission)
        {
            _hasPermission = hasPermission;
        }

        public Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, string permission, CancellationToken cancellationToken = default)
            => Task.FromResult(_hasPermission);
    }
}

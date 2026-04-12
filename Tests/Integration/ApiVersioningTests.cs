using System.Net;
using FluentAssertions;

namespace Tests.Integration;

public class ApiVersioningTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ApiVersioningTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task VersionedPlansRoute_ShouldResolveUnderV1()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnversionedPlansRoute_ShouldNotResolve()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/plans");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SwaggerDocument_ShouldExposeV1Spec()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

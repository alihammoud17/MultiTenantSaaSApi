using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tests.Integration;

public class CorsPolicyTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string AllowedOrigin = "https://app.example.test";
    private const string OtherAllowedOrigin = "https://admin.example.test";
    private const string DisallowedOrigin = "https://evil.example.test";

    private static readonly string[] CorsResponseHeaders =
    [
        "Access-Control-Allow-Origin",
        "Access-Control-Allow-Credentials",
        "Access-Control-Allow-Methods",
        "Access-Control-Allow-Headers",
        "Access-Control-Expose-Headers"
    ];

    private readonly ApiWebApplicationFactory _factory;

    public CorsPolicyTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DisallowedOrigin_ShouldReceiveNoCorsHeaders_OnSimpleAndPreflightRequests()
    {
        using var factory = WithAllowedOrigins($"{AllowedOrigin},{OtherAllowedOrigin}");
        using var client = CreateHttpsClient(factory);

        using var simple = await client.SendAsync(CreateSimpleRequest(DisallowedOrigin));
        using var preflight = await client.SendAsync(CreatePreflightRequest(DisallowedOrigin));

        simple.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldHaveNoCorsHeaders(simple);
        ShouldHaveNoCorsHeaders(preflight);
    }

    [Theory]
    [InlineData("https://app.example.test.evil.test")]
    [InlineData("http://app.example.test")]
    [InlineData("https://app.example.test:8443")]
    [InlineData("null")]
    public async Task OriginsThatOnlyResembleAnAllowedOrigin_ShouldReceiveNoCorsHeaders(string origin)
    {
        using var factory = WithAllowedOrigins(AllowedOrigin);
        using var client = CreateHttpsClient(factory);

        using var simple = await client.SendAsync(CreateSimpleRequest(origin));
        using var preflight = await client.SendAsync(CreatePreflightRequest(origin));

        ShouldHaveNoCorsHeaders(simple);
        ShouldHaveNoCorsHeaders(preflight);
    }

    [Fact]
    public async Task NoConfiguredOrigins_ShouldGiveEveryOriginNoCorsHeaders_OutsideDevelopment()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        using var simple = await client.SendAsync(CreateSimpleRequest(AllowedOrigin));
        using var preflight = await client.SendAsync(CreatePreflightRequest(AllowedOrigin));

        ShouldHaveNoCorsHeaders(simple);
        ShouldHaveNoCorsHeaders(preflight);
    }

    [Theory]
    [InlineData(AllowedOrigin)]
    [InlineData(OtherAllowedOrigin)]
    public async Task ListedOrigin_ShouldBeEchoedWithCredentials(string origin)
    {
        using var factory = WithAllowedOrigins($"{AllowedOrigin}; {OtherAllowedOrigin}/");
        using var client = CreateHttpsClient(factory);

        using var simple = await client.SendAsync(CreateSimpleRequest(origin));
        using var preflight = await client.SendAsync(CreatePreflightRequest(origin));

        foreach (var response in new[] { simple, preflight })
        {
            response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
            response.Headers.GetValues("Access-Control-Allow-Credentials").Should().Equal("true");
        }

        preflight.Headers.GetValues("Access-Control-Allow-Methods").Should().Equal("DELETE");
        preflight.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().ContainEquivalentOf("authorization");
    }

    [Fact]
    public async Task IndexedOriginList_ShouldBeAccepted()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                    ["Cors:AllowedOrigins:1"] = OtherAllowedOrigin
                })));
        using var client = CreateHttpsClient(factory);

        using var allowed = await client.SendAsync(CreateSimpleRequest(OtherAllowedOrigin));
        using var disallowed = await client.SendAsync(CreateSimpleRequest(DisallowedOrigin));

        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(OtherAllowedOrigin);
        ShouldHaveNoCorsHeaders(disallowed);
    }

    [Fact]
    public async Task Development_ShouldAllowAnyOrigin_WithoutCredentials()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = CreateHttpsClient(factory);

        using var simple = await client.SendAsync(CreateSimpleRequest(DisallowedOrigin));

        simple.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("*");
        simple.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public void WildcardOrigin_ShouldFailStartup()
    {
        using var factory = WithAllowedOrigins("*");

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*Cors:AllowedOrigins*");
    }

    private WebApplicationFactory<Program> WithAllowedOrigins(string allowedOrigins) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins"] = allowedOrigins })));

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static HttpRequestMessage CreateSimpleRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static HttpRequestMessage CreatePreflightRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/users/00000000-0000-0000-0000-000000000001");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "DELETE");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    private static void ShouldHaveNoCorsHeaders(HttpResponseMessage response)
    {
        foreach (var header in CorsResponseHeaders)
        {
            response.Headers.Contains(header).Should().BeFalse($"{header} must not be sent to a disallowed origin");
        }
    }
}

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Presentation.Cors;

namespace Tests.UnitTests;

public class ApiCorsPolicyTests
{
    [Fact]
    public void ReadAllowedOrigins_ShouldSplitASingleValue_AndNormalizeEachOrigin()
    {
        var configuration = Build(new() { ["Cors:AllowedOrigins"] = " https://App.Example.com/ ,https://admin.example.com;http://localhost:5173\nhttps://app.example.com" });

        ApiCorsPolicy.ReadAllowedOrigins(configuration).Should().Equal(
            "https://app.example.com", "https://admin.example.com", "http://localhost:5173");
    }

    [Fact]
    public void ReadAllowedOrigins_ShouldReadAnIndexedList()
    {
        var configuration = Build(new()
        {
            ["Cors:AllowedOrigins:0"] = "https://app.example.com",
            ["Cors:AllowedOrigins:1"] = "https://admin.example.com:8443"
        });

        ApiCorsPolicy.ReadAllowedOrigins(configuration).Should().Equal(
            "https://app.example.com", "https://admin.example.com:8443");
    }

    [Fact]
    public void ReadAllowedOrigins_ShouldReturnEmpty_WhenUnsetOrBlank()
    {
        ApiCorsPolicy.ReadAllowedOrigins(Build(new())).Should().BeEmpty();
        ApiCorsPolicy.ReadAllowedOrigins(Build(new() { ["Cors:AllowedOrigins"] = "  " })).Should().BeEmpty();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("app.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com?x=1")]
    [InlineData("https://user@app.example.com")]
    public void ReadAllowedOrigins_ShouldRejectEntriesThatAreNotOrigins(string origin)
    {
        var configuration = Build(new() { ["Cors:AllowedOrigins"] = $"https://ok.example.com,{origin}" });

        var read = () => ApiCorsPolicy.ReadAllowedOrigins(configuration);

        read.Should().Throw<InvalidOperationException>().WithMessage("*Cors:AllowedOrigins*");
    }

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

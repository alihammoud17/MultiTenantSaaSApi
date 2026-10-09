using Application.Services;
using Domain.DTOs;
using FluentAssertions;

namespace Tests.UnitTests;

public class TenantRegistrationValidatorTests
{
    private const string ValidPassword = "Passw0rd!";

    [Fact]
    public void Validate_ShouldTrimAndLowercaseSubdomain_AndTrimCompanyNameAndEmail()
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("  Acme Corp  ", "  Acme-Corp ", "  Admin@Example.com ", ValidPassword));

        result.IsValid.Should().BeTrue();
        result.NormalizedRequest.Should().Be(
            new RegisterTenantRequest("Acme Corp", "acme-corp", "Admin@Example.com", ValidPassword));
    }

    [Theory]
    [InlineData("", "Company name is required")]
    [InlineData("   ", "Company name is required")]
    [InlineData(null, "Company name is required")]
    public void Validate_ShouldRejectMissingCompanyName(string? companyName, string expectedError)
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest(companyName!, "acme", "admin@example.com", ValidPassword));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(expectedError);
    }

    [Fact]
    public void Validate_ShouldRejectCompanyNameOverMaxLength()
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest(new string('a', 101), "acme", "admin@example.com", ValidPassword));

        result.Error.Should().Be("Company name must be at most 100 characters");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 51 characters
    public void Validate_ShouldRejectSubdomainOutsideLengthBounds(string subdomain)
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", subdomain, "admin@example.com", ValidPassword));

        result.Error.Should().Be("Subdomain must be between 3 and 50 characters");
    }

    [Theory]
    [InlineData("acme.corp")]
    [InlineData("acme_corp")]
    [InlineData("acme corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acmé")]
    public void Validate_ShouldRejectSubdomainWithInvalidCharacters(string subdomain)
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", subdomain, "admin@example.com", ValidPassword));

        result.IsValid.Should().BeFalse();
        result.Error.Should().StartWith("Subdomain may only contain");
    }

    [Fact]
    public void Validate_ShouldRejectAllNumericSubdomain()
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", "10203", "admin@example.com", ValidPassword));

        result.Error.Should().Be("Subdomain cannot be entirely numeric");
    }

    [Theory]
    [InlineData("www")]
    [InlineData("API")]
    [InlineData(" admin ")]
    [InlineData("localhost")]
    public void Validate_ShouldRejectReservedSubdomain_RegardlessOfCaseOrWhitespace(string subdomain)
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", subdomain, "admin@example.com", ValidPassword));

        result.Error.Should().Be("Subdomain is reserved");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Admin <admin@example.com>")]
    [InlineData("admin@")]
    public void Validate_ShouldRejectInvalidEmail(string email)
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", "acme", email, ValidPassword));

        result.Error.Should().Be("Admin email is invalid");
    }

    [Fact]
    public void Validate_ShouldRejectEmailOverMaxLength()
    {
        var email = new string('a', 250) + "@example.com";

        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", "acme", email, ValidPassword));

        result.Error.Should().Be("Admin email is invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("short12")]
    public void Validate_ShouldRejectShortPassword(string password)
    {
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", "acme", "admin@example.com", password));

        result.Error.Should().Be("Admin password must be at least 8 characters");
    }

    [Fact]
    public void Validate_ShouldRejectPasswordOverBcryptByteLimit()
    {
        // 37 two-byte characters = 74 UTF-8 bytes, over BCrypt's 72-byte input limit.
        var result = TenantRegistrationValidator.Validate(
            new RegisterTenantRequest("Acme", "acme", "admin@example.com", new string('é', 37)));

        result.Error.Should().Be("Admin password must be at most 72 bytes");
    }
}

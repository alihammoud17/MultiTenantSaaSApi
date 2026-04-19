using Domain.Outputs;
using FluentAssertions;

namespace Tests.UnitTests.Entitlements;

public static class EntitlementMatrixAssertions
{
    public static void AssertMatchesExpectation(EntitlementEvaluationResult actual, EntitlementMatrixCase expected)
    {
        actual.Value.Should().Be(expected.ExpectedResolvedValue, $"matrix case '{expected.Name}' should resolve deterministic value");
        actual.IsAllowed.Should().Be(expected.ExpectedAllowed, $"matrix case '{expected.Name}' should resolve deterministic allowance");
        actual.ResolvedFrom.Should().Be(expected.ExpectedResolvedFrom, $"matrix case '{expected.Name}' should keep precedence ordering stable");
    }
}

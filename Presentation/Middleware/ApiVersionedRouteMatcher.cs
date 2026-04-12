using System.Text.RegularExpressions;

namespace Presentation.Middleware;

internal static partial class ApiVersionedRouteMatcher
{
    [GeneratedRegex("^/api/v\\d+/auth(?:/|$)", RegexOptions.Compiled)]
    private static partial Regex AuthPathRegex();

    [GeneratedRegex("^/api/v\\d+/plans(?:/|$)", RegexOptions.Compiled)]
    private static partial Regex PlansPathRegex();

    public static bool IsAuthPath(string path) => AuthPathRegex().IsMatch(path);

    public static bool IsPlansPath(string path) => PlansPathRegex().IsMatch(path);
}

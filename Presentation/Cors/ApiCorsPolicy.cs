using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Presentation.Cors
{
    public static class ApiCorsPolicy
    {
        public const string PolicyName = "ApiCorsPolicy";
        public const string AllowedOriginsKey = "Cors:AllowedOrigins";

        private static readonly char[] OriginSeparators = [',', ';', ' ', '\t', '\r', '\n'];

        /// <summary>
        /// Registers <see cref="PolicyName"/>. Development allows any origin without credentials. Every other
        /// environment allows only the origins in <c>Cors:AllowedOrigins</c>, with credentials; with none
        /// configured, no cross-origin request receives CORS headers. Invalid origins fail startup.
        /// </summary>
        public static IServiceCollection AddApiCors(this IServiceCollection services)
        {
            services.AddCors();
            services.AddOptions<CorsOptions>()
                .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
                    options.AddPolicy(PolicyName, policy =>
                    {
                        if (environment.IsDevelopment())
                        {
                            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                            return;
                        }

                        policy
                            .WithOrigins(ReadAllowedOrigins(configuration))
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials();
                    }))
                .ValidateOnStart();

            return services;
        }

        /// <summary>
        /// Reads <c>Cors:AllowedOrigins</c> as either an indexed list (<c>Cors__AllowedOrigins__0=...</c>) or a single
        /// comma-, semicolon- or whitespace-separated value (<c>Cors__AllowedOrigins=https://a,https://b</c>).
        /// Each entry must be an absolute http(s) origin with no path, query or wildcard; startup fails otherwise.
        /// </summary>
        public static string[] ReadAllowedOrigins(IConfiguration configuration)
        {
            var section = configuration.GetSection(AllowedOriginsKey);
            var rawValues = (section.Get<string[]>() ?? []).Append(section.Value ?? string.Empty);

            return rawValues
                .SelectMany(value => value.Split(OriginSeparators, StringSplitOptions.RemoveEmptyEntries))
                .Select(NormalizeOrigin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string NormalizeOrigin(string origin)
        {
            if (origin.Contains('*')
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    $"{AllowedOriginsKey} contains '{origin}', which is not an http(s) origin such as https://app.example.com.");
            }

            return uri.GetLeftPart(UriPartial.Authority);
        }
    }
}

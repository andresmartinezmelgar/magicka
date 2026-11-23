using Arcana.Core;

namespace Arcana.Server;

/// <summary>Common HTTP limits, JSON contract and account services for both hosts.</summary>
public static class ArcanaWebServices
{
    public static void ConfigureArcanaHttp(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Protocol.MaxMessageBytes);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = Protocol.Json.PropertyNamingPolicy;
            options.SerializerOptions.MaxDepth = Protocol.Json.MaxDepth;
            foreach (var converter in Protocol.Json.Converters) options.SerializerOptions.Converters.Add(converter);
        });
    }

    public static void AddArcanaAccounts(this IServiceCollection services, AccountOptions options)
    {
        services.AddSingleton(new AccountService(options));
        services.AddPublicDashboard();
        services.AddHostedService<AccountDeletionService>();
        services.AddHostedService<StatisticsRetentionService>();
    }
}

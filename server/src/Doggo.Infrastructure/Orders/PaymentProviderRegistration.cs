using Doggo.Domain.Orders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Orders;

/// <summary>
/// Binds <see cref="IPaymentProvider"/> to the configured channel. The mock provider verifies
/// nothing and refunds unconditionally, so it is a development/test stand-in only: a production
/// host that still resolves to Mock fails fast at startup rather than running with an
/// unverifiable payment callback. Both the Api and the Worker use this so a refund sweep or a
/// callback endpoint can never run against the fake in production.
/// </summary>
public static class PaymentProviderRegistration
{
    public static IServiceCollection AddPaymentProvider(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<PaymentOptions>().Bind(configuration.GetSection(PaymentOptions.SectionName));

        var configured = configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>() ?? new PaymentOptions();
        if (configured.IsMockProvider && environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Payment:Provider is Mock in a production environment; configure a real payment channel before running.");
        }

        services.AddSingleton<IPaymentProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PaymentOptions>>().Value;
            return options.IsMockProvider
                ? new MockPaymentProvider()
                : throw new NotSupportedException($"Payment provider '{options.Provider}' is not supported.");
        });
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services;

namespace NovaTechCRM.Infrastructure.Payments;

public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IServiceProvider _services;

    public PaymentProviderFactory(IServiceProvider services) => _services = services;

    public IPaymentProvider GetProvider(PaymentProvider provider)
        => provider switch
        {
            PaymentProvider.Stripe     => _services.GetRequiredService<StripePaymentProvider>(),
            PaymentProvider.PayPal     => _services.GetRequiredService<PayPalPaymentProvider>(),
            PaymentProvider.Braintree  => _services.GetRequiredService<BraintreePaymentProvider>(),
            _ => throw new NotSupportedException($"Payment provider '{provider}' is not configured.")
        };
}

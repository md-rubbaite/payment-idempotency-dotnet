using System.Security.Cryptography;
using System.Text;
using Payments.Api.Domain;

namespace Payments.Api.Idempotency;

/// <summary>Stable hash of the business-relevant request fields, used to detect key reuse with a different body.</summary>
public static class RequestFingerprint
{
    public static string Compute(PaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = string.Join('\u001f', request.CustomerId, request.AmountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Currency.ToUpperInvariant(), request.Description);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

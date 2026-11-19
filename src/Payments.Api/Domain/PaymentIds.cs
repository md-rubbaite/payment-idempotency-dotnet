using System.Security.Cryptography;
using System.Text;

namespace Payments.Api.Domain;

/// <summary>Derives a stable, non-reversible payment id from the idempotency key.</summary>
public static class PaymentIds
{
    public static string FromKey(string idempotencyKey) =>
        "pay_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..16].ToLowerInvariant();
}

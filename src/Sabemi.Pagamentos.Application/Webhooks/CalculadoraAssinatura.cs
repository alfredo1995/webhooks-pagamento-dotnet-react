using System.Security.Cryptography;
using System.Text;

namespace Sabemi.Pagamentos.Application.Webhooks;

/// <summary>
/// Assinatura HMAC-SHA256 do corpo bruto da requisicao, no formato
/// <c>sha256=&lt;hex&gt;</c> — o mesmo padrao usado por GitHub e Stripe.
/// </summary>
/// <remarks>
/// A assinatura precisa ser calculada sobre os bytes exatos recebidos, nao sobre
/// o objeto desserializado e reserializado: qualquer diferenca de espacamento ou
/// ordem de chaves mudaria o hash. Por isso o corpo bruto e lido e preservado
/// antes de qualquer model binding.
/// </remarks>
public static class CalculadoraAssinatura
{
    public const string Prefixo = "sha256=";

    public static string Calcular(string corpo, string segredo)
    {
        ArgumentNullException.ThrowIfNull(corpo);
        ArgumentNullException.ThrowIfNull(segredo);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(segredo));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(corpo));

        return Prefixo + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Comparacao em tempo constante: nao vaza informacao por timing.</summary>
    public static bool Conferir(string corpo, string segredo, string? assinaturaRecebida)
    {
        if (string.IsNullOrWhiteSpace(assinaturaRecebida))
        {
            return false;
        }

        var esperada = Encoding.UTF8.GetBytes(Calcular(corpo, segredo));
        var recebida = Encoding.UTF8.GetBytes(assinaturaRecebida.Trim());

        return CryptographicOperations.FixedTimeEquals(esperada, recebida);
    }
}

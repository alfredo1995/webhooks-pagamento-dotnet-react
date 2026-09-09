using System.Globalization;
using System.Net.Mime;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Api.Webhooks;

namespace Sabemi.Pagamentos.Api.Seguranca;

/// <summary>
/// Limites de taxa da borda: webhook por parceiro e login por origem.
/// </summary>
/// <remarks>
/// <para>
/// No webhook a particao e a ApiKey recebida, lida do header antes de qualquer
/// validacao. Limitar por IP nao serviria — parceiro grande sai de varios
/// enderecos, e um unico NAT juntaria parceiros diferentes no mesmo balde.
/// Limitar depois de autenticar tambem nao: a conferencia do HMAC e justamente o
/// trabalho que uma rajada tornaria caro, entao o limite tem que vir antes dela.
/// </para>
/// <para>
/// Chave desconhecida cai em uma particao unica e apertada. Assim uma varredura
/// com chaves aleatorias nao ganha um balde novo a cada tentativa, que e como um
/// limitador por chave se torna inutil contra quem esta atacando.
/// </para>
/// <para>
/// O algoritmo do webhook e token bucket: o parceiro pode gastar o balde de uma
/// vez numa rajada legitima — reentrega acumulada do banco, por exemplo — e
/// depois volta ao ritmo de reposicao. Uma janela fixa recusaria essa rajada
/// mesmo com a media dentro do limite. Ja o login usa janela fixa por IP, porque
/// ali nao existe rajada legitima a preservar.
/// </para>
/// </remarks>
public static class RateLimitingWebhook
{
    public const string Politica = "webhook-por-parceiro";
    public const string PoliticaLogin = "login-por-origem";

    private const string ParticaoDesconhecida = "desconhecido";
    private const int LimiteDesconhecido = 10;
    private const int TentativasDeLoginPorMinuto = 10;

    public static IServiceCollection AddRateLimitingDeWebhook(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(limitador =>
        {
            limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limitador.AddPolicy(Politica, contexto =>
            {
                var opcoes = contexto.RequestServices.GetRequiredService<IOptions<OpcoesWebhook>>().Value;
                var apiKey = contexto.Request.Headers[AutenticacaoWebhookFilter.HeaderApiKey].ToString().Trim();

                var parceiro = opcoes.Parceiros.FirstOrDefault(p =>
                    string.Equals(p.ApiKey, apiKey, StringComparison.Ordinal)
                    || string.Equals(p.ApiKeyAnterior, apiKey, StringComparison.Ordinal));

                var particao = parceiro?.Nome ?? ParticaoDesconhecida;
                var limite = Math.Max(1, parceiro?.LimitePorMinuto ?? LimiteDesconhecido);

                return RateLimitPartition.GetTokenBucketLimiter(particao, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limite,
                    TokensPerPeriod = limite,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                });
            });

            limitador.AddPolicy(PoliticaLogin, contexto => RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? ParticaoDesconhecida,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = TentativasDeLoginPorMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));

            limitador.OnRejected = async (contexto, cancellationToken) =>
            {
                var espera = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var valor)
                    ? valor
                    : TimeSpan.FromMinutes(1);

                contexto.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                contexto.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                contexto.HttpContext.Response.ContentType = MediaTypeNames.Application.Json;

                await contexto.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Limite de requisicoes excedido.",
                        Detail = "Reduza a frequencia de envio ou aguarde o intervalo informado em Retry-After.",
                        Instance = contexto.HttpContext.Request.Path,
                    },
                    cancellationToken);
            };
        });

        return services;
    }
}

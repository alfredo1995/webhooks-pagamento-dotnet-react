namespace Sabemi.Pagamentos.Api.Webhooks;

public sealed class OpcoesWebhook
{
    public const string Secao = "Webhook";

    /// <summary>
    /// Quando falso, apenas a ApiKey e exigida. Existe para permitir teste manual
    /// rapido (curl/Swagger) sem calcular HMAC; em producao fica ligado.
    /// </summary>
    public bool ExigirAssinatura { get; set; } = true;

    public IList<ParceiroWebhook> Parceiros { get; init; } = [];
}

public sealed class ParceiroWebhook
{
    public string Nome { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Segredo compartilhado usado no HMAC. Em producao vem de cofre, nao do appsettings.</summary>
    public string Segredo { get; set; } = string.Empty;
}

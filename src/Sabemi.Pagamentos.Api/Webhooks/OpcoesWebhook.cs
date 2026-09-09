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

/// <summary>
/// Credenciais de um parceiro, com suporte a rotacao.
/// </summary>
/// <remarks>
/// <para>
/// Rotacionar um segredo compartilhado tem um problema de ordem: o parceiro nao
/// troca a chave no mesmo instante em que nos trocamos. Se so a chave nova
/// valesse, toda rotacao exigiria uma janela combinada de indisponibilidade — e
/// na pratica isso vira "ninguem rotaciona".
/// </para>
/// <para>
/// Por isso a chave anterior continua aceita ate
/// <see cref="ChavesAnterioresValidasAte"/>. A janela e curta e tem prazo no
/// proprio dado: sem a data, "aceitar as duas" viraria permanente e o ganho da
/// rotacao se perderia. Toda requisicao aceita pela chave antiga sai no log como
/// aviso, entao da para saber se o parceiro ja migrou antes de a janela fechar.
/// </para>
/// </remarks>
public sealed class ParceiroWebhook
{
    public string Nome { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Segredo compartilhado usado no HMAC. Em producao vem de cofre, nao do appsettings.</summary>
    public string Segredo { get; set; } = string.Empty;

    public string? ApiKeyAnterior { get; set; }

    public string? SegredoAnterior { get; set; }

    /// <summary>Fim da janela de rotacao, em UTC. Sem data, as chaves anteriores nao valem.</summary>
    public DateTime? ChavesAnterioresValidasAte { get; set; }

    /// <summary>Teto de requisicoes por minuto deste parceiro no endpoint de webhook.</summary>
    public int LimitePorMinuto { get; set; } = 120;

    public bool JanelaDeRotacaoAberta(DateTime agoraUtc)
    {
        if (ChavesAnterioresValidasAte is null)
        {
            return false;
        }

        // O binder de configuracao entrega a data no fuso local mesmo quando o
        // texto tem o sufixo Z. Sem normalizar, a janela fecharia com horas de
        // erro em qualquer servidor fora do UTC.
        var limite = ChavesAnterioresValidasAte.Value;
        var limiteUtc = limite.Kind == DateTimeKind.Utc ? limite : limite.ToUniversalTime();

        return agoraUtc <= limiteUtc;
    }
}

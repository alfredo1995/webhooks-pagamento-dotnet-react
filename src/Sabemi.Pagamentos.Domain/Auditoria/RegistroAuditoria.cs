using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Auditoria;

/// <summary>
/// Trilha de auditoria do painel: quem consultou o que, quando e de onde.
/// </summary>
/// <remarks>
/// O painel expoe valor de pagamento, contrato e payload bruto de terceiros.
/// Autenticar diz quem pode entrar; a trilha diz o que a pessoa olhou depois de
/// entrar — e so a segunda responde a pergunta que aparece quando um dado vaza.
/// Por isso o registro guarda tambem a query string: "consultou eventos" nao
/// diferencia quem abriu a lista de quem varreu o contrato de um cliente
/// especifico.
/// </remarks>
public class RegistroAuditoria : Entity, IAggregateRoot
{
    public const int TamanhoMaximoConsulta = 2000;

    /// <summary>Construtor exigido pelo EF Core.</summary>
    private RegistroAuditoria()
    {
        Usuario = null!;
        Papel = null!;
        Metodo = null!;
        Recurso = null!;
        IpOrigem = null!;
    }

    private RegistroAuditoria(
        string usuario,
        string papel,
        string metodo,
        string recurso,
        string? consulta,
        string ipOrigem,
        int statusHttp,
        string? traceId)
    {
        GerarIdentidade();

        Usuario = usuario;
        Papel = papel;
        Metodo = metodo;
        Recurso = recurso;
        Consulta = consulta;
        IpOrigem = ipOrigem;
        StatusHttp = statusHttp;
        TraceId = traceId;
        EmUtc = DateTime.UtcNow;
    }

    public string Usuario { get; private set; }

    public string Papel { get; private set; }

    public string Metodo { get; private set; }

    public string Recurso { get; private set; }

    /// <summary>Filtros usados na consulta. E o que distingue "olhou a lista" de "procurou um contrato".</summary>
    public string? Consulta { get; private set; }

    public string IpOrigem { get; private set; }

    public int StatusHttp { get; private set; }

    /// <summary>Amarra a linha da trilha ao trace do OpenTelemetry.</summary>
    public string? TraceId { get; private set; }

    public DateTime EmUtc { get; private set; }

    public static RegistroAuditoria Registrar(
        string? usuario,
        string? papel,
        string metodo,
        string recurso,
        string? consulta,
        string? ipOrigem,
        int statusHttp,
        string? traceId)
    {
        if (string.IsNullOrWhiteSpace(metodo) || string.IsNullOrWhiteSpace(recurso))
        {
            throw new DomainException("Registro de auditoria exige metodo e recurso.");
        }

        var filtros = string.IsNullOrWhiteSpace(consulta) ? null : consulta.Trim();
        if (filtros is not null && filtros.Length > TamanhoMaximoConsulta)
        {
            filtros = filtros[..TamanhoMaximoConsulta];
        }

        return new RegistroAuditoria(
            string.IsNullOrWhiteSpace(usuario) ? "anonimo" : usuario.Trim(),
            string.IsNullOrWhiteSpace(papel) ? "nenhum" : papel.Trim(),
            metodo.Trim().ToUpperInvariant(),
            recurso.Trim(),
            filtros,
            string.IsNullOrWhiteSpace(ipOrigem) ? "desconhecido" : ipOrigem.Trim(),
            statusHttp,
            traceId);
    }
}

using Sabemi.Pagamentos.Application.Common;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Domain.Auditoria;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.Application.Consultas;

public interface IConsultaService
{
    Task<PagedResult<EventoResumo>> BuscarEventosAsync(
        string? resultado,
        string? idContrato,
        string? idTransacao,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<EventoDetalhe> ObterEventoAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<ContratoResumo>> BuscarContratosAsync(
        string? idContrato,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<PagedResult<DeadLetterResumo>> BuscarDeadLettersAsync(
        bool apenasPendentes,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AuditoriaResumo>> BuscarAuditoriaAsync(
        string? usuario,
        string? recurso,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<Metricas> ObterMetricasAsync(CancellationToken cancellationToken = default);
}

public sealed class ConsultaService(
    IEventoWebhookRepository eventos,
    IStatusContratoRepository contratos,
    IDeadLetterRepository deadLetters,
    IOutboxRepository outbox,
    IAuditoriaRepository auditoria,
    IFilaProcessamento fila) : IConsultaService
{
    public async Task<PagedResult<EventoResumo>> BuscarEventosAsync(
        string? resultado,
        string? idContrato,
        string? idTransacao,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var (p, tamanho) = Paginacao.Normalizar(pagina, tamanhoPagina);

        var encontrados = await eventos.BuscarAsync(
            ConverterResultado(resultado),
            idContrato,
            idTransacao,
            p,
            tamanho,
            cancellationToken);

        return new PagedResult<EventoResumo>(
            encontrados.Itens.Select(e => e.ParaResumo()).ToList(),
            encontrados.Pagina,
            encontrados.TamanhoPagina,
            encontrados.TotalItens);
    }

    public async Task<EventoDetalhe> ObterEventoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterPorIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Evento", id);

        return new EventoDetalhe(evento.ParaResumo(), evento.PayloadBruto);
    }

    public async Task<PagedResult<ContratoResumo>> BuscarContratosAsync(
        string? idContrato,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var (p, tamanho) = Paginacao.Normalizar(pagina, tamanhoPagina);

        var resultado = await contratos.BuscarAsync(idContrato, p, tamanho, cancellationToken);

        return new PagedResult<ContratoResumo>(
            resultado.Itens.Select(c => c.ParaResumo()).ToList(),
            resultado.Pagina,
            resultado.TamanhoPagina,
            resultado.TotalItens);
    }

    public async Task<PagedResult<DeadLetterResumo>> BuscarDeadLettersAsync(
        bool apenasPendentes,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var (p, tamanho) = Paginacao.Normalizar(pagina, tamanhoPagina);

        var resultado = await deadLetters.BuscarAsync(apenasPendentes, p, tamanho, cancellationToken);

        return new PagedResult<DeadLetterResumo>(
            resultado.Itens.Select(c => c.ParaResumo()).ToList(),
            resultado.Pagina,
            resultado.TamanhoPagina,
            resultado.TotalItens);
    }

    public async Task<PagedResult<AuditoriaResumo>> BuscarAuditoriaAsync(
        string? usuario,
        string? recurso,
        int? pagina,
        int? tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var (p, tamanho) = Paginacao.Normalizar(pagina, tamanhoPagina);

        var resultado = await auditoria.BuscarAsync(usuario, recurso, p, tamanho, cancellationToken);

        return new PagedResult<AuditoriaResumo>(
            resultado.Itens.Select(r => r.ParaResumo()).ToList(),
            resultado.Pagina,
            resultado.TamanhoPagina,
            resultado.TotalItens);
    }

    public async Task<Metricas> ObterMetricasAsync(CancellationToken cancellationToken = default)
    {
        var porStatus = await eventos.ContarPorStatusAsync(cancellationToken);

        var total = porStatus.Values.Sum();
        var sucesso = Contar(porStatus, ResultadoEvento.Sucesso);
        var erro = Contar(porStatus, ResultadoEvento.Erro);
        var pendentes = Contar(porStatus, ResultadoEvento.Pendente);

        return new Metricas(
            total,
            pendentes,
            sucesso,
            erro,
            porStatus.TryGetValue(StatusProcessamento.AguardandoRetentativa, out var retentando) ? retentando : 0,
            fila.Aguardando,
            await outbox.ContarPendentesAsync(cancellationToken),
            await deadLetters.ContarPendentesAsync(cancellationToken),
            porStatus.ToDictionary(par => par.Key.ToString(), par => par.Value));
    }

    private static int Contar(IReadOnlyDictionary<StatusProcessamento, int> porStatus, ResultadoEvento resultado)
        => resultado.Membros().Sum(status => porStatus.TryGetValue(status, out var total) ? total : 0);

    /// <summary>Aceita tanto o agrupamento do painel quanto o status tecnico exato.</summary>
    private static ResultadoEvento? ConverterResultado(string? resultado)
    {
        if (string.IsNullOrWhiteSpace(resultado) || resultado.Equals("todos", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Enum.TryParse<ResultadoEvento>(resultado.Trim(), ignoreCase: true, out var convertido)
            ? convertido
            : throw new DomainException(
                $"Filtro de resultado invalido: '{resultado}'. Use Sucesso, Erro, Pendente ou Todos.");
    }
}

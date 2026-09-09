using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Api.Seguranca;
using Sabemi.Pagamentos.Domain.Auditoria;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Api.Auditoria;

/// <summary>
/// Registra na trilha de auditoria cada consulta feita ao painel.
/// </summary>
/// <remarks>
/// <para>
/// A gravacao acontece depois da resposta e em um escopo de DI proprio. O escopo
/// separado nao e detalhe: usar o <c>DbContext</c> da requisicao faria a trilha
/// compartilhar unidade de trabalho com o caso de uso, e um <c>SaveChanges</c>
/// da auditoria poderia arrastar junto alteracoes que o caso de uso ainda nao
/// quis gravar.
/// </para>
/// <para>
/// Falha ao auditar nao derruba a requisicao — ela ja terminou —, mas sai no log
/// como erro. Auditoria silenciosamente quebrada e pior do que auditoria
/// ausente: cria a impressao de que existe registro.
/// </para>
/// </remarks>
public sealed partial class AuditoriaMiddleware(
    RequestDelegate next,
    IServiceScopeFactory escopos,
    IOptions<OpcoesAuditoria> opcoes,
    ILogger<AuditoriaMiddleware> logger)
{
    /// <summary>Preenchido pelo login, que autentica dentro da propria requisicao.</summary>
    public const string ItemUsuario = "auditoria.usuario";

    private readonly OpcoesAuditoria _opcoes = opcoes.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await next(context);

        if (!_opcoes.Habilitada || !DeveAuditar(context.Request.Path))
        {
            return;
        }

        try
        {
            await RegistrarAsync(context);
        }
        catch (Exception excecao)
        {
            LogFalhaAoAuditar(logger, excecao, context.Request.Path);
        }
    }

    private bool DeveAuditar(PathString caminho)
    {
        if (!caminho.StartsWithSegments("/api"))
        {
            return false;
        }

        return !_opcoes.RotasIgnoradas.Any(rota =>
            caminho.StartsWithSegments(rota, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RegistrarAsync(HttpContext context)
    {
        using var escopo = escopos.CreateScope();

        var repositorio = escopo.ServiceProvider.GetRequiredService<IAuditoriaRepository>();
        var unitOfWork = escopo.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var registro = RegistroAuditoria.Registrar(
            context.User.Identity?.Name ?? context.Items[ItemUsuario] as string,
            context.User.FindFirstValue(OpcoesAutenticacao.ClaimPapel),
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Response.StatusCode,
            Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);

        // Sem o token da requisicao de proposito: se o cliente desconectou no
        // meio, a consulta ainda aconteceu e precisa aparecer na trilha.
        await repositorio.AdicionarAsync(registro, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Error,
        Message = "Nao foi possivel gravar a trilha de auditoria da rota {Rota}.")]
    private static partial void LogFalhaAoAuditar(ILogger logger, Exception excecao, string rota);
}

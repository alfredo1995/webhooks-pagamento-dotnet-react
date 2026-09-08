using System.Net.Mime;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Api.Middleware;

/// <summary>
/// Ponto unico de traducao de excecao para resposta HTTP: os controllers ficam
/// sem try/catch e o cliente sempre recebe ProblemDetails (RFC 7807).
/// </summary>
public sealed partial class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment ambiente)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (ValidationException excecao)
        {
            var erros = excecao.Errors
                .GroupBy(erro => erro.PropertyName)
                .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(erro => erro.ErrorMessage).ToArray());

            await EscreverAsync(context, new ValidationProblemDetails(erros)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Um ou mais campos sao invalidos.",
            });
        }
        catch (NotFoundException excecao)
        {
            await EscreverAsync(context, Problema(StatusCodes.Status404NotFound, "Recurso nao encontrado.", excecao.Message));
        }
        catch (DomainException excecao)
        {
            await EscreverAsync(context, Problema(StatusCodes.Status409Conflict, "Regra de negocio violada.", excecao.Message));
        }
        catch (Exception excecao)
        {
            LogNaoTratado(logger, excecao, context.TraceIdentifier);

            var detalhe = ambiente.IsDevelopment()
                ? excecao.ToString()
                : "Erro inesperado. Contate o suporte informando o traceId.";

            await EscreverAsync(context, Problema(StatusCodes.Status500InternalServerError, "Erro interno.", detalhe));
        }
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Error, Message = "Erro nao tratado na requisicao {TraceId}.")]
    private static partial void LogNaoTratado(ILogger logger, Exception excecao, string traceId);

    private static ProblemDetails Problema(int status, string titulo, string detalhe) => new()
    {
        Status = status,
        Title = titulo,
        Detail = detalhe,
    };

    private static async Task EscreverAsync(HttpContext context, ProblemDetails problema)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        problema.Extensions["traceId"] = context.TraceIdentifier;
        problema.Instance = context.Request.Path;

        context.Response.Clear();
        context.Response.StatusCode = problema.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = MediaTypeNames.Application.Json;

        await context.Response.WriteAsJsonAsync(problema, problema.GetType());
    }
}

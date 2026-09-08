namespace Sabemi.Pagamentos.Domain.Common;

/// <summary>Violacao de regra de negocio. Traduzida em HTTP 409 pela borda.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Recurso inexistente. Traduzido em HTTP 404 pela borda.</summary>
public class NotFoundException(string recurso, object chave)
    : Exception($"{recurso} '{chave}' nao foi encontrado.");

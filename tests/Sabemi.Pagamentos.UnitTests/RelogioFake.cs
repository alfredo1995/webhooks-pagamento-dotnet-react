namespace Sabemi.Pagamentos.UnitTests;

/// <summary>
/// Relogio controlado pelo teste.
/// </summary>
/// <remarks>
/// Backoff e janela de rotacao de segredo sao regras sobre o tempo. Testa-las com
/// o relogio real significaria ou esperar de verdade, ou afrouxar a assercao ate
/// ela nao provar mais nada.
/// </remarks>
public sealed class RelogioFake(DateTime inicioUtc) : TimeProvider
{
    public DateTime AgoraUtc { get; private set; } = DateTime.SpecifyKind(inicioUtc, DateTimeKind.Utc);

    public override DateTimeOffset GetUtcNow() => new(AgoraUtc, TimeSpan.Zero);

    public void Avancar(TimeSpan intervalo) => AgoraUtc = AgoraUtc.Add(intervalo);
}

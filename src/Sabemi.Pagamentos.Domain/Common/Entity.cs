using System.Runtime.CompilerServices;

namespace Sabemi.Pagamentos.Domain.Common;

/// <summary>
/// Base das entidades: identidade por Id, nunca por valor. Raizes de agregacao
/// geram o proprio Id, porque a aplicacao precisa referencia-las antes de salvar.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }

    public bool Transiente => Id == Guid.Empty;

    public override bool Equals(object? obj)
    {
        if (obj is not Entity outra || GetType() != outra.GetType())
        {
            return false;
        }

        return ReferenceEquals(this, outra) || (!Transiente && !outra.Transiente && Id == outra.Id);
    }

    public override int GetHashCode()
        => Transiente ? RuntimeHelpers.GetHashCode(this) : HashCode.Combine(GetType(), Id);

    protected void GerarIdentidade() => Id = Guid.NewGuid();
}

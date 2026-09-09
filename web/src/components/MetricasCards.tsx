import type { Metricas } from '../api/types'

interface Props {
  metricas: Metricas | null
}

export function MetricasCards({ metricas }: Props) {
  // "Em retentativa" e "Na dead-letter" contam a mesma historia em dois tempos:
  // o que o sistema ainda esta tentando resolver sozinho e o que ja desistiu e
  // espera decisao humana. Sem separar os dois, um numero de erros crescente
  // nao diria se o problema esta piorando ou se resolvendo.
  const cartoes = [
    { rotulo: 'Eventos recebidos', valor: metricas?.total, tom: 'neutro' },
    { rotulo: 'Processados', valor: metricas?.sucesso, tom: 'sucesso' },
    { rotulo: 'Com erro', valor: metricas?.erro, tom: 'erro' },
    { rotulo: 'Em retentativa', valor: metricas?.emRetentativa, tom: 'pendente' },
    { rotulo: 'Na dead-letter', valor: metricas?.deadLetters, tom: 'erro' },
    { rotulo: 'Pendentes', valor: metricas?.pendentes, tom: 'neutro' },
    { rotulo: 'Aguardando na fila', valor: metricas?.naFila, tom: 'fila' },
  ] as const

  return (
    <section className="cards" aria-label="Resumo">
      {cartoes.map((cartao) => (
        <article key={cartao.rotulo} className={`card card--${cartao.tom}`}>
          <span className="card__rotulo">{cartao.rotulo}</span>
          <strong className="card__valor">{cartao.valor ?? '—'}</strong>
        </article>
      ))}
    </section>
  )
}

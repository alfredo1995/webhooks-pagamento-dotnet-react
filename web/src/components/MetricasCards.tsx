import type { Metricas } from '../api/types'

interface Props {
  metricas: Metricas | null
}

export function MetricasCards({ metricas }: Props) {
  const cartoes = [
    { rotulo: 'Eventos recebidos', valor: metricas?.total, tom: 'neutro' },
    { rotulo: 'Processados', valor: metricas?.sucesso, tom: 'sucesso' },
    { rotulo: 'Com erro', valor: metricas?.erro, tom: 'erro' },
    { rotulo: 'Pendentes', valor: metricas?.pendentes, tom: 'pendente' },
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

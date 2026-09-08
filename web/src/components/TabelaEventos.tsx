import type { EventoResumo } from '../api/types'
import { StatusBadge } from './StatusBadge'
import { formatarDataHora, formatarDuracao, formatarMoeda } from './formatadores'

interface Props {
  eventos: EventoResumo[]
  carregando: boolean
  aoSelecionar: (evento: EventoResumo) => void
}

export function TabelaEventos({ eventos, carregando, aoSelecionar }: Props) {
  if (carregando && eventos.length === 0) {
    return <p className="vazio">Carregando eventos…</p>
  }

  if (eventos.length === 0) {
    return <p className="vazio">Nenhum evento encontrado para os filtros aplicados.</p>
  }

  return (
    <div className="tabela-wrapper">
      <table className="tabela">
        <thead>
          <tr>
            <th scope="col">Status</th>
            <th scope="col">Transação</th>
            <th scope="col">Contrato</th>
            <th scope="col" className="num">
              Valor
            </th>
            <th scope="col">Pagamento</th>
            <th scope="col">Recebido em</th>
            <th scope="col" className="num">
              Duração
            </th>
            <th scope="col">Detalhe</th>
          </tr>
        </thead>
        <tbody>
          {eventos.map((evento) => (
            <tr
              key={evento.id}
              className={evento.resultado === 'Erro' ? 'linha--erro' : undefined}
              onClick={() => aoSelecionar(evento)}
              tabIndex={0}
              role="button"
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault()
                  aoSelecionar(evento)
                }
              }}
            >
              <td>
                <StatusBadge status={evento.status} resultado={evento.resultado} />
              </td>
              <td className="mono">{evento.idTransacao}</td>
              <td className="mono">{evento.idContrato ?? '—'}</td>
              <td className="num">{formatarMoeda(evento.valor)}</td>
              <td>{evento.statusPagamento ?? '—'}</td>
              <td>{formatarDataHora(evento.recebidoEmUtc)}</td>
              <td className="num">{formatarDuracao(evento.duracaoProcessamentoMs)}</td>
              <td>
                {/* O motivo aparece na propria linha: descobrir por que falhou
                    nao deveria exigir abrir um modal. */}
                {evento.motivoFalha ? (
                  <span className="motivo" title={evento.motivoFalha}>
                    {evento.motivoFalha}
                  </span>
                ) : (
                  <span className="discreto">ver payload</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

import type { DeadLetterResumo } from '../api/types'
import { formatarDataHora } from './formatadores'

interface Props {
  cartas: DeadLetterResumo[]
  carregando: boolean
  podeReprocessar: boolean
  reprocessando: string | null
  aoReprocessar: (eventoId: string) => void
}

export function TabelaDeadLetters({
  cartas,
  carregando,
  podeReprocessar,
  reprocessando,
  aoReprocessar,
}: Props) {
  if (carregando && cartas.length === 0) {
    return <p className="vazio">Carregando a dead-letter queue…</p>
  }

  if (cartas.length === 0) {
    return <p className="vazio">Nenhum evento esgotou as retentativas automáticas.</p>
  }

  return (
    <div className="tabela-wrapper">
      <table className="tabela">
        <thead>
          <tr>
            <th scope="col">Situação</th>
            <th scope="col">Transação</th>
            <th scope="col">Contrato</th>
            <th scope="col" className="num">Tentativas</th>
            <th scope="col">Motivo da última falha</th>
            <th scope="col">Parou em</th>
            <th scope="col">Ação</th>
          </tr>
        </thead>
        <tbody>
          {cartas.map((carta) => (
            <tr key={carta.id} className={carta.pendente ? 'linha--erro' : undefined}>
              <td>
                <span className={`badge badge--${carta.pendente ? 'erro' : 'sucesso'}`}>
                  {carta.pendente ? '⚠ Parado' : 'Reprocessado'}
                </span>
              </td>
              <td className="mono">{carta.idTransacao}</td>
              <td className="mono">{carta.idContrato ?? '—'}</td>
              <td className="num">{carta.tentativas}</td>
              <td>
                <span className="motivo" title={carta.motivo}>
                  {carta.motivo}
                </span>
              </td>
              <td>{formatarDataHora(carta.criadoEmUtc)}</td>
              <td>
                {carta.pendente ? (
                  <button
                    type="button"
                    className="botao"
                    /* Reprocessar movimenta saldo de contrato: e uma escrita, e
                       so administrador tem o papel para isso. */
                    disabled={!podeReprocessar || reprocessando === carta.eventoId}
                    title={
                      podeReprocessar
                        ? 'Devolve o evento para a fila com um orçamento novo de tentativas'
                        : 'Somente administradores podem reprocessar'
                    }
                    onClick={() => aoReprocessar(carta.eventoId)}
                  >
                    {reprocessando === carta.eventoId ? 'Enviando…' : 'Reprocessar'}
                  </button>
                ) : (
                  <span className="discreto">
                    por {carta.reprocessadoPor} em {formatarDataHora(carta.reprocessadoEmUtc)}
                  </span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

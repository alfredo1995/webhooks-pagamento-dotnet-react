import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { EventoDetalhe, EventoResumo } from '../api/types'
import { StatusBadge } from './StatusBadge'
import { formatarDataHora, formatarDuracao, formatarMoeda } from './formatadores'

interface Props {
  evento: EventoResumo
  aoFechar: () => void
}

export function ModalEvento({ evento, aoFechar }: Props) {
  const [detalhe, setDetalhe] = useState<EventoDetalhe | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    api
      .obterEvento(evento.id, controller.signal)
      .then(setDetalhe)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) {
          setErro(e instanceof Error ? e.message : 'Falha ao carregar o evento.')
        }
      })

    return () => controller.abort()
  }, [evento.id])

  useEffect(() => {
    const aoTeclar = (e: KeyboardEvent) => {
      if (e.key === 'Escape') aoFechar()
    }

    window.addEventListener('keydown', aoTeclar)

    return () => window.removeEventListener('keydown', aoTeclar)
  }, [aoFechar])

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Detalhe do evento" onClick={aoFechar}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <header className="modal__topo">
          <div>
            <StatusBadge status={evento.status} resultado={evento.resultado} />
            <h2 className="mono">{evento.idTransacao}</h2>
          </div>
          <button type="button" className="botao botao--discreto" onClick={aoFechar} aria-label="Fechar">
            ✕
          </button>
        </header>

        {evento.motivoFalha && (
          <div className="alerta" role="alert">
            <strong>Motivo da falha</strong>
            <p>{evento.motivoFalha}</p>
          </div>
        )}

        <dl className="propriedades">
          <div>
            <dt>Contrato</dt>
            <dd className="mono">{evento.idContrato ?? '—'}</dd>
          </div>
          <div>
            <dt>Valor</dt>
            <dd>{formatarMoeda(evento.valor)}</dd>
          </div>
          <div>
            <dt>Status do pagamento</dt>
            <dd>{evento.statusPagamento ?? '—'}</dd>
          </div>
          <div>
            <dt>Data do pagamento</dt>
            <dd>{formatarDataHora(evento.dataPagamento)}</dd>
          </div>
          <div>
            <dt>Recebido em</dt>
            <dd>{formatarDataHora(evento.recebidoEmUtc)}</dd>
          </div>
          <div>
            <dt>Processado em</dt>
            <dd>{formatarDataHora(evento.processadoEmUtc)}</dd>
          </div>
          <div>
            <dt>Duração</dt>
            <dd>{formatarDuracao(evento.duracaoProcessamentoMs)}</dd>
          </div>
          <div>
            <dt>Tentativas</dt>
            <dd>{evento.tentativas}</dd>
          </div>
          <div>
            <dt>Parceiro</dt>
            <dd>{evento.origemParceiro}</dd>
          </div>
        </dl>

        <section className="payload">
          <h3>Payload bruto recebido</h3>
          {erro && <p className="alerta" role="alert">{erro}</p>}
          <pre>{detalhe ? formatarJson(detalhe.payloadBruto) : 'Carregando…'}</pre>
        </section>
      </div>
    </div>
  )
}

/** Reindenta para leitura, mas nunca esconde o original: se nao for JSON, mostra cru. */
function formatarJson(bruto: string): string {
  try {
    return JSON.stringify(JSON.parse(bruto), null, 2)
  } catch {
    return bruto
  }
}

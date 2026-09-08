import type {
  ContratoResumo,
  EventoDetalhe,
  EventoResumo,
  FiltrosEventos,
  Metricas,
  PagedResult,
} from './types'

/**
 * Caminhos relativos de proposito: em desenvolvimento o Vite faz proxy e em
 * producao o nginx serve o painel e a API na mesma origem. Sem URL absoluta,
 * nao existe build "de um ambiente so".
 */
const BASE = '/api'

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

async function obter<T>(caminho: string, signal?: AbortSignal): Promise<T> {
  const resposta = await fetch(`${BASE}${caminho}`, {
    signal,
    headers: { Accept: 'application/json' },
  })

  if (!resposta.ok) {
    const corpo = await resposta.text()
    let detalhe = corpo

    try {
      const problema = JSON.parse(corpo) as { detail?: string; title?: string }
      detalhe = problema.detail ?? problema.title ?? corpo
    } catch {
      // Resposta sem ProblemDetails: mantem o texto cru.
    }

    throw new ApiError(detalhe || `Falha na requisicao (HTTP ${resposta.status}).`, resposta.status)
  }

  return (await resposta.json()) as T
}

function montarQuery(filtros: FiltrosEventos, tamanhoPagina: number): string {
  const parametros = new URLSearchParams()

  if (filtros.resultado !== 'Todos') {
    parametros.set('resultado', filtros.resultado)
  }

  if (filtros.idContrato.trim()) {
    parametros.set('idContrato', filtros.idContrato.trim())
  }

  if (filtros.idTransacao.trim()) {
    parametros.set('idTransacao', filtros.idTransacao.trim())
  }

  parametros.set('pagina', String(filtros.pagina))
  parametros.set('tamanhoPagina', String(tamanhoPagina))

  return parametros.toString()
}

export const api = {
  listarEventos(filtros: FiltrosEventos, tamanhoPagina: number, signal?: AbortSignal) {
    return obter<PagedResult<EventoResumo>>(`/eventos?${montarQuery(filtros, tamanhoPagina)}`, signal)
  },

  obterEvento(id: string, signal?: AbortSignal) {
    return obter<EventoDetalhe>(`/eventos/${id}`, signal)
  },

  listarContratos(idContrato: string, signal?: AbortSignal) {
    const parametros = new URLSearchParams({ tamanhoPagina: '50' })

    if (idContrato.trim()) {
      parametros.set('idContrato', idContrato.trim())
    }

    return obter<PagedResult<ContratoResumo>>(`/contratos?${parametros}`, signal)
  },

  obterMetricas(signal?: AbortSignal) {
    return obter<Metricas>('/metricas', signal)
  },
}

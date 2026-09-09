import { armazenamentoSessao } from './sessao'
import type {
  AuditoriaResumo,
  ContratoResumo,
  DeadLetterResumo,
  EventoDetalhe,
  EventoResumo,
  FiltrosEventos,
  Metricas,
  PagedResult,
  Sessao,
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

/**
 * Um 401 nao e erro de tela, e fim de sessao.
 *
 * Quem descobre isso e a camada de rede, mas quem precisa reagir e a aplicacao:
 * o painel mantem varias requisicoes em polling, e sem um ponto unico de aviso
 * cada uma mostraria seu proprio "nao autorizado" em vez de voltar para o login.
 */
let aoPerderSessao: () => void = () => {}

export function registrarPerdaDeSessao(callback: () => void) {
  aoPerderSessao = callback
}

async function requisitar<T>(
  caminho: string,
  init: RequestInit = {},
  signal?: AbortSignal,
): Promise<T> {
  const sessao = armazenamentoSessao.ler()

  const resposta = await fetch(`${BASE}${caminho}`, {
    ...init,
    signal,
    headers: {
      Accept: 'application/json',
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...(sessao ? { Authorization: `Bearer ${sessao.token}` } : {}),
      ...init.headers,
    },
  })

  // 401 com sessao no bolso e token vencido; 401 sem sessao e o proprio login
  // recusando credencial. Tratar os dois igual faria a tela de login dizer
  // "sessao expirada" para quem acabou de errar a senha.
  if (resposta.status === 401 && sessao) {
    armazenamentoSessao.limpar()
    aoPerderSessao()

    throw new ApiError('Sessão expirada. Entre novamente.', 401)
  }

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

  return resposta.status === 204 ? (undefined as T) : ((await resposta.json()) as T)
}

function obter<T>(caminho: string, signal?: AbortSignal): Promise<T> {
  return requisitar<T>(caminho, {}, signal)
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
  async entrar(login: string, senha: string): Promise<Sessao> {
    const sessao = await requisitar<Sessao>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ login, senha }),
    })

    armazenamentoSessao.gravar(sessao)

    return sessao
  },

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

  listarDeadLetters(apenasPendentes: boolean, signal?: AbortSignal) {
    return obter<PagedResult<DeadLetterResumo>>(
      `/dead-letters?apenasPendentes=${apenasPendentes}&tamanhoPagina=50`,
      signal,
    )
  },

  reprocessar(eventoId: string) {
    return requisitar<{ eventoId: string; idTransacao: string; mensagem: string }>(
      `/dead-letters/${eventoId}/reprocessar`,
      { method: 'POST' },
    )
  },

  listarAuditoria(usuario: string, recurso: string, signal?: AbortSignal) {
    const parametros = new URLSearchParams({ tamanhoPagina: '50' })

    if (usuario.trim()) parametros.set('usuario', usuario.trim())
    if (recurso.trim()) parametros.set('recurso', recurso.trim())

    return obter<PagedResult<AuditoriaResumo>>(`/auditoria?${parametros}`, signal)
  },
}

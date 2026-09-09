import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api, registrarPerdaDeSessao } from './client'
import { armazenamentoSessao } from './sessao'
import { asMetricas, umEvento, umaPagina, umaSessao } from '../testes/fabricas'

function responder(corpo: unknown, status = 200): Response {
  return new Response(status === 204 ? null : JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function urlDaChamada(indice = 0): string {
  return String(vi.mocked(fetch).mock.calls[indice]?.[0])
}

function cabecalhosDaChamada(indice = 0): Record<string, string> {
  return (vi.mocked(fetch).mock.calls[indice]?.[1]?.headers ?? {}) as Record<string, string>
}

describe('client', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    registrarPerdaDeSessao(() => {})
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('usa caminho relativo, para nao existir build "de um ambiente so"', async () => {
    vi.mocked(fetch).mockResolvedValue(responder(asMetricas()))

    await api.obterMetricas()

    expect(urlDaChamada()).toBe('/api/metricas')
  })

  it('anexa o token quando ha sessao', async () => {
    armazenamentoSessao.gravar(umaSessao({ token: 'jwt-abc' }))
    vi.mocked(fetch).mockResolvedValue(responder(asMetricas()))

    await api.obterMetricas()

    expect(cabecalhosDaChamada()).toMatchObject({ Authorization: 'Bearer jwt-abc' })
  })

  it('nao anexa Authorization quando nao ha sessao', async () => {
    vi.mocked(fetch).mockResolvedValue(responder(asMetricas()))

    await api.obterMetricas()

    expect(cabecalhosDaChamada()).not.toHaveProperty('Authorization')
  })

  it('so manda Content-Type quando ha corpo', async () => {
    vi.mocked(fetch).mockResolvedValue(responder(umaSessao()))

    await api.entrar('admin', 'senha')

    expect(cabecalhosDaChamada()).toMatchObject({ 'Content-Type': 'application/json' })
  })

  it('grava a sessao ao entrar', async () => {
    const sessao = umaSessao()
    vi.mocked(fetch).mockResolvedValue(responder(sessao))

    await api.entrar('admin', 'senha')

    expect(armazenamentoSessao.ler()).toEqual(sessao)
  })

  /**
   * 401 com sessao no bolso e token vencido; 401 sem sessao e o proprio login
   * recusando credencial. Tratar os dois igual faria a tela de login dizer
   * "sessao expirada" para quem acabou de errar a senha.
   */
  it('401 com sessao encerra a sessao e avisa a aplicacao uma vez so', async () => {
    armazenamentoSessao.gravar(umaSessao())
    const aoPerder = vi.fn()
    registrarPerdaDeSessao(aoPerder)
    vi.mocked(fetch).mockResolvedValue(responder({ detail: 'nao autorizado' }, 401))

    await expect(api.obterMetricas()).rejects.toThrow('Sessão expirada. Entre novamente.')

    expect(armazenamentoSessao.ler()).toBeNull()
    expect(aoPerder).toHaveBeenCalledOnce()
  })

  it('401 sem sessao devolve a mensagem da API e nao dispara perda de sessao', async () => {
    const aoPerder = vi.fn()
    registrarPerdaDeSessao(aoPerder)
    vi.mocked(fetch).mockResolvedValue(
      responder({ detail: 'Usuario ou senha invalidos.' }, 401),
    )

    await expect(api.entrar('admin', 'errada')).rejects.toThrow('Usuario ou senha invalidos.')

    expect(aoPerder).not.toHaveBeenCalled()
  })

  it('extrai detail do ProblemDetails', async () => {
    vi.mocked(fetch).mockResolvedValue(
      responder({ title: 'Nao autorizado.', detail: 'Assinatura invalida.' }, 403),
    )

    await expect(api.obterMetricas()).rejects.toThrow('Assinatura invalida.')
  })

  it('cai para o title quando nao ha detail', async () => {
    vi.mocked(fetch).mockResolvedValue(responder({ title: 'Acesso negado.' }, 403))

    await expect(api.obterMetricas()).rejects.toThrow('Acesso negado.')
  })

  it('mantem o texto cru quando a resposta nao e ProblemDetails', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response('estourou no proxy', { status: 502 }))

    await expect(api.obterMetricas()).rejects.toThrow('estourou no proxy')
  })

  it('descreve o status quando o corpo do erro vem vazio', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response('', { status: 500 }))

    await expect(api.obterMetricas()).rejects.toThrow('Falha na requisicao (HTTP 500).')
  })

  it('expoe o status no erro, para quem precisar distinguir', async () => {
    vi.mocked(fetch).mockResolvedValue(responder({ detail: 'nao achei' }, 404))

    await expect(api.obterMetricas()).rejects.toMatchObject({
      name: 'ApiError',
      status: 404,
    })
    expect(new ApiError('x', 404)).toBeInstanceOf(Error)
  })

  it('aceita 204 sem tentar desserializar corpo', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 204 }))

    await expect(api.obterMetricas()).resolves.toBeUndefined()
  })

  describe('montagem da query de eventos', () => {
    beforeEach(() => {
      vi.mocked(fetch).mockResolvedValue(responder(umaPagina([umEvento()])))
    })

    it('omite o resultado quando o filtro e "Todos"', async () => {
      await api.listarEventos(
        { resultado: 'Todos', idContrato: '', idTransacao: '', pagina: 1 },
        25,
      )

      expect(urlDaChamada()).not.toContain('resultado=')
      expect(urlDaChamada()).toContain('pagina=1')
      expect(urlDaChamada()).toContain('tamanhoPagina=25')
    })

    it('envia resultado, contrato e transacao quando preenchidos', async () => {
      await api.listarEventos(
        { resultado: 'Erro', idContrato: 'CT-1', idTransacao: 'TX-1', pagina: 3 },
        50,
      )

      const url = urlDaChamada()

      expect(url).toContain('resultado=Erro')
      expect(url).toContain('idContrato=CT-1')
      expect(url).toContain('idTransacao=TX-1')
      expect(url).toContain('pagina=3')
    })

    /** Espaco colado no fim de um id nao pode virar filtro que nao acha nada. */
    it('apara espacos e ignora filtro que so tem espaco', async () => {
      await api.listarEventos(
        { resultado: 'Todos', idContrato: '  CT-9  ', idTransacao: '   ', pagina: 1 },
        25,
      )

      expect(urlDaChamada()).toContain('idContrato=CT-9')
      expect(urlDaChamada()).not.toContain('idTransacao=')
    })

    it('escapa caractere especial no filtro', async () => {
      await api.listarEventos(
        { resultado: 'Todos', idContrato: 'CT/1 &2', idTransacao: '', pagina: 1 },
        25,
      )

      expect(urlDaChamada()).toContain('idContrato=CT%2F1+%262')
    })
  })

  it('lista contratos, omitindo o filtro vazio', async () => {
    vi.mocked(fetch).mockResolvedValue(responder(umaPagina([])))

    await api.listarContratos('   ')

    expect(urlDaChamada()).toBe('/api/contratos?tamanhoPagina=50')
  })

  it('lista dead-letters com o recorte pedido', async () => {
    vi.mocked(fetch).mockResolvedValue(responder(umaPagina([])))

    await api.listarDeadLetters(true)

    expect(urlDaChamada()).toContain('apenasPendentes=true')
  })

  it('reprocessa por POST', async () => {
    vi.mocked(fetch).mockResolvedValue(
      responder({ eventoId: 'e1', idTransacao: 'TX-1', mensagem: 'devolvido a fila' }),
    )

    await api.reprocessar('e1')

    expect(urlDaChamada()).toBe('/api/dead-letters/e1/reprocessar')
    expect(vi.mocked(fetch).mock.calls[0]?.[1]?.method).toBe('POST')
  })

  it('repassa o signal para permitir cancelamento', async () => {
    vi.mocked(fetch).mockResolvedValue(responder(asMetricas()))
    const controller = new AbortController()

    await api.obterMetricas(controller.signal)

    expect(vi.mocked(fetch).mock.calls[0]?.[1]?.signal).toBe(controller.signal)
  })
})

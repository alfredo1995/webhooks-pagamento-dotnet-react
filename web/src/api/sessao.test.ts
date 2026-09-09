import { describe, expect, it } from 'vitest'
import { armazenamentoSessao } from './sessao'
import { umaSessao } from '../testes/fabricas'

const CHAVE = 'sabemi.painel.sessao'

describe('armazenamentoSessao', () => {
  it('grava e le a sessao', () => {
    const sessao = umaSessao()

    armazenamentoSessao.gravar(sessao)

    expect(armazenamentoSessao.ler()).toEqual(sessao)
  })

  it('devolve nulo quando nunca houve sessao', () => {
    expect(armazenamentoSessao.ler()).toBeNull()
  })

  /**
   * Enviar token vencido so renderiza o painel para ele quebrar no primeiro
   * request — e o operador ve um erro de API onde deveria ver a tela de login.
   */
  it('trata sessao expirada como ausente', () => {
    armazenamentoSessao.gravar(
      umaSessao({ expiraEmUtc: new Date(Date.now() - 1000).toISOString() }),
    )

    expect(armazenamentoSessao.ler()).toBeNull()
  })

  /** Storage corrompido nao pode derrubar a aplicacao na primeira renderizacao. */
  it('trata conteudo invalido como ausente, sem lancar', () => {
    sessionStorage.setItem(CHAVE, '{isso nao e json')

    expect(() => armazenamentoSessao.ler()).not.toThrow()
    expect(armazenamentoSessao.ler()).toBeNull()
  })

  it('limpa a sessao', () => {
    armazenamentoSessao.gravar(umaSessao())

    armazenamentoSessao.limpar()

    expect(armazenamentoSessao.ler()).toBeNull()
    expect(sessionStorage.getItem(CHAVE)).toBeNull()
  })

  /**
   * Em sessionStorage, e nao em localStorage: o token morre com a aba. Painel
   * operacional roda em maquina compartilhada, e nao ha ganho em manter
   * credencial valida depois que a pessoa fechou a janela.
   */
  it('usa sessionStorage, nao localStorage', () => {
    armazenamentoSessao.gravar(umaSessao())

    expect(sessionStorage.getItem(CHAVE)).not.toBeNull()
    expect(localStorage.getItem(CHAVE)).toBeNull()
  })
})

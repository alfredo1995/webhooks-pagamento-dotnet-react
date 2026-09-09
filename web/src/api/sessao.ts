import type { Sessao } from './types'

const CHAVE = 'sabemi.painel.sessao'

/**
 * A sessao vive em `sessionStorage`, e nao em `localStorage`: o token morre com
 * a aba. Um painel operacional e usado em turno, muitas vezes em maquina
 * compartilhada, e nao ha ganho em manter credencial valida depois que a pessoa
 * fechou a janela.
 *
 * Guardar em memoria seria mais seguro ainda, mas um F5 exigiria login de novo —
 * e o painel atualiza sozinho a cada tres segundos, entao recarregar a pagina e
 * um gesto comum.
 */
export const armazenamentoSessao = {
  ler(): Sessao | null {
    const bruto = sessionStorage.getItem(CHAVE)
    if (!bruto) return null

    try {
      const sessao = JSON.parse(bruto) as Sessao

      // Token expirado nao adianta enviar: só renderiza o painel para ele
      // quebrar no primeiro request.
      return new Date(sessao.expiraEmUtc).getTime() > Date.now() ? sessao : null
    } catch {
      return null
    }
  },

  gravar(sessao: Sessao) {
    sessionStorage.setItem(CHAVE, JSON.stringify(sessao))
  },

  limpar() {
    sessionStorage.removeItem(CHAVE)
  },
}

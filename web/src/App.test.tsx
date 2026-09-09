import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { api } from './api/client'
import { armazenamentoSessao } from './api/sessao'
import {
  asMetricas,
  umContrato,
  umEvento,
  umEventoComErro,
  umRegistroDeAuditoria,
  umaCarta,
  umaPagina,
  umaSessao,
} from './testes/fabricas'

vi.mock('./api/client', async (original) => ({
  ...(await original<typeof import('./api/client')>()),
  api: {
    entrar: vi.fn(),
    listarEventos: vi.fn(),
    obterEvento: vi.fn(),
    listarContratos: vi.fn(),
    obterMetricas: vi.fn(),
    listarDeadLetters: vi.fn(),
    reprocessar: vi.fn(),
    listarAuditoria: vi.fn(),
  },
}))

/**
 * Testes de jornada: exercitam o painel pelo caminho que o operador percorre,
 * com a API dublada na fronteira. E o unico nivel em que se verifica que filtro
 * digitado na tela vira consulta enviada — os testes de componente sabem que o
 * callback foi chamado, nao que alguem o ligou na requisicao.
 */
function dublarApi() {
  vi.mocked(api.obterMetricas).mockResolvedValue(asMetricas())
  vi.mocked(api.listarEventos).mockResolvedValue(
    umaPagina([umEvento(), umEventoComErro()]),
  )
  vi.mocked(api.listarContratos).mockResolvedValue(umaPagina([umContrato()]))
  vi.mocked(api.listarDeadLetters).mockResolvedValue(umaPagina([umaCarta()]))
  vi.mocked(api.listarAuditoria).mockResolvedValue(umaPagina([umRegistroDeAuditoria()]))
  vi.mocked(api.obterEvento).mockResolvedValue({ resumo: umEvento(), payloadBruto: '{}' })
}

async function abrirPainelComo(papel: 'administrador' | 'operador' = 'administrador') {
  armazenamentoSessao.gravar(umaSessao({ papel, nome: papel === 'operador' ? 'Operadora' : 'Administracao' }))

  render(<App />)

  expect(await screen.findByRole('heading', { name: 'Painel de Pagamentos' })).toBeInTheDocument()
}

describe('App', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    dublarApi()
  })

  it('mostra o login quando nao ha sessao', () => {
    render(<App />)

    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument()
    expect(api.listarEventos).not.toHaveBeenCalled()
  })

  it('entra e passa a mostrar o painel', async () => {
    vi.mocked(api.entrar).mockImplementation(async () => {
      const sessao = umaSessao()
      armazenamentoSessao.gravar(sessao)

      return sessao
    })

    render(<App />)

    await userEvent.type(screen.getByLabelText('Usuário'), 'admin')
    await userEvent.type(screen.getByLabelText('Senha'), 'segredo')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('heading', { name: 'Painel de Pagamentos' })).toBeInTheDocument()
    expect(screen.getByText('Administracao')).toBeInTheDocument()
  })

  it('reaproveita a sessao guardada, sem pedir login de novo', async () => {
    await abrirPainelComo()

    expect(screen.queryByRole('button', { name: 'Entrar' })).not.toBeInTheDocument()
  })

  it('lista os eventos e as metricas', async () => {
    await abrirPainelComo()

    expect(await screen.findByText('TX-001')).toBeInTheDocument()
    expect(screen.getByText('TX-INVALIDA')).toBeInTheDocument()
  })

  /** O que o enunciado pede: filtrar por status na tela vira consulta na API. */
  it('filtra por status e repassa o filtro para a API', async () => {
    await abrirPainelComo()
    await screen.findByText('TX-001')

    await userEvent.click(screen.getByRole('button', { name: 'Erro' }))

    await waitFor(() =>
      expect(api.listarEventos).toHaveBeenLastCalledWith(
        expect.objectContaining({ resultado: 'Erro' }),
        25,
        expect.anything(),
      ),
    )
  })

  it('filtra por contrato e repassa o filtro para a API', async () => {
    await abrirPainelComo()
    await screen.findByText('TX-001')

    await userEvent.type(screen.getByLabelText('ID do contrato'), 'CT-9')

    await waitFor(() =>
      expect(api.listarEventos).toHaveBeenLastCalledWith(
        expect.objectContaining({ idContrato: 'CT-9' }),
        25,
        expect.anything(),
      ),
    )
  })

  /**
   * O alerta fica acima e independe do filtro ativo: um evento com falha nao
   * pode depender de o operador lembrar de filtrar para aparecer.
   */
  it('avisa dos erros no topo, com a contagem e a dead-letter', async () => {
    await abrirPainelComo()

    const alerta = await screen.findByText(/5 eventos com erro/)

    expect(alerta).toHaveTextContent('1 na dead-letter queue')
  })

  it('usa singular quando ha um erro so', async () => {
    vi.mocked(api.obterMetricas).mockResolvedValue(asMetricas({ erro: 1, deadLetters: 0 }))

    await abrirPainelComo()

    expect(await screen.findByText(/1 evento com erro/)).toBeInTheDocument()
  })

  it('nao mostra o alerta quando nao ha erro', async () => {
    vi.mocked(api.obterMetricas).mockResolvedValue(asMetricas({ erro: 0, deadLetters: 0 }))

    await abrirPainelComo()
    await screen.findByText('TX-001')

    expect(screen.queryByText(/evento.* com erro/)).not.toBeInTheDocument()
  })

  it('o atalho do alerta aplica o filtro de erro', async () => {
    await abrirPainelComo()

    await userEvent.click(await screen.findByRole('button', { name: 'Ver apenas os erros' }))

    await waitFor(() =>
      expect(api.listarEventos).toHaveBeenLastCalledWith(
        expect.objectContaining({ resultado: 'Erro' }),
        25,
        expect.anything(),
      ),
    )
  })

  it('o atalho do alerta abre a dead-letter queue', async () => {
    await abrirPainelComo()

    await userEvent.click(await screen.findByRole('button', { name: 'Abrir a dead-letter queue' }))

    expect(await screen.findByText('TX-ESTORNO')).toBeInTheDocument()
  })

  it('abre o detalhe do evento ao clicar na linha', async () => {
    await abrirPainelComo()

    await userEvent.click(await screen.findByText('TX-001'))

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(api.obterEvento).toHaveBeenCalled()
  })

  it('troca entre as abas, consultando so a que esta visivel', async () => {
    await abrirPainelComo()
    await screen.findByText('TX-001')

    expect(api.listarContratos).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Status por contrato' }))

    expect(await screen.findByText('CT-2026-0001')).toBeInTheDocument()
    await waitFor(() => expect(api.listarContratos).toHaveBeenCalled())
  })

  /**
   * A aba de auditoria nao existe para operador — e, mais importante, a consulta
   * nem chega a sair: senao o painel exibiria o 403 como se a API estivesse fora.
   */
  it('esconde a auditoria do operador e nao consulta a rota', async () => {
    await abrirPainelComo('operador')
    await screen.findByText('TX-001')

    expect(screen.queryByRole('button', { name: 'Auditoria' })).not.toBeInTheDocument()
    expect(api.listarAuditoria).not.toHaveBeenCalled()
  })

  it('mostra a auditoria para o administrador', async () => {
    await abrirPainelComo('administrador')
    await screen.findByText('TX-001')

    await userEvent.click(screen.getByRole('button', { name: 'Auditoria' }))

    expect(await screen.findByText('GET /api/eventos')).toBeInTheDocument()
  })

  it('conta as cartas pendentes no rotulo da aba', async () => {
    await abrirPainelComo()

    expect(await screen.findByRole('button', { name: 'Dead-letter queue (1)' })).toBeInTheDocument()
  })

  it('reprocessa a carta e mostra a confirmacao', async () => {
    vi.mocked(api.reprocessar).mockResolvedValue({
      eventoId: '44444444-4444-4444-4444-444444444444',
      idTransacao: 'TX-ESTORNO',
      mensagem: 'devolvido a fila',
    })

    await abrirPainelComo()

    await userEvent.click(await screen.findByRole('button', { name: 'Dead-letter queue (1)' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Reprocessar' }))

    expect(await screen.findByText('TX-ESTORNO: devolvido a fila')).toBeInTheDocument()
  })

  it('mostra a falha do reprocessamento sem derrubar a tela', async () => {
    vi.mocked(api.reprocessar).mockRejectedValue(new Error('Evento não está na DLQ.'))

    await abrirPainelComo()

    await userEvent.click(await screen.findByRole('button', { name: 'Dead-letter queue (1)' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Reprocessar' }))

    expect(await screen.findByText('Evento não está na DLQ.')).toBeInTheDocument()
  })

  it('mostra o erro da API quando a consulta falha', async () => {
    vi.mocked(api.listarEventos).mockRejectedValue(new Error('Failed to fetch'))

    await abrirPainelComo()

    expect(await screen.findByText('Não foi possível falar com a API.')).toBeInTheDocument()
    expect(screen.getByText('Failed to fetch')).toBeInTheDocument()
  })

  it('sai e volta para o login, limpando a sessao', async () => {
    await abrirPainelComo()

    await userEvent.click(screen.getByRole('button', { name: 'Sair' }))

    expect(await screen.findByRole('button', { name: 'Entrar' })).toBeInTheDocument()
    expect(armazenamentoSessao.ler()).toBeNull()
  })

  it('alterna o indicador de ao vivo ao pausar', async () => {
    await abrirPainelComo()
    await screen.findByText('TX-001')

    // O rotulo é buscado pelo indicador do cabecalho: "pausado" tambem aparece
    // no texto de apoio da barra de filtros, e a busca por texto solto acharia
    // os dois.
    const indicador = () => document.querySelector('.pulso')

    expect(indicador()).toHaveTextContent('ao vivo')
    expect(indicador()).toHaveClass('pulso--ativo')

    await userEvent.click(screen.getByRole('checkbox'))

    expect(indicador()).toHaveTextContent('pausado')
    expect(indicador()).not.toHaveClass('pulso--ativo')
  })

  it('mostra a paginacao so quando ha mais de uma pagina', async () => {
    vi.mocked(api.listarEventos).mockResolvedValue(
      umaPagina([umEvento()], { totalPaginas: 3, totalItens: 60, temProximaPagina: true }),
    )

    await abrirPainelComo()

    const paginacao = await screen.findByRole('navigation', { name: 'Paginação' })

    expect(within(paginacao).getByText(/Página 1 de 3/)).toBeInTheDocument()
    expect(within(paginacao).getByRole('button', { name: 'Anterior' })).toBeDisabled()
  })

  it('avanca de pagina', async () => {
    vi.mocked(api.listarEventos).mockResolvedValue(
      umaPagina([umEvento()], { totalPaginas: 3, totalItens: 60, temProximaPagina: true }),
    )

    await abrirPainelComo()

    await userEvent.click(await screen.findByRole('button', { name: 'Próxima' }))

    await waitFor(() =>
      expect(api.listarEventos).toHaveBeenLastCalledWith(
        expect.objectContaining({ pagina: 2 }),
        25,
        expect.anything(),
      ),
    )
  })

  it('esconde a paginacao quando tudo cabe em uma pagina', async () => {
    await abrirPainelComo()
    await screen.findByText('TX-001')

    expect(screen.queryByRole('navigation', { name: 'Paginação' })).not.toBeInTheDocument()
  })
})

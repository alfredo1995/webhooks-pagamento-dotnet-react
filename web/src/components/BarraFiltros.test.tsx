import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { BarraFiltros } from './BarraFiltros'
import type { FiltrosEventos } from '../api/types'

const filtrosPadrao: FiltrosEventos = {
  resultado: 'Todos',
  idContrato: '',
  idTransacao: '',
  pagina: 1,
}

function montar(sobrescritas: Partial<Parameters<typeof BarraFiltros>[0]> = {}) {
  const aoAlterar = vi.fn()
  const aoAlternarAutoRefresh = vi.fn()
  const aoRecarregar = vi.fn()

  render(
    <BarraFiltros
      filtros={filtrosPadrao}
      aoAlterar={aoAlterar}
      autoRefresh
      aoAlternarAutoRefresh={aoAlternarAutoRefresh}
      aoRecarregar={aoRecarregar}
      atualizadoEm="10:25:31"
      {...sobrescritas}
    />,
  )

  return { aoAlterar, aoAlternarAutoRefresh, aoRecarregar }
}

describe('BarraFiltros', () => {
  it('filtra por resultado ao clicar no chip', async () => {
    const { aoAlterar } = montar()

    await userEvent.click(screen.getByRole('button', { name: 'Erro' }))

    expect(aoAlterar).toHaveBeenCalledWith(expect.objectContaining({ resultado: 'Erro' }))
  })

  it('marca o chip ativo para leitores de tela, nao so pela cor', () => {
    montar({ filtros: { ...filtrosPadrao, resultado: 'Erro' } })

    expect(screen.getByRole('button', { name: 'Erro' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Sucesso' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('filtra por id do contrato', async () => {
    const { aoAlterar } = montar()

    await userEvent.type(screen.getByLabelText('ID do contrato'), 'C')

    expect(aoAlterar).toHaveBeenCalledWith(expect.objectContaining({ idContrato: 'C' }))
  })

  it('filtra por id da transacao', async () => {
    const { aoAlterar } = montar()

    await userEvent.type(screen.getByLabelText('ID da transação'), 'T')

    expect(aoAlterar).toHaveBeenCalledWith(expect.objectContaining({ idTransacao: 'T' }))
  })

  /**
   * Manter a pagina 7 ao trocar de filtro quase sempre resulta em lista vazia
   * sem explicacao — o operador conclui que nao ha nada, quando so esta fora do
   * intervalo.
   */
  it('volta para a primeira pagina quando qualquer filtro muda', async () => {
    const { aoAlterar } = montar({ filtros: { ...filtrosPadrao, pagina: 7 } })

    await userEvent.click(screen.getByRole('button', { name: 'Sucesso' }))

    expect(aoAlterar).toHaveBeenCalledWith(expect.objectContaining({ pagina: 1 }))
  })

  it('preserva os demais filtros ao mudar um deles', async () => {
    const { aoAlterar } = montar({
      filtros: { ...filtrosPadrao, idContrato: 'CT-9', idTransacao: 'TX-9' },
    })

    await userEvent.click(screen.getByRole('button', { name: 'Erro' }))

    expect(aoAlterar).toHaveBeenCalledWith({
      resultado: 'Erro',
      idContrato: 'CT-9',
      idTransacao: 'TX-9',
      pagina: 1,
    })
  })

  /** Nas abas que nao listam eventos, um filtro de evento nao teria efeito. */
  it('esconde os filtros de evento nas abas que nao listam eventos', () => {
    montar({ somenteAtualizacao: true })

    expect(screen.queryByLabelText('ID do contrato')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Erro' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Atualizar agora' })).toBeInTheDocument()
  })

  it('pausa e retoma a atualizacao automatica', async () => {
    const { aoAlternarAutoRefresh } = montar()

    await userEvent.click(screen.getByRole('checkbox'))

    expect(aoAlternarAutoRefresh).toHaveBeenCalledWith(false)
  })

  it('mostra a hora da ultima atualizacao quando ativo, e "pausado" quando nao', () => {
    const { unmount } = render(
      <BarraFiltros
        filtros={filtrosPadrao}
        aoAlterar={vi.fn()}
        autoRefresh
        aoAlternarAutoRefresh={vi.fn()}
        aoRecarregar={vi.fn()}
        atualizadoEm="10:25:31"
      />,
    )

    expect(screen.getByText('atualizado às 10:25:31')).toBeInTheDocument()

    unmount()

    render(
      <BarraFiltros
        filtros={filtrosPadrao}
        aoAlterar={vi.fn()}
        autoRefresh={false}
        aoAlternarAutoRefresh={vi.fn()}
        aoRecarregar={vi.fn()}
        atualizadoEm="10:25:31"
      />,
    )

    expect(screen.getByText('pausado')).toBeInTheDocument()
  })

  it('dispara a recarga manual', async () => {
    const { aoRecarregar } = montar()

    await userEvent.click(screen.getByRole('button', { name: 'Atualizar agora' }))

    expect(aoRecarregar).toHaveBeenCalledOnce()
  })
})

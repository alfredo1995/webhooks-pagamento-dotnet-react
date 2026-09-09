import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ModalEvento } from './ModalEvento'
import { api } from '../api/client'
import { umEvento, umEventoComErro } from '../testes/fabricas'

vi.mock('../api/client', () => ({ api: { obterEvento: vi.fn() } }))

const payload =
  '{"id_transacao":"TX-001","id_contrato":"CT-2026-0001","valor":1250.90,"status":"CONFIRMADO"}'

describe('ModalEvento', () => {
  beforeEach(() => {
    vi.mocked(api.obterEvento).mockReset()
    vi.mocked(api.obterEvento).mockResolvedValue({ resumo: umEvento(), payloadBruto: payload })
  })

  it('anuncia-se como dialogo modal', () => {
    render(<ModalEvento evento={umEvento()} aoFechar={vi.fn()} />)

    expect(screen.getByRole('dialog')).toHaveAttribute('aria-modal', 'true')
  })

  it('mostra as propriedades do evento', () => {
    render(
      <ModalEvento
        evento={umEvento({ tentativas: 3, origemParceiro: 'banco-parceiro' })}
        aoFechar={vi.fn()}
      />,
    )

    expect(screen.getByText('TX-001')).toBeInTheDocument()
    expect(screen.getByText('CT-2026-0001')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('banco-parceiro')).toBeInTheDocument()
  })

  /** O corpo original é a prova do que o parceiro mandou; reindentar é só leitura. */
  it('reindenta o payload sem esconder o original', async () => {
    render(<ModalEvento evento={umEvento()} aoFechar={vi.fn()} />)

    const bloco = await screen.findByText(/"id_transacao": "TX-001"/)

    expect(bloco.textContent).toContain('\n')
    expect(bloco.textContent).toContain('CT-2026-0001')
  })

  it('mostra cru o payload que nao e JSON valido', async () => {
    vi.mocked(api.obterEvento).mockResolvedValue({
      resumo: umEvento(),
      payloadBruto: 'isso nao e json',
    })

    render(<ModalEvento evento={umEvento()} aoFechar={vi.fn()} />)

    expect(await screen.findByText('isso nao e json')).toBeInTheDocument()
  })

  it('destaca o motivo da falha quando houver', () => {
    render(<ModalEvento evento={umEventoComErro()} aoFechar={vi.fn()} />)

    expect(screen.getByRole('alert')).toHaveTextContent('id_contrato e obrigatorio.')
  })

  it('nao mostra bloco de falha em evento bem-sucedido', () => {
    render(<ModalEvento evento={umEvento()} aoFechar={vi.fn()} />)

    expect(screen.queryByText('Motivo da falha')).not.toBeInTheDocument()
  })

  it('mostra o erro quando o payload nao carrega', async () => {
    vi.mocked(api.obterEvento).mockRejectedValue(new Error('Evento não encontrado.'))

    render(<ModalEvento evento={umEvento()} aoFechar={vi.fn()} />)

    expect(await screen.findByText('Evento não encontrado.')).toBeInTheDocument()
  })

  it('fecha no botao, no clique fora e no Escape', async () => {
    const aoFechar = vi.fn()
    const { unmount } = render(<ModalEvento evento={umEvento()} aoFechar={aoFechar} />)

    await userEvent.click(screen.getByRole('button', { name: 'Fechar' }))
    expect(aoFechar).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole('dialog'))
    expect(aoFechar).toHaveBeenCalledTimes(2)

    await userEvent.keyboard('{Escape}')
    expect(aoFechar).toHaveBeenCalledTimes(3)

    unmount()
  })

  /** Clicar no conteudo nao pode fechar: seria impossivel selecionar o payload. */
  it('nao fecha ao clicar dentro do modal', async () => {
    const aoFechar = vi.fn()

    render(<ModalEvento evento={umEvento()} aoFechar={aoFechar} />)

    await userEvent.click(screen.getByText('TX-001'))

    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('para de ouvir o Escape depois de fechado', async () => {
    const aoFechar = vi.fn()
    const { unmount } = render(<ModalEvento evento={umEvento()} aoFechar={aoFechar} />)

    await waitFor(() => expect(api.obterEvento).toHaveBeenCalled())

    unmount()
    await userEvent.keyboard('{Escape}')

    expect(aoFechar).not.toHaveBeenCalled()
  })
})

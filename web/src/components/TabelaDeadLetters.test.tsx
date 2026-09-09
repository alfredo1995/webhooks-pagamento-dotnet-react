import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TabelaDeadLetters } from './TabelaDeadLetters'
import { umaCarta } from '../testes/fabricas'

function montar(sobrescritas: Partial<Parameters<typeof TabelaDeadLetters>[0]> = {}) {
  const aoReprocessar = vi.fn()

  render(
    <TabelaDeadLetters
      cartas={[umaCarta()]}
      carregando={false}
      podeReprocessar
      reprocessando={null}
      aoReprocessar={aoReprocessar}
      {...sobrescritas}
    />,
  )

  return { aoReprocessar }
}

describe('TabelaDeadLetters', () => {
  it('marca a carta pendente e mostra o motivo da ultima falha', () => {
    montar()

    expect(screen.getByText('TX-ESTORNO').closest('tr')).toHaveClass('linha--erro')
    expect(screen.getByText(/⚠ Parado/)).toBeInTheDocument()
    expect(screen.getByText(/excede o saldo liquido/)).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('mostra quem reprocessou, no lugar do botao, quando ja foi tratada', () => {
    montar({
      cartas: [
        umaCarta({
          pendente: false,
          reprocessadoPor: 'admin',
          reprocessadoEmUtc: '2026-03-01T13:00:00Z',
        }),
      ],
    })

    expect(screen.getByText('Reprocessado')).toBeInTheDocument()
    expect(screen.getByText(/por admin em/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reprocessar' })).not.toBeInTheDocument()
    expect(screen.getByText('TX-ESTORNO').closest('tr')).not.toHaveClass('linha--erro')
  })

  it('reprocessa pelo id do evento, e nao pelo id da carta', async () => {
    const { aoReprocessar } = montar()

    await userEvent.click(screen.getByRole('button', { name: 'Reprocessar' }))

    expect(aoReprocessar).toHaveBeenCalledWith('44444444-4444-4444-4444-444444444444')
  })

  /**
   * Reprocessar movimenta saldo de contrato: e escrita, e so administrador tem
   * o papel. O botao desabilitado explica o porque no title — botao morto sem
   * explicacao vira chamado de suporte.
   */
  it('desabilita o reprocessamento para quem nao é administrador, dizendo o motivo', () => {
    montar({ podeReprocessar: false })

    const botao = screen.getByRole('button', { name: 'Reprocessar' })

    expect(botao).toBeDisabled()
    expect(botao).toHaveAttribute('title', 'Somente administradores podem reprocessar')
  })

  it('explica o efeito do botao para quem pode usa-lo', () => {
    montar()

    expect(screen.getByRole('button', { name: 'Reprocessar' })).toHaveAttribute(
      'title',
      'Devolve o evento para a fila com um orçamento novo de tentativas',
    )
  })

  /** Sem isso, dois cliques rapidos gerariam dois reprocessamentos. */
  it('trava o botao da carta em andamento, e so o dela', () => {
    render(
      <TabelaDeadLetters
        cartas={[
          umaCarta({ id: 'c1', eventoId: 'e1', idTransacao: 'TX-A' }),
          umaCarta({ id: 'c2', eventoId: 'e2', idTransacao: 'TX-B' }),
        ]}
        carregando={false}
        podeReprocessar
        reprocessando="e1"
        aoReprocessar={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: 'Enviando…' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Reprocessar' })).toBeEnabled()
  })

  it('distingue "carregando" de fila vazia', () => {
    const { unmount } = render(
      <TabelaDeadLetters
        cartas={[]}
        carregando
        podeReprocessar
        reprocessando={null}
        aoReprocessar={vi.fn()}
      />,
    )

    expect(screen.getByText('Carregando a dead-letter queue…')).toBeInTheDocument()

    unmount()

    render(
      <TabelaDeadLetters
        cartas={[]}
        carregando={false}
        podeReprocessar
        reprocessando={null}
        aoReprocessar={vi.fn()}
      />,
    )

    expect(
      screen.getByText('Nenhum evento esgotou as retentativas automáticas.'),
    ).toBeInTheDocument()
  })
})

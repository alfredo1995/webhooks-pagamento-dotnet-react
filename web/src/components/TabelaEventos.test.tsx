import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TabelaEventos } from './TabelaEventos'
import { umEvento, umEventoComErro } from '../testes/fabricas'

describe('TabelaEventos', () => {
  /**
   * O enunciado pede alerta visual claro para o evento que falhou. "Claro" aqui
   * e a linha marcada, o badge de erro e o motivo legivel na propria linha —
   * descobrir por que falhou nao deveria exigir abrir um modal.
   */
  it('destaca a linha do evento com erro e mostra o motivo sem abrir nada', () => {
    render(
      <TabelaEventos
        eventos={[umEvento(), umEventoComErro()]}
        carregando={false}
        aoSelecionar={vi.fn()}
      />,
    )

    const linhaComErro = screen.getByText('TX-INVALIDA').closest('tr')
    const linhaOk = screen.getByText('TX-001').closest('tr')

    expect(linhaComErro).toHaveClass('linha--erro')
    expect(linhaOk).not.toHaveClass('linha--erro')
    expect(
      screen.getByText(/id_contrato e obrigatorio\. \| valor deve ser maior que zero\./),
    ).toBeInTheDocument()
  })

  it('mostra o motivo tambem no title, porque a coluna trunca o texto longo', () => {
    const motivo = 'Estorno de 99999.00 excede o saldo liquido de 3200.00 do contrato CT-2026-0002.'

    render(
      <TabelaEventos
        eventos={[umEventoComErro({ motivoFalha: motivo })]}
        carregando={false}
        aoSelecionar={vi.fn()}
      />,
    )

    expect(screen.getByText(motivo)).toHaveAttribute('title', motivo)
  })

  it('formata valor, duracao e campos ausentes', () => {
    render(
      <TabelaEventos
        eventos={[umEvento({ valor: 1250.9, duracaoProcessamentoMs: 2010 })]}
        carregando={false}
        aoSelecionar={vi.fn()}
      />,
    )

    expect(screen.getByText(/1\.250,90/)).toBeInTheDocument()
    expect(screen.getByText('2.01 s')).toBeInTheDocument()
  })

  it('troca contrato, valor e pagamento ausentes por travessao', () => {
    render(
      <TabelaEventos eventos={[umEventoComErro()]} carregando={false} aoSelecionar={vi.fn()} />,
    )

    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(3)
  })

  it('abre o detalhe ao clicar na linha', async () => {
    const aoSelecionar = vi.fn()
    const evento = umEvento()

    render(<TabelaEventos eventos={[evento]} carregando={false} aoSelecionar={aoSelecionar} />)

    await userEvent.click(screen.getByText('TX-001'))

    expect(aoSelecionar).toHaveBeenCalledWith(evento)
  })

  /** A linha e clicavel, entao precisa responder ao teclado como um botao. */
  it('abre o detalhe pelo teclado', async () => {
    const aoSelecionar = vi.fn()
    const evento = umEvento()

    render(<TabelaEventos eventos={[evento]} carregando={false} aoSelecionar={aoSelecionar} />)

    screen.getByRole('button', { name: /TX-001/ }).focus()
    await userEvent.keyboard('{Enter}')

    expect(aoSelecionar).toHaveBeenCalledWith(evento)
  })

  it('distingue "carregando" de "nao ha nada"', () => {
    const { unmount } = render(
      <TabelaEventos eventos={[]} carregando aoSelecionar={vi.fn()} />,
    )

    expect(screen.getByText('Carregando eventos…')).toBeInTheDocument()

    unmount()

    render(<TabelaEventos eventos={[]} carregando={false} aoSelecionar={vi.fn()} />)

    expect(
      screen.getByText('Nenhum evento encontrado para os filtros aplicados.'),
    ).toBeInTheDocument()
  })

  /**
   * Recarga em polling nao pode limpar a tabela: piscar a cada tres segundos
   * tornaria o painel inutilizavel para leitura.
   */
  it('mantem os itens na tela durante uma recarga', () => {
    render(<TabelaEventos eventos={[umEvento()]} carregando aoSelecionar={vi.fn()} />)

    expect(screen.getByText('TX-001')).toBeInTheDocument()
    expect(screen.queryByText('Carregando eventos…')).not.toBeInTheDocument()
  })
})

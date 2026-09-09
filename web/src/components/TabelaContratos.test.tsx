import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TabelaContratos } from './TabelaContratos'
import { umContrato } from '../testes/fabricas'

describe('TabelaContratos', () => {
  it('mostra pago, estornado e saldo liquido consolidados', () => {
    render(
      <TabelaContratos
        contratos={[
          umContrato({
            idContrato: 'CT-2026-0002',
            valorTotalPago: 109599,
            valorTotalEstornado: 99999,
            saldoLiquido: 9600,
            quantidadePagamentos: 4,
          }),
        ]}
        carregando={false}
      />,
    )

    expect(screen.getByText('CT-2026-0002')).toBeInTheDocument()
    expect(screen.getByText(/109\.599,00/)).toBeInTheDocument()
    expect(screen.getByText(/99\.999,00/)).toBeInTheDocument()
    expect(screen.getByText(/9\.600,00/)).toBeInTheDocument()
    expect(screen.getByText('4')).toBeInTheDocument()
  })

  /** O saldo é o número que decide se um estorno cabe: precisa saltar aos olhos. */
  it('destaca o saldo liquido em relacao as outras colunas de valor', () => {
    // Valores distintos de proposito: com pago e saldo iguais, a busca por texto
    // acharia as duas celulas e o teste passaria sem provar qual esta destacada.
    render(
      <TabelaContratos
        contratos={[
          umContrato({ valorTotalPago: 2000, valorTotalEstornado: 269.1, saldoLiquido: 1730.9 }),
        ]}
        carregando={false}
      />,
    )

    expect(screen.getByText(/1\.730,90/)).toHaveClass('forte')
    expect(screen.getByText(/2\.000,00/)).not.toHaveClass('forte')
  })

  it('mostra R$ 0,00 no estorno em vez de travessao, porque zero e informacao', () => {
    render(
      <TabelaContratos contratos={[umContrato({ valorTotalEstornado: 0 })]} carregando={false} />,
    )

    expect(screen.getByText(/0,00/)).toBeInTheDocument()
  })

  it('lista um contrato por linha', () => {
    render(
      <TabelaContratos
        contratos={[umContrato({ idContrato: 'CT-1' }), umContrato({ idContrato: 'CT-2' })]}
        carregando={false}
      />,
    )

    expect(screen.getAllByRole('row')).toHaveLength(3)
  })

  it('distingue "carregando" de "nenhum contrato ainda"', () => {
    const { unmount } = render(<TabelaContratos contratos={[]} carregando />)

    expect(screen.getByText('Carregando contratos…')).toBeInTheDocument()

    unmount()

    render(<TabelaContratos contratos={[]} carregando={false} />)

    expect(screen.getByText('Nenhum contrato consolidado ainda.')).toBeInTheDocument()
  })
})

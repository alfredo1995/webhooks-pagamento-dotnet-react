import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StatusBadge } from './StatusBadge'
import type { StatusProcessamento } from '../api/types'

describe('StatusBadge', () => {
  it.each<[StatusProcessamento, string]>([
    ['Recebido', 'Na fila'],
    ['Processando', 'Processando'],
    ['Processado', 'Processado'],
    ['Invalido', 'Inválido'],
    ['Falha', 'Falha'],
    ['AguardandoRetentativa', 'Em retentativa'],
  ])('traduz o status tecnico %s para "%s"', (status, rotulo) => {
    render(<StatusBadge status={status} resultado="Sucesso" />)

    expect(screen.getByText(rotulo)).toBeInTheDocument()
  })

  /**
   * A cor vem do resultado, nao do status: "Inválido" e "Falha" sao o mesmo
   * alarme visual, que e o que o operador enxerga de longe, sem perder a
   * distincao no texto.
   */
  it('pinta pelo resultado e nao pelo status', () => {
    const { unmount } = render(<StatusBadge status="Invalido" resultado="Erro" />)
    expect(screen.getByText(/Inválido/)).toHaveClass('badge--erro')
    unmount()

    render(<StatusBadge status="Falha" resultado="Erro" />)
    expect(screen.getByText(/Falha/)).toHaveClass('badge--erro')
  })

  /** Cor sozinha nao serve a quem nao a distingue: o erro tambem ganha simbolo. */
  it('acrescenta um simbolo no resultado de erro, decorativo para leitor de tela', () => {
    const { container } = render(<StatusBadge status="Falha" resultado="Erro" />)

    const simbolo = container.querySelector('[aria-hidden="true"]')

    expect(simbolo).toHaveTextContent('⚠')
  })

  it('nao acrescenta simbolo quando nao e erro', () => {
    const { container } = render(<StatusBadge status="Processado" resultado="Sucesso" />)

    expect(container.querySelector('[aria-hidden="true"]')).toBeNull()
  })

  it('usa a classe do resultado pendente', () => {
    render(<StatusBadge status="Recebido" resultado="Pendente" />)

    expect(screen.getByText('Na fila')).toHaveClass('badge--pendente')
  })
})

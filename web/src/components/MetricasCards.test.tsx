import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MetricasCards } from './MetricasCards'
import { asMetricas } from '../testes/fabricas'

function valorDoCartao(rotulo: string): string | null {
  return screen.getByText(rotulo).parentElement?.querySelector('.card__valor')?.textContent ?? null
}

describe('MetricasCards', () => {
  it('mostra cada metrica no seu cartao', () => {
    render(
      <MetricasCards
        metricas={asMetricas({
          total: 26,
          sucesso: 19,
          erro: 7,
          emRetentativa: 1,
          deadLetters: 2,
          pendentes: 3,
          naFila: 4,
        })}
      />,
    )

    expect(valorDoCartao('Eventos recebidos')).toBe('26')
    expect(valorDoCartao('Processados')).toBe('19')
    expect(valorDoCartao('Com erro')).toBe('7')
    expect(valorDoCartao('Em retentativa')).toBe('1')
    expect(valorDoCartao('Na dead-letter')).toBe('2')
    expect(valorDoCartao('Pendentes')).toBe('3')
    expect(valorDoCartao('Aguardando na fila')).toBe('4')
  })

  /**
   * "Em retentativa" e "Na dead-letter" contam a mesma historia em dois tempos:
   * o que o sistema ainda tenta resolver sozinho e o que ja desistiu. Sem
   * separar, um numero de erros crescente nao diria se piora ou se resolve.
   */
  it('separa o que ainda esta sendo tentado do que ja desistiu', () => {
    render(<MetricasCards metricas={asMetricas({ emRetentativa: 3, deadLetters: 1 })} />)

    expect(screen.getByText('Em retentativa')).toBeInTheDocument()
    expect(screen.getByText('Na dead-letter')).toBeInTheDocument()
  })

  /** Antes da primeira resposta, travessao — nao zero, que seria mentira. */
  it('mostra travessao enquanto as metricas nao chegaram', () => {
    render(<MetricasCards metricas={null} />)

    expect(screen.getAllByText('—')).toHaveLength(7)
  })

  it('mostra zero de verdade quando o valor e zero', () => {
    render(<MetricasCards metricas={asMetricas({ erro: 0 })} />)

    expect(valorDoCartao('Com erro')).toBe('0')
  })

  it('da tom de erro aos cartoes de falha, e nao aos demais', () => {
    const { container } = render(<MetricasCards metricas={asMetricas()} />)

    expect(screen.getByText('Com erro').closest('article')).toHaveClass('card--erro')
    expect(screen.getByText('Processados').closest('article')).toHaveClass('card--sucesso')
    expect(container.querySelectorAll('article')).toHaveLength(7)
  })
})

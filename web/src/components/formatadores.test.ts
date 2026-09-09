import { describe, expect, it } from 'vitest'
import {
  formatarData,
  formatarDataHora,
  formatarDuracao,
  formatarHora,
  formatarMoeda,
} from './formatadores'

describe('formatarMoeda', () => {
  it('formata em real brasileiro', () => {
    // O separador de milhar do Intl e um espaco nao-quebravel, nao um espaco
    // comum: comparar com string literal falharia por um caractere invisivel.
    expect(formatarMoeda(1250.9).replace(/ /g, ' ')).toBe('R$ 1.250,90')
  })

  it('mostra travessao para nulo e indefinido, e nao "R$ 0,00"', () => {
    expect(formatarMoeda(null)).toBe('—')
    expect(formatarMoeda(undefined)).toBe('—')
  })

  /** Zero e um valor, nao ausencia: trocar por travessao esconderia o dado. */
  it('formata o zero em vez de trata-lo como ausente', () => {
    expect(formatarMoeda(0).replace(/ /g, ' ')).toBe('R$ 0,00')
  })

  it('formata valor negativo', () => {
    expect(formatarMoeda(-50)).toContain('50,00')
  })
})

describe('formatarDataHora', () => {
  /**
   * A API devolve DateTime sem sufixo Z. Sem completar o Z, o JS interpreta
   * como hora local e o painel mostraria o horario deslocado do fuso.
   */
  it('trata data sem sufixo como UTC', () => {
    const semZ = formatarDataHora('2026-03-01T12:00:00')
    const comZ = formatarDataHora('2026-03-01T12:00:00Z')

    expect(semZ).toBe(comZ)
  })

  it('respeita o deslocamento quando ele vem explicito', () => {
    expect(formatarDataHora('2026-03-01T12:00:00+03:00')).toBe(
      formatarDataHora('2026-03-01T09:00:00Z'),
    )
  })

  it('mostra travessao para nulo, vazio e data invalida', () => {
    expect(formatarDataHora(null)).toBe('—')
    expect(formatarDataHora(undefined)).toBe('—')
    expect(formatarDataHora('')).toBe('—')
    expect(formatarDataHora('nao-e-data')).toBe('—')
  })

  it('inclui data e hora com segundos', () => {
    expect(formatarDataHora('2026-03-01T12:00:00Z')).toMatch(/\d{2}\/\d{2}\/\d{4}.*\d{2}:\d{2}:\d{2}/)
  })
})

describe('formatarData', () => {
  it('mostra so a data', () => {
    expect(formatarData('2026-03-01T12:00:00Z')).toMatch(/^\d{2}\/\d{2}\/\d{4}$/)
  })

  it('mostra travessao para entrada invalida', () => {
    expect(formatarData('xpto')).toBe('—')
    expect(formatarData(null)).toBe('—')
  })
})

describe('formatarDuracao', () => {
  it('usa milissegundos abaixo de um segundo', () => {
    expect(formatarDuracao(0)).toBe('0 ms')
    expect(formatarDuracao(999)).toBe('999 ms')
  })

  it('vira segundos com duas casas a partir de mil', () => {
    expect(formatarDuracao(1000)).toBe('1.00 s')
    expect(formatarDuracao(2010)).toBe('2.01 s')
  })

  it('mostra travessao quando o evento ainda nao foi processado', () => {
    expect(formatarDuracao(null)).toBe('—')
    expect(formatarDuracao(undefined)).toBe('—')
  })
})

describe('formatarHora', () => {
  it('mostra travessao quando nunca atualizou', () => {
    expect(formatarHora(null)).toBe('—')
  })

  it('formata a hora local', () => {
    expect(formatarHora(new Date('2026-03-01T12:00:00Z'))).toMatch(/\d{2}:\d{2}:\d{2}/)
  })
})

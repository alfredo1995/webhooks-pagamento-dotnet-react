import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAutoRefresh } from './useAutoRefresh'

describe('useAutoRefresh', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('busca na montagem e expoe os dados', async () => {
    const buscar = vi.fn().mockResolvedValue('dados')

    const { result } = renderHook(() => useAutoRefresh(buscar, 3000, true))

    await waitFor(() => expect(result.current.dados).toBe('dados'))
    expect(result.current.carregando).toBe(false)
    expect(result.current.atualizadoEm).toBeInstanceOf(Date)
  })

  it('repete no intervalo quando ativo', async () => {
    const buscar = vi.fn().mockResolvedValue('dados')

    renderHook(() => useAutoRefresh(buscar, 3000, true))

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(1))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(6000)
    })

    expect(buscar).toHaveBeenCalledTimes(3)
  })

  /** Pausado, a primeira carga ainda acontece — pausa nao e "nao mostrar nada". */
  it('busca uma vez e nao repete quando pausado', async () => {
    const buscar = vi.fn().mockResolvedValue('dados')

    renderHook(() => useAutoRefresh(buscar, 3000, false))

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(1))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000)
    })

    expect(buscar).toHaveBeenCalledTimes(1)
  })

  /**
   * `habilitado` decide se a consulta deve existir; `ativo`, se ela se repete.
   * Sem a separacao, a aba escondida da auditoria ainda faria a primeira busca —
   * e o painel do operador exibia o 403 como se a API estivesse fora do ar.
   */
  it('nao busca nada quando desabilitado', async () => {
    const buscar = vi.fn().mockResolvedValue('dados')

    renderHook(() => useAutoRefresh(buscar, 3000, true, false))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000)
    })

    expect(buscar).not.toHaveBeenCalled()
  })

  it('passa a buscar quando é habilitado depois', async () => {
    const buscar = vi.fn().mockResolvedValue('dados')

    const { rerender } = renderHook(
      ({ habilitado }) => useAutoRefresh(buscar, 3000, true, habilitado),
      { initialProps: { habilitado: false } },
    )

    expect(buscar).not.toHaveBeenCalled()

    rerender({ habilitado: true })

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(1))
  })

  /**
   * Regressao: o hook guardava `buscar` num ref e o efeito nao dependia dele,
   * entao um filtro novo so valia no tick seguinte — e com a atualizacao
   * pausada, nunca. A tabela seguia mostrando o resultado do filtro anterior
   * sem nada na tela indicando isso.
   */
  it('refaz a busca assim que a consulta muda, mesmo com a atualizacao pausada', async () => {
    const buscarTodos = vi.fn().mockResolvedValue('todos')
    const buscarErros = vi.fn().mockResolvedValue('erros')

    const { rerender, result } = renderHook(
      ({ buscar }) => useAutoRefresh(buscar, 3000, false),
      { initialProps: { buscar: buscarTodos } },
    )

    await waitFor(() => expect(result.current.dados).toBe('todos'))

    rerender({ buscar: buscarErros })

    await waitFor(() => expect(result.current.dados).toBe('erros'))
    expect(buscarErros).toHaveBeenCalledTimes(1)
  })

  it('nao refaz a busca quando a consulta e a mesma entre renderizacoes', async () => {
    const buscar = vi.fn().mockResolvedValue('dados')

    const { rerender } = renderHook(() => useAutoRefresh(buscar, 3000, false))

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(1))

    rerender()
    rerender()

    expect(buscar).toHaveBeenCalledTimes(1)
  })

  it('guarda a mensagem de erro sem apagar os dados que ja estavam na tela', async () => {
    const buscar = vi
      .fn()
      .mockResolvedValueOnce('dados')
      .mockRejectedValue(new Error('API fora do ar'))

    const { result } = renderHook(() => useAutoRefresh(buscar, 3000, true))

    await waitFor(() => expect(result.current.dados).toBe('dados'))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000)
    })

    await waitFor(() => expect(result.current.erro).toBe('API fora do ar'))
    expect(result.current.dados).toBe('dados')
  })

  it('descreve o erro quando o que foi lancado nao e um Error', async () => {
    const buscar = vi.fn().mockRejectedValue('string solta')

    const { result } = renderHook(() => useAutoRefresh(buscar, 3000, true))

    await waitFor(() => expect(result.current.erro).toBe('Falha ao consultar a API.'))
  })

  /** Resposta lenta nao pode sobrescrever uma recente. */
  it('aborta a requisicao anterior antes de disparar a proxima', async () => {
    const sinais: AbortSignal[] = []
    const buscar = vi.fn(async (signal: AbortSignal) => {
      sinais.push(signal)

      return 'dados'
    })

    const { result } = renderHook(() => useAutoRefresh(buscar, 3000, true))

    await waitFor(() => expect(sinais).toHaveLength(1))

    act(() => result.current.recarregar())

    await waitFor(() => expect(sinais).toHaveLength(2))
    expect(sinais[0]?.aborted).toBe(true)
    expect(sinais[1]?.aborted).toBe(false)
  })

  it('nao registra erro quando a falha veio de um abort', async () => {
    const buscar = vi.fn(async (signal: AbortSignal) => {
      await new Promise((resolve) => setTimeout(resolve, 50))

      if (signal.aborted) {
        throw new Error('AbortError')
      }

      return 'dados'
    })

    const { result } = renderHook(() => useAutoRefresh(buscar, 3000, true))

    act(() => result.current.recarregar())

    await act(async () => {
      await vi.advanceTimersByTimeAsync(100)
    })

    expect(result.current.erro).toBeNull()
  })

  it('aborta ao desmontar, para nao atualizar estado de componente que saiu', async () => {
    const sinais: AbortSignal[] = []
    const buscar = vi.fn(async (signal: AbortSignal) => {
      sinais.push(signal)

      return 'dados'
    })

    const { unmount } = renderHook(() => useAutoRefresh(buscar, 3000, true))

    await waitFor(() => expect(sinais).toHaveLength(1))

    unmount()

    expect(sinais[0]?.aborted).toBe(true)
  })

  it('recarrega sob demanda', async () => {
    const buscar = vi.fn().mockResolvedValue('dados')

    const { result } = renderHook(() => useAutoRefresh(buscar, 3000, false))

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(1))

    act(() => result.current.recarregar())

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(2))
  })
})

import { useCallback, useEffect, useRef, useState } from 'react'

interface Estado<T> {
  dados: T | null
  carregando: boolean
  erro: string | null
  atualizadoEm: Date | null
}

/**
 * Busca dados e reexecuta em intervalo fixo.
 *
 * Dois cuidados que a versao ingenua deste hook nao tem: a requisicao anterior e
 * abortada antes de disparar a proxima, para que uma resposta lenta nao sobrescreva
 * uma recente; e o `carregando` so aparece na primeira carga, senao a tabela
 * piscaria a cada ciclo de atualizacao.
 */
export function useAutoRefresh<T>(
  buscar: (signal: AbortSignal) => Promise<T>,
  intervaloMs: number,
  ativo: boolean,
): Estado<T> & { recarregar: () => void } {
  const [estado, setEstado] = useState<Estado<T>>({
    dados: null,
    carregando: true,
    erro: null,
    atualizadoEm: null,
  })

  const controllerRef = useRef<AbortController | null>(null)
  const buscarRef = useRef(buscar)
  buscarRef.current = buscar

  const executar = useCallback(async () => {
    controllerRef.current?.abort()

    const controller = new AbortController()
    controllerRef.current = controller

    try {
      const dados = await buscarRef.current(controller.signal)

      setEstado({ dados, carregando: false, erro: null, atualizadoEm: new Date() })
    } catch (erro) {
      if (controller.signal.aborted) {
        return
      }

      setEstado((anterior) => ({
        ...anterior,
        carregando: false,
        erro: erro instanceof Error ? erro.message : 'Falha ao consultar a API.',
      }))
    }
  }, [])

  useEffect(() => {
    void executar()

    if (!ativo) {
      return
    }

    const timer = window.setInterval(() => void executar(), intervaloMs)

    return () => window.clearInterval(timer)
  }, [executar, intervaloMs, ativo])

  useEffect(() => () => controllerRef.current?.abort(), [])

  return { ...estado, recarregar: () => void executar() }
}

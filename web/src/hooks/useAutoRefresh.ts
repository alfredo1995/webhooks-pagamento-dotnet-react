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
 * Três cuidados que a versao ingenua deste hook nao tem. A requisicao anterior e
 * abortada antes de disparar a proxima, para que uma resposta lenta nao
 * sobrescreva uma recente. O `carregando` so aparece na primeira carga, senao a
 * tabela piscaria a cada ciclo de atualizacao.
 *
 * E `habilitado` e separado de `ativo` de proposito: `ativo` decide se o
 * intervalo roda — e o botao de pausa —, enquanto `habilitado` decide se a
 * consulta deve existir. Sem essa separacao, uma aba invisivel ainda faria a
 * primeira busca; foi assim que o painel do operador chegava a pedir a trilha de
 * auditoria e exibia o 403 como se a API estivesse fora do ar.
 */
export function useAutoRefresh<T>(
  buscar: (signal: AbortSignal) => Promise<T>,
  intervaloMs: number,
  ativo: boolean,
  habilitado = true,
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

  const habilitadoRef = useRef(habilitado)
  habilitadoRef.current = habilitado

  const executar = useCallback(async () => {
    if (!habilitadoRef.current) {
      return
    }

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
    if (!habilitado) {
      return
    }

    void executar()

    if (!ativo) {
      return
    }

    const timer = window.setInterval(() => void executar(), intervaloMs)

    return () => window.clearInterval(timer)

    // `buscar` entra nas dependencias para que trocar de consulta refaca a busca
    // na hora. Sem ele, um filtro novo so valeria no proximo tick — e com a
    // atualizacao automatica pausada, nunca: a tabela ficaria mostrando o
    // resultado do filtro anterior sem nada indicando isso.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [executar, intervaloMs, ativo, habilitado, buscar])

  useEffect(() => () => controllerRef.current?.abort(), [])

  return { ...estado, recarregar: () => void executar() }
}

export type Resultado = 'Sucesso' | 'Erro' | 'Pendente'

export type StatusProcessamento =
  | 'Recebido'
  | 'Processando'
  | 'Processado'
  | 'Invalido'
  | 'Falha'

export interface EventoResumo {
  id: string
  idTransacao: string
  idContrato: string | null
  valor: number | null
  dataPagamento: string | null
  statusPagamento: string | null
  status: StatusProcessamento
  resultado: Resultado
  motivoFalha: string | null
  tentativas: number
  origemParceiro: string
  recebidoEmUtc: string
  processadoEmUtc: string | null
  duracaoProcessamentoMs: number | null
}

export interface EventoDetalhe {
  resumo: EventoResumo
  payloadBruto: string
}

export interface ContratoResumo {
  idContrato: string
  valorTotalPago: number
  valorTotalEstornado: number
  saldoLiquido: number
  quantidadePagamentos: number
  quantidadeEstornos: number
  ultimoStatus: string
  ultimaTransacao: string
  ultimoPagamentoUtc: string | null
  atualizadoEmUtc: string
}

export interface Metricas {
  total: number
  pendentes: number
  sucesso: number
  erro: number
  naFila: number
  porStatus: Record<string, number>
}

export interface PagedResult<T> {
  itens: T[]
  pagina: number
  tamanhoPagina: number
  totalItens: number
  totalPaginas: number
  temProximaPagina: boolean
}

export interface FiltrosEventos {
  resultado: Resultado | 'Todos'
  idContrato: string
  idTransacao: string
  pagina: number
}

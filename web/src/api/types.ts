export type Resultado = 'Sucesso' | 'Erro' | 'Pendente'

export type StatusProcessamento =
  | 'Recebido'
  | 'Processando'
  | 'Processado'
  | 'Invalido'
  | 'Falha'
  | 'AguardandoRetentativa'

export type Papel = 'operador' | 'administrador'

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
  proximaTentativaEmUtc: string | null
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

export interface DeadLetterResumo {
  id: string
  eventoId: string
  idTransacao: string
  idContrato: string | null
  motivo: string
  tentativas: number
  criadoEmUtc: string
  reprocessadoEmUtc: string | null
  reprocessadoPor: string | null
  pendente: boolean
}

export interface AuditoriaResumo {
  id: string
  usuario: string
  papel: string
  metodo: string
  recurso: string
  consulta: string | null
  ipOrigem: string
  statusHttp: number
  traceId: string | null
  emUtc: string
}

export interface Metricas {
  total: number
  pendentes: number
  sucesso: number
  erro: number
  emRetentativa: number
  naFila: number
  outboxPendente: number
  deadLetters: number
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

export interface Sessao {
  token: string
  expiraEmUtc: string
  usuario: string
  nome: string
  papel: Papel
}

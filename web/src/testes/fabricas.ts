import type {
  AuditoriaResumo,
  ContratoResumo,
  DeadLetterResumo,
  EventoResumo,
  Metricas,
  PagedResult,
  Sessao,
} from '../api/types'

/**
 * Fabricas com valores padrao plausiveis e sobrescrita por parametro.
 *
 * Montar o objeto inteiro em cada teste afogaria a assercao: quem le precisa
 * enxergar de imediato qual campo importa naquele caso, e nao os outros quinze
 * que so estao ali para satisfazer o tipo.
 */
export function umEvento(parcial: Partial<EventoResumo> = {}): EventoResumo {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    idTransacao: 'TX-001',
    idContrato: 'CT-2026-0001',
    valor: 1250.9,
    dataPagamento: '2026-03-01T12:00:00Z',
    statusPagamento: 'Confirmado',
    status: 'Processado',
    resultado: 'Sucesso',
    motivoFalha: null,
    tentativas: 1,
    proximaTentativaEmUtc: null,
    origemParceiro: 'banco-parceiro',
    recebidoEmUtc: '2026-03-01T12:00:01Z',
    processadoEmUtc: '2026-03-01T12:00:03Z',
    duracaoProcessamentoMs: 2010,
    ...parcial,
  }
}

export function umEventoComErro(parcial: Partial<EventoResumo> = {}): EventoResumo {
  return umEvento({
    id: '22222222-2222-2222-2222-222222222222',
    idTransacao: 'TX-INVALIDA',
    idContrato: null,
    valor: null,
    statusPagamento: null,
    status: 'Invalido',
    resultado: 'Erro',
    motivoFalha: 'id_contrato e obrigatorio. | valor deve ser maior que zero.',
    duracaoProcessamentoMs: null,
    processadoEmUtc: null,
    ...parcial,
  })
}

export function umContrato(parcial: Partial<ContratoResumo> = {}): ContratoResumo {
  return {
    idContrato: 'CT-2026-0001',
    valorTotalPago: 1730.9,
    valorTotalEstornado: 0,
    saldoLiquido: 1730.9,
    quantidadePagamentos: 2,
    quantidadeEstornos: 0,
    ultimoStatus: 'Confirmado',
    ultimaTransacao: 'TX-001',
    ultimoPagamentoUtc: '2026-03-01T12:00:00Z',
    atualizadoEmUtc: '2026-03-01T12:00:03Z',
    ...parcial,
  }
}

export function umaCarta(parcial: Partial<DeadLetterResumo> = {}): DeadLetterResumo {
  return {
    id: '33333333-3333-3333-3333-333333333333',
    eventoId: '44444444-4444-4444-4444-444444444444',
    idTransacao: 'TX-ESTORNO',
    idContrato: 'CT-2026-0002',
    motivo: 'Estorno de 99999.00 excede o saldo liquido de 3200.00 do contrato.',
    tentativas: 5,
    criadoEmUtc: '2026-03-01T12:10:00Z',
    reprocessadoEmUtc: null,
    reprocessadoPor: null,
    pendente: true,
    ...parcial,
  }
}

export function umRegistroDeAuditoria(parcial: Partial<AuditoriaResumo> = {}): AuditoriaResumo {
  return {
    id: '55555555-5555-5555-5555-555555555555',
    usuario: 'admin',
    papel: 'administrador',
    metodo: 'GET',
    recurso: '/api/eventos',
    consulta: '?resultado=Erro',
    ipOrigem: '172.20.0.6',
    statusHttp: 200,
    traceId: 'abc123',
    emUtc: '2026-03-01T12:20:00Z',
    ...parcial,
  }
}

export function asMetricas(parcial: Partial<Metricas> = {}): Metricas {
  return {
    total: 20,
    pendentes: 0,
    sucesso: 15,
    erro: 5,
    emRetentativa: 0,
    naFila: 0,
    outboxPendente: 0,
    deadLetters: 1,
    porStatus: { Processado: 15, Invalido: 3, Falha: 2 },
    ...parcial,
  }
}

export function umaPagina<T>(itens: T[], parcial: Partial<PagedResult<T>> = {}): PagedResult<T> {
  return {
    itens,
    pagina: 1,
    tamanhoPagina: 25,
    totalItens: itens.length,
    totalPaginas: 1,
    temProximaPagina: false,
    ...parcial,
  }
}

export function umaSessao(parcial: Partial<Sessao> = {}): Sessao {
  return {
    token: 'token-de-teste',
    // Uma hora a frente: perto o bastante de um token real, longe o bastante de
    // expirar no meio da suite.
    expiraEmUtc: new Date(Date.now() + 3_600_000).toISOString(),
    usuario: 'admin',
    nome: 'Administracao',
    papel: 'administrador',
    ...parcial,
  }
}

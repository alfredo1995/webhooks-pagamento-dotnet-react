import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, registrarPerdaDeSessao } from './api/client'
import { armazenamentoSessao } from './api/sessao'
import type { EventoResumo, FiltrosEventos, Sessao } from './api/types'
import { BarraFiltros } from './components/BarraFiltros'
import { MetricasCards } from './components/MetricasCards'
import { ModalEvento } from './components/ModalEvento'
import { TabelaAuditoria } from './components/TabelaAuditoria'
import { TabelaContratos } from './components/TabelaContratos'
import { TabelaDeadLetters } from './components/TabelaDeadLetters'
import { TabelaEventos } from './components/TabelaEventos'
import { TelaLogin } from './components/TelaLogin'
import { formatarHora } from './components/formatadores'
import { useAutoRefresh } from './hooks/useAutoRefresh'

const INTERVALO_MS = 3000
const TAMANHO_PAGINA = 25

type Aba = 'eventos' | 'contratos' | 'dlq' | 'auditoria'

export default function App() {
  const [sessao, setSessao] = useState<Sessao | null>(() => armazenamentoSessao.ler())

  // O painel mantem varias requisicoes em polling; sem um ponto unico de aviso,
  // cada uma mostraria seu proprio "nao autorizado" quando o token vencesse.
  useEffect(() => {
    registrarPerdaDeSessao(() => setSessao(null))
  }, [])

  const sair = useCallback(() => {
    armazenamentoSessao.limpar()
    setSessao(null)
  }, [])

  return sessao ? <Painel sessao={sessao} aoSair={sair} /> : <TelaLogin aoEntrar={setSessao} />
}

function Painel({ sessao, aoSair }: { sessao: Sessao; aoSair: () => void }) {
  const [filtros, setFiltros] = useState<FiltrosEventos>({
    resultado: 'Todos',
    idContrato: '',
    idTransacao: '',
    pagina: 1,
  })
  const [autoRefresh, setAutoRefresh] = useState(true)
  const [aba, setAba] = useState<Aba>('eventos')
  const [selecionado, setSelecionado] = useState<EventoResumo | null>(null)
  const [reprocessando, setReprocessando] = useState<string | null>(null)
  const [avisoAcao, setAvisoAcao] = useState<string | null>(null)

  const ehAdministrador = sessao.papel === 'administrador'

  const buscarEventos = useCallback(
    (signal: AbortSignal) => api.listarEventos(filtros, TAMANHO_PAGINA, signal),
    [filtros],
  )

  const buscarMetricas = useCallback((signal: AbortSignal) => api.obterMetricas(signal), [])

  const buscarContratos = useCallback(
    (signal: AbortSignal) => api.listarContratos(filtros.idContrato, signal),
    [filtros.idContrato],
  )

  const buscarDeadLetters = useCallback(
    (signal: AbortSignal) => api.listarDeadLetters(false, signal),
    [],
  )

  const buscarAuditoria = useCallback(
    (signal: AbortSignal) => api.listarAuditoria('', '', signal),
    [],
  )

  // O quarto argumento diz se a consulta deve existir; o terceiro, se ela se
  // repete. Aba escondida nem chega a pedir — e o que impede o painel do
  // operador de bater na trilha de auditoria e receber 403.
  const eventos = useAutoRefresh(buscarEventos, INTERVALO_MS, autoRefresh, aba === 'eventos')
  const metricas = useAutoRefresh(buscarMetricas, INTERVALO_MS, autoRefresh)
  const contratos = useAutoRefresh(buscarContratos, INTERVALO_MS, autoRefresh, aba === 'contratos')
  const deadLetters = useAutoRefresh(buscarDeadLetters, INTERVALO_MS, autoRefresh, aba === 'dlq')
  const auditoria = useAutoRefresh(
    buscarAuditoria,
    INTERVALO_MS,
    autoRefresh,
    aba === 'auditoria' && ehAdministrador,
  )

  const erroApi = eventos.erro ?? metricas.erro ?? contratos.erro ?? deadLetters.erro ?? auditoria.erro
  const totalErros = metricas.dados?.erro ?? 0
  const totalDlq = metricas.dados?.deadLetters ?? 0

  const pagina = eventos.dados
  const itens = useMemo(() => pagina?.itens ?? [], [pagina])

  const recarregarTudo = useCallback(() => {
    eventos.recarregar()
    metricas.recarregar()
    contratos.recarregar()
    deadLetters.recarregar()
    auditoria.recarregar()
  }, [eventos, metricas, contratos, deadLetters, auditoria])

  const reprocessar = useCallback(
    async (eventoId: string) => {
      setReprocessando(eventoId)
      setAvisoAcao(null)

      try {
        const resposta = await api.reprocessar(eventoId)
        setAvisoAcao(`${resposta.idTransacao}: ${resposta.mensagem}`)
        deadLetters.recarregar()
        metricas.recarregar()
      } catch (e: unknown) {
        setAvisoAcao(e instanceof Error ? e.message : 'Não foi possível reprocessar o evento.')
      } finally {
        setReprocessando(null)
      }
    },
    [deadLetters, metricas],
  )

  return (
    <div className="app">
      <header className="cabecalho">
        <div>
          <h1>Painel de Pagamentos</h1>
          <p>Notificações recebidas do banco parceiro via webhook</p>
        </div>
        <div className="cabecalho__acoes">
          <span className={`pulso ${autoRefresh ? 'pulso--ativo' : ''}`}>
            {autoRefresh ? 'ao vivo' : 'pausado'}
          </span>
          <span className="identidade">
            {sessao.nome}
            <small>{sessao.papel}</small>
          </span>
          <button type="button" className="botao botao--discreto" onClick={aoSair}>
            Sair
          </button>
        </div>
      </header>

      {erroApi && (
        <div className="alerta alerta--api" role="alert">
          <strong>Não foi possível falar com a API.</strong>
          <p>{erroApi}</p>
        </div>
      )}

      <MetricasCards metricas={metricas.dados} />

      {/* Um alerta so, e nao um por contagem: a carta da DLQ ja esta contada nos
          eventos com erro, entao dois banners vermelhos empilhados descreveriam
          o mesmo problema duas vezes e empurrariam a tabela para fora da tela.
          Ele fica acima e independe do filtro ativo — um evento com falha nao
          pode depender de o operador lembrar de filtrar. */}
      {totalErros > 0 && (
        <div className="alerta alerta--erro" role="alert">
          <strong>
            {totalErros} evento{totalErros > 1 ? 's' : ''} com erro
            {totalDlq > 0 && ` · ${totalDlq} na dead-letter queue`}
          </strong>
          <p>
            {totalDlq > 0
              ? 'Parte esgotou as retentativas automáticas e aguarda decisão manual.'
              : 'Falhas de validação ou de processamento que precisam de atenção.'}
          </p>
          <div className="alerta__acoes">
            {filtros.resultado !== 'Erro' && aba === 'eventos' && (
              <button
                type="button"
                className="botao botao--erro"
                onClick={() => setFiltros((f) => ({ ...f, resultado: 'Erro', pagina: 1 }))}
              >
                Ver apenas os erros
              </button>
            )}
            {totalDlq > 0 && aba !== 'dlq' && (
              <button type="button" className="botao botao--erro" onClick={() => setAba('dlq')}>
                Abrir a dead-letter queue
              </button>
            )}
          </div>
        </div>
      )}

      <nav className="abas" aria-label="Seções">
        <button
          type="button"
          className={aba === 'eventos' ? 'aba aba--ativa' : 'aba'}
          onClick={() => setAba('eventos')}
        >
          Eventos recebidos
        </button>
        <button
          type="button"
          className={aba === 'contratos' ? 'aba aba--ativa' : 'aba'}
          onClick={() => setAba('contratos')}
        >
          Status por contrato
        </button>
        <button
          type="button"
          className={aba === 'dlq' ? 'aba aba--ativa' : 'aba'}
          onClick={() => setAba('dlq')}
        >
          Dead-letter queue{totalDlq > 0 ? ` (${totalDlq})` : ''}
        </button>
        {ehAdministrador && (
          <button
            type="button"
            className={aba === 'auditoria' ? 'aba aba--ativa' : 'aba'}
            onClick={() => setAba('auditoria')}
          >
            Auditoria
          </button>
        )}
      </nav>

      <BarraFiltros
        filtros={filtros}
        aoAlterar={setFiltros}
        autoRefresh={autoRefresh}
        aoAlternarAutoRefresh={setAutoRefresh}
        aoRecarregar={recarregarTudo}
        atualizadoEm={formatarHora(eventos.atualizadoEm)}
        somenteAtualizacao={aba !== 'eventos'}
      />

      {avisoAcao && (
        <div className="alerta" role="status">
          <p>{avisoAcao}</p>
        </div>
      )}

      {aba === 'eventos' && (
        <>
          <TabelaEventos eventos={itens} carregando={eventos.carregando} aoSelecionar={setSelecionado} />

          {pagina && pagina.totalPaginas > 1 && (
            <nav className="paginacao" aria-label="Paginação">
              <button
                type="button"
                className="botao"
                disabled={pagina.pagina <= 1}
                onClick={() => setFiltros((f) => ({ ...f, pagina: f.pagina - 1 }))}
              >
                Anterior
              </button>
              <span>
                Página {pagina.pagina} de {pagina.totalPaginas} · {pagina.totalItens} evento(s)
              </span>
              <button
                type="button"
                className="botao"
                disabled={!pagina.temProximaPagina}
                onClick={() => setFiltros((f) => ({ ...f, pagina: f.pagina + 1 }))}
              >
                Próxima
              </button>
            </nav>
          )}
        </>
      )}

      {aba === 'contratos' && (
        <TabelaContratos contratos={contratos.dados?.itens ?? []} carregando={contratos.carregando} />
      )}

      {aba === 'dlq' && (
        <TabelaDeadLetters
          cartas={deadLetters.dados?.itens ?? []}
          carregando={deadLetters.carregando}
          podeReprocessar={ehAdministrador}
          reprocessando={reprocessando}
          aoReprocessar={reprocessar}
        />
      )}

      {aba === 'auditoria' && (
        <TabelaAuditoria registros={auditoria.dados?.itens ?? []} carregando={auditoria.carregando} />
      )}

      {selecionado && <ModalEvento evento={selecionado} aoFechar={() => setSelecionado(null)} />}
    </div>
  )
}

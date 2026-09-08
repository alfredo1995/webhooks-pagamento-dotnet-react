import { useCallback, useMemo, useState } from 'react'
import { api } from './api/client'
import type { EventoResumo, FiltrosEventos } from './api/types'
import { BarraFiltros } from './components/BarraFiltros'
import { MetricasCards } from './components/MetricasCards'
import { ModalEvento } from './components/ModalEvento'
import { TabelaContratos } from './components/TabelaContratos'
import { TabelaEventos } from './components/TabelaEventos'
import { formatarHora } from './components/formatadores'
import { useAutoRefresh } from './hooks/useAutoRefresh'

const INTERVALO_MS = 3000
const TAMANHO_PAGINA = 25

type Aba = 'eventos' | 'contratos'

export default function App() {
  const [filtros, setFiltros] = useState<FiltrosEventos>({
    resultado: 'Todos',
    idContrato: '',
    idTransacao: '',
    pagina: 1,
  })
  const [autoRefresh, setAutoRefresh] = useState(true)
  const [aba, setAba] = useState<Aba>('eventos')
  const [selecionado, setSelecionado] = useState<EventoResumo | null>(null)

  const buscarEventos = useCallback(
    (signal: AbortSignal) => api.listarEventos(filtros, TAMANHO_PAGINA, signal),
    [filtros],
  )

  const buscarMetricas = useCallback((signal: AbortSignal) => api.obterMetricas(signal), [])

  const buscarContratos = useCallback(
    (signal: AbortSignal) => api.listarContratos(filtros.idContrato, signal),
    [filtros.idContrato],
  )

  const eventos = useAutoRefresh(buscarEventos, INTERVALO_MS, autoRefresh && aba === 'eventos')
  const metricas = useAutoRefresh(buscarMetricas, INTERVALO_MS, autoRefresh)
  const contratos = useAutoRefresh(buscarContratos, INTERVALO_MS, autoRefresh && aba === 'contratos')

  const erroApi = eventos.erro ?? metricas.erro ?? contratos.erro
  const totalErros = metricas.dados?.erro ?? 0

  const pagina = eventos.dados
  const itens = useMemo(() => pagina?.itens ?? [], [pagina])

  const recarregarTudo = useCallback(() => {
    eventos.recarregar()
    metricas.recarregar()
    contratos.recarregar()
  }, [eventos, metricas, contratos])

  return (
    <div className="app">
      <header className="cabecalho">
        <div>
          <h1>Painel de Pagamentos</h1>
          <p>Notificações recebidas do banco parceiro via webhook</p>
        </div>
        <span className={`pulso ${autoRefresh ? 'pulso--ativo' : ''}`}>
          {autoRefresh ? 'ao vivo' : 'pausado'}
        </span>
      </header>

      {erroApi && (
        <div className="alerta alerta--api" role="alert">
          <strong>Não foi possível falar com a API.</strong>
          <p>{erroApi}</p>
        </div>
      )}

      <MetricasCards metricas={metricas.dados} />

      {/* O alerta de erro fica acima da tabela e independe do filtro ativo:
          um evento com falha nao pode depender de o operador lembrar de filtrar. */}
      {totalErros > 0 && (
        <div className="alerta alerta--erro" role="alert">
          <strong>
            {totalErros} evento{totalErros > 1 ? 's' : ''} com erro
          </strong>
          <p>Falhas de validação ou de processamento que precisam de atenção.</p>
          {filtros.resultado !== 'Erro' && (
            <button
              type="button"
              className="botao botao--erro"
              onClick={() => setFiltros((f) => ({ ...f, resultado: 'Erro', pagina: 1 }))}
            >
              Ver apenas os erros
            </button>
          )}
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
      </nav>

      <BarraFiltros
        filtros={filtros}
        aoAlterar={setFiltros}
        autoRefresh={autoRefresh}
        aoAlternarAutoRefresh={setAutoRefresh}
        aoRecarregar={recarregarTudo}
        atualizadoEm={formatarHora(eventos.atualizadoEm)}
      />

      {aba === 'eventos' ? (
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
      ) : (
        <TabelaContratos contratos={contratos.dados?.itens ?? []} carregando={contratos.carregando} />
      )}

      {selecionado && <ModalEvento evento={selecionado} aoFechar={() => setSelecionado(null)} />}
    </div>
  )
}

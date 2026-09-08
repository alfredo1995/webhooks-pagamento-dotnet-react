import type { FiltrosEventos, Resultado } from '../api/types'

interface Props {
  filtros: FiltrosEventos
  aoAlterar: (filtros: FiltrosEventos) => void
  autoRefresh: boolean
  aoAlternarAutoRefresh: (ativo: boolean) => void
  aoRecarregar: () => void
  atualizadoEm: string
}

const opcoes: Array<{ valor: Resultado | 'Todos'; rotulo: string }> = [
  { valor: 'Todos', rotulo: 'Todos' },
  { valor: 'Sucesso', rotulo: 'Sucesso' },
  { valor: 'Erro', rotulo: 'Erro' },
  { valor: 'Pendente', rotulo: 'Pendentes' },
]

export function BarraFiltros({
  filtros,
  aoAlterar,
  autoRefresh,
  aoAlternarAutoRefresh,
  aoRecarregar,
  atualizadoEm,
}: Props) {
  // Qualquer mudanca de filtro volta para a primeira pagina: manter a pagina 7
  // ao trocar de filtro quase sempre resulta em uma lista vazia sem explicacao.
  const alterar = (parcial: Partial<FiltrosEventos>) =>
    aoAlterar({ ...filtros, ...parcial, pagina: 1 })

  return (
    <section className="filtros" aria-label="Filtros">
      <div className="filtros__grupo" role="group" aria-label="Filtrar por resultado">
        {opcoes.map((opcao) => (
          <button
            key={opcao.valor}
            type="button"
            className={`chip ${filtros.resultado === opcao.valor ? 'chip--ativo' : ''}`}
            aria-pressed={filtros.resultado === opcao.valor}
            onClick={() => alterar({ resultado: opcao.valor })}
          >
            {opcao.rotulo}
          </button>
        ))}
      </div>

      <label className="campo">
        <span>ID do contrato</span>
        <input
          type="search"
          value={filtros.idContrato}
          placeholder="ex.: CT-2026-0042"
          onChange={(e) => alterar({ idContrato: e.target.value })}
        />
      </label>

      <label className="campo">
        <span>ID da transação</span>
        <input
          type="search"
          value={filtros.idTransacao}
          placeholder="ex.: TX-9931"
          onChange={(e) => alterar({ idTransacao: e.target.value })}
        />
      </label>

      <div className="filtros__acoes">
        <label className="toggle">
          <input
            type="checkbox"
            checked={autoRefresh}
            onChange={(e) => aoAlternarAutoRefresh(e.target.checked)}
          />
          <span>
            Atualizar automaticamente
            <small>{autoRefresh ? `atualizado às ${atualizadoEm}` : 'pausado'}</small>
          </span>
        </label>

        <button type="button" className="botao" onClick={aoRecarregar}>
          Atualizar agora
        </button>
      </div>
    </section>
  )
}

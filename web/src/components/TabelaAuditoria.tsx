import type { AuditoriaResumo } from '../api/types'
import { formatarDataHora } from './formatadores'

interface Props {
  registros: AuditoriaResumo[]
  carregando: boolean
}

export function TabelaAuditoria({ registros, carregando }: Props) {
  if (carregando && registros.length === 0) {
    return <p className="vazio">Carregando a trilha de auditoria…</p>
  }

  if (registros.length === 0) {
    return <p className="vazio">Nenhum acesso registrado para os filtros aplicados.</p>
  }

  return (
    <div className="tabela-wrapper">
      <table className="tabela">
        <thead>
          <tr>
            <th scope="col">Quando</th>
            <th scope="col">Usuário</th>
            <th scope="col">Papel</th>
            <th scope="col">Rota</th>
            <th scope="col">Filtros usados</th>
            <th scope="col">Origem</th>
            <th scope="col" className="num">HTTP</th>
          </tr>
        </thead>
        <tbody>
          {registros.map((registro) => (
            <tr key={registro.id}>
              <td>{formatarDataHora(registro.emUtc)}</td>
              <td>{registro.usuario}</td>
              <td>{registro.papel}</td>
              <td className="mono">
                {registro.metodo} {registro.recurso}
              </td>
              {/* Os filtros sao o que distingue "abriu a lista" de "procurou o
                  contrato de um cliente". Sem eles a trilha nao responde nada. */}
              <td className="mono">{registro.consulta ?? '—'}</td>
              <td className="mono">{registro.ipOrigem}</td>
              <td className="num">{registro.statusHttp}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

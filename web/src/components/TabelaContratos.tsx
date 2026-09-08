import type { ContratoResumo } from '../api/types'
import { formatarDataHora, formatarMoeda } from './formatadores'

interface Props {
  contratos: ContratoResumo[]
  carregando: boolean
}

export function TabelaContratos({ contratos, carregando }: Props) {
  if (carregando && contratos.length === 0) {
    return <p className="vazio">Carregando contratos…</p>
  }

  if (contratos.length === 0) {
    return <p className="vazio">Nenhum contrato consolidado ainda.</p>
  }

  return (
    <div className="tabela-wrapper">
      <table className="tabela">
        <thead>
          <tr>
            <th scope="col">Contrato</th>
            <th scope="col" className="num">Pago</th>
            <th scope="col" className="num">Estornado</th>
            <th scope="col" className="num">Saldo líquido</th>
            <th scope="col" className="num">Pagamentos</th>
            <th scope="col">Último status</th>
            <th scope="col">Atualizado em</th>
          </tr>
        </thead>
        <tbody>
          {contratos.map((contrato) => (
            <tr key={contrato.idContrato}>
              <td className="mono">{contrato.idContrato}</td>
              <td className="num">{formatarMoeda(contrato.valorTotalPago)}</td>
              <td className="num">{formatarMoeda(contrato.valorTotalEstornado)}</td>
              <td className="num forte">{formatarMoeda(contrato.saldoLiquido)}</td>
              <td className="num">{contrato.quantidadePagamentos}</td>
              <td>{contrato.ultimoStatus}</td>
              <td>{formatarDataHora(contrato.atualizadoEmUtc)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

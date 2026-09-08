import type { Resultado, StatusProcessamento } from '../api/types'

const rotulos: Record<StatusProcessamento, string> = {
  Recebido: 'Na fila',
  Processando: 'Processando',
  Processado: 'Processado',
  Invalido: 'Inválido',
  Falha: 'Falha',
}

/**
 * O badge carrega o status tecnico, mas a cor vem do resultado. Assim
 * "Inválido" e "Falha" sao visualmente o mesmo alarme, que e o que o operador
 * precisa enxergar de longe, sem perder a distincao no texto.
 */
export function StatusBadge({
  status,
  resultado,
}: {
  status: StatusProcessamento
  resultado: Resultado
}) {
  return (
    <span className={`badge badge--${resultado.toLowerCase()}`}>
      {resultado === 'Erro' && <span aria-hidden="true">⚠ </span>}
      {rotulos[status] ?? status}
    </span>
  )
}

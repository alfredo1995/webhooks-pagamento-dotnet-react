const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const dataHora = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' })
const data = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short' })

export function formatarMoeda(valor: number | null | undefined): string {
  return valor === null || valor === undefined ? '—' : moeda.format(valor)
}

/** As datas chegam em UTC; o painel mostra no fuso de quem esta olhando. */
export function formatarDataHora(iso: string | null | undefined): string {
  if (!iso) return '—'

  const d = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)

  return Number.isNaN(d.getTime()) ? '—' : dataHora.format(d)
}

export function formatarData(iso: string | null | undefined): string {
  if (!iso) return '—'

  const d = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)

  return Number.isNaN(d.getTime()) ? '—' : data.format(d)
}

export function formatarDuracao(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return '—'

  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(2)} s`
}

export function formatarHora(d: Date | null): string {
  return d ? d.toLocaleTimeString('pt-BR') : '—'
}

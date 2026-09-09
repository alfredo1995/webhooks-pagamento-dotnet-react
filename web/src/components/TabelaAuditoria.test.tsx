import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TabelaAuditoria } from './TabelaAuditoria'
import { umRegistroDeAuditoria } from '../testes/fabricas'

describe('TabelaAuditoria', () => {
  it('mostra usuario, papel, rota, origem e status', () => {
    render(
      <TabelaAuditoria registros={[umRegistroDeAuditoria()]} carregando={false} />,
    )

    expect(screen.getByText('admin')).toBeInTheDocument()
    expect(screen.getByText('administrador')).toBeInTheDocument()
    expect(screen.getByText('GET /api/eventos')).toBeInTheDocument()
    expect(screen.getByText('172.20.0.6')).toBeInTheDocument()
    expect(screen.getByText('200')).toBeInTheDocument()
  })

  /**
   * Os filtros sao o que distingue "abriu a lista" de "procurou o contrato de um
   * cliente". Sem eles a trilha nao responde a pergunta que motiva auditoria.
   */
  it('mostra os filtros usados na consulta', () => {
    render(
      <TabelaAuditoria
        registros={[umRegistroDeAuditoria({ consulta: '?idContrato=CT-2026-0002' })]}
        carregando={false}
      />,
    )

    expect(screen.getByText('?idContrato=CT-2026-0002')).toBeInTheDocument()
  })

  it('mostra travessao quando a consulta veio sem filtro', () => {
    render(
      <TabelaAuditoria registros={[umRegistroDeAuditoria({ consulta: null })]} carregando={false} />,
    )

    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('registra tambem o acesso negado, que e o mais interessante da trilha', () => {
    render(
      <TabelaAuditoria
        registros={[umRegistroDeAuditoria({ usuario: 'operador', papel: 'operador', statusHttp: 403 })]}
        carregando={false}
      />,
    )

    expect(screen.getByText('403')).toBeInTheDocument()
  })

  it('distingue "carregando" de trilha vazia', () => {
    const { unmount } = render(<TabelaAuditoria registros={[]} carregando />)

    expect(screen.getByText('Carregando a trilha de auditoria…')).toBeInTheDocument()

    unmount()

    render(<TabelaAuditoria registros={[]} carregando={false} />)

    expect(screen.getByText('Nenhum acesso registrado para os filtros aplicados.')).toBeInTheDocument()
  })
})

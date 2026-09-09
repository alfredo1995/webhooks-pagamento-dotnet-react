import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TelaLogin } from './TelaLogin'
import { api } from '../api/client'
import { umaSessao } from '../testes/fabricas'

vi.mock('../api/client', () => ({ api: { entrar: vi.fn() } }))

describe('TelaLogin', () => {
  beforeEach(() => {
    vi.mocked(api.entrar).mockReset()
  })

  it('entra com as credenciais digitadas', async () => {
    const sessao = umaSessao()
    vi.mocked(api.entrar).mockResolvedValue(sessao)
    const aoEntrar = vi.fn()

    render(<TelaLogin aoEntrar={aoEntrar} />)

    await userEvent.type(screen.getByLabelText('Usuário'), 'admin')
    await userEvent.type(screen.getByLabelText('Senha'), 'segredo')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(api.entrar).toHaveBeenCalledWith('admin', 'segredo')
    await waitFor(() => expect(aoEntrar).toHaveBeenCalledWith(sessao))
  })

  it('esconde a senha digitada', () => {
    render(<TelaLogin aoEntrar={vi.fn()} />)

    expect(screen.getByLabelText('Senha')).toHaveAttribute('type', 'password')
  })

  it('mostra o erro da API e mantem a pessoa na tela', async () => {
    vi.mocked(api.entrar).mockRejectedValue(new Error('Usuário ou senha inválidos.'))
    const aoEntrar = vi.fn()

    render(<TelaLogin aoEntrar={aoEntrar} />)

    await userEvent.type(screen.getByLabelText('Usuário'), 'admin')
    await userEvent.type(screen.getByLabelText('Senha'), 'errada')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Usuário ou senha inválidos.')
    expect(aoEntrar).not.toHaveBeenCalled()
  })

  it('descreve a falha quando o que veio nao é um Error', async () => {
    vi.mocked(api.entrar).mockRejectedValue('caiu a rede')

    render(<TelaLogin aoEntrar={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('Usuário'), 'admin')
    await userEvent.type(screen.getByLabelText('Senha'), 'x')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível entrar.')
  })

  /** Sem isso, dois cliques rapidos viram duas tentativas de login. */
  it('trava o botao durante o envio', async () => {
    let liberar: (sessao: ReturnType<typeof umaSessao>) => void = () => {}
    vi.mocked(api.entrar).mockReturnValue(
      new Promise((resolve) => {
        liberar = resolve
      }),
    )

    render(<TelaLogin aoEntrar={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('Usuário'), 'admin')
    await userEvent.type(screen.getByLabelText('Senha'), 'x')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('button', { name: 'Entrando…' })).toBeDisabled()

    liberar(umaSessao())

    await waitFor(() => expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled())
  })

  it('limpa o erro anterior ao tentar de novo', async () => {
    vi.mocked(api.entrar)
      .mockRejectedValueOnce(new Error('Usuário ou senha inválidos.'))
      .mockResolvedValueOnce(umaSessao())

    render(<TelaLogin aoEntrar={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('Usuário'), 'admin')
    await userEvent.type(screen.getByLabelText('Senha'), 'errada')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  /** O acesso é auditado; avisar disso na tela é parte do combinado. */
  it('avisa que o acesso e registrado na trilha de auditoria', () => {
    render(<TelaLogin aoEntrar={vi.fn()} />)

    expect(screen.getByText(/registrado na trilha de auditoria/)).toBeInTheDocument()
  })
})

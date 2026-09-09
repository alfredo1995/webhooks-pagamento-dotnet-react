import { useState } from 'react'
import { api } from '../api/client'
import type { Sessao } from '../api/types'

interface Props {
  aoEntrar: (sessao: Sessao) => void
}

export function TelaLogin({ aoEntrar }: Props) {
  const [login, setLogin] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  const submeter = async (evento: React.FormEvent) => {
    evento.preventDefault()
    setErro(null)
    setEnviando(true)

    try {
      aoEntrar(await api.entrar(login, senha))
    } catch (e: unknown) {
      setErro(e instanceof Error ? e.message : 'Não foi possível entrar.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="login">
      <form className="login__cartao" onSubmit={submeter}>
        <h1>Painel de Pagamentos</h1>
        <p className="login__subtitulo">
          Consulta de webhooks do banco parceiro. O acesso é registrado na trilha de auditoria.
        </p>

        {erro && (
          <div className="alerta alerta--erro" role="alert">
            <strong>Não foi possível entrar</strong>
            <p>{erro}</p>
          </div>
        )}

        <label className="campo">
          <span>Usuário</span>
          <input
            type="text"
            value={login}
            autoComplete="username"
            autoFocus
            required
            onChange={(e) => setLogin(e.target.value)}
          />
        </label>

        <label className="campo">
          <span>Senha</span>
          <input
            type="password"
            value={senha}
            autoComplete="current-password"
            required
            onChange={(e) => setSenha(e.target.value)}
          />
        </label>

        <button type="submit" className="botao botao--primario" disabled={enviando}>
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
      </form>
    </div>
  )
}

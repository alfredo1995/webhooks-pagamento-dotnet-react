import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Sem `globals: true` o auto-cleanup da Testing Library nao se registra sozinho.
// Sem ele, o DOM de um teste sobra para o proximo e as buscas por texto passam a
// achar dois elementos — falha que aparece longe do teste que a causou.
afterEach(() => {
  cleanup()
  sessionStorage.clear()
})

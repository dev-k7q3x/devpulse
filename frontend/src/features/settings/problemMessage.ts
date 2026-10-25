import { ProblemError } from '@/api/httpClient'

/** Komunikat dla użytkownika z ProblemDetails – rozpoznawany po `type` (plan 03, tabela 0.1). */
export function problemMessage(error: unknown, fallback: string): string {
  if (!(error instanceof ProblemError)) {
    return fallback
  }

  switch (error.problem.type) {
    case 'rate-limited':
      return 'Zbyt wiele prób. Spróbuj ponownie później.'
    case 'conflict':
      return error.problem.detail ?? fallback
    case 'validation':
      return firstValidationError(error.problem.errors) ?? fallback
    default:
      return fallback
  }
}

function firstValidationError(errors: unknown): string | undefined {
  if (typeof errors !== 'object' || errors === null) {
    return undefined
  }
  for (const messages of Object.values(errors)) {
    if (Array.isArray(messages) && typeof messages[0] === 'string') {
      return messages[0]
    }
  }
  return undefined
}

import { ProblemError } from '@/api/httpClient'
import { formatRetryAfter } from '@/lib/dateTime'

/** Komunikat dla błędów `/api/repositories*` – rozpoznawany po `problem.type` (plan 03, tabela 0.1). */
export function repositoryProblemMessage(error: unknown): string {
  if (!(error instanceof ProblemError)) {
    return 'Coś poszło nie tak. Spróbuj ponownie.'
  }

  switch (error.problem.type) {
    case 'conflict':
      return 'To repozytorium jest już śledzone.'
    case 'limit-exceeded':
      return (
        error.problem.detail ??
        'Osiągnięto limit śledzonych repozytoriów. Usuń któreś, aby dodać nowe.'
      )
    case 'not-found':
      return 'Repozytorium nie istnieje albo nie masz do niego dostępu.'
    case 'github-token-invalid':
      return 'GitHub odrzucił dostęp do Twojego konta – zaloguj się ponownie.'
    case 'github-rate-limited':
      return `Wyczerpany limit zapytań do GitHuba. Spróbuj ponownie ${formatRetryAfter(error.retryAfterSeconds)}.`
    case 'rate-limited':
      return `Zbyt wiele żądań. Spróbuj ponownie ${formatRetryAfter(error.retryAfterSeconds)}.`
    case 'github-unavailable':
      return 'GitHub jest chwilowo niedostępny. Spróbuj ponownie za chwilę.'
    default:
      return 'Coś poszło nie tak. Spróbuj ponownie.'
  }
}

export function isGitHubTokenInvalid(error: unknown): boolean {
  return (
    error instanceof ProblemError &&
    error.problem.type === 'github-token-invalid'
  )
}

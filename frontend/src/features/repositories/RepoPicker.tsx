import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { useLocation } from 'react-router'
import { queryKeys } from '@/api/queryKeys'
import { loginUrl } from '@/features/auth/loginUrl'
import {
  isGitHubTokenInvalid,
  repositoryProblemMessage,
} from './repositoryProblemMessage'
import {
  PAGE_SIZE,
  useAddRepository,
  useAvailableRepositories,
  useRefreshAvailableRepositories,
  useRemoveRepository,
  useTrackedRepositories,
  type AvailableRepository,
} from './useRepositories'

/**
 * Wybór śledzonych repozytoriów: wyszukiwanie i paginacja (po stronie serwera, w pamięci), dodawanie i usuwanie.
 * Osadzony w pustym dashboardzie i w ustawieniach.
 */
export function RepoPicker() {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)

  const available = useAvailableRepositories(search, page)
  const tracked = useTrackedRepositories()
  const add = useAddRepository()
  const remove = useRemoveRepository()
  const refresh = useRefreshAvailableRepositories()

  const error = add.error ?? remove.error ?? refresh.error ?? available.error
  useRefreshSessionOnTokenInvalid(error)

  const trackedIdByGitHubId = new Map(
    (tracked.data ?? []).map((repo) => [Number(repo.githubRepoId), repo.id]),
  )
  const totalCount = Number(available.data?.totalCount ?? 0)
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const busyRepoId = add.isPending
    ? add.variables
    : remove.isPending
      ? remove.variables
      : undefined

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSearch(searchInput.trim())
    setPage(1)
  }

  function toggle(repo: AvailableRepository) {
    add.reset()
    remove.reset()
    const githubRepoId = Number(repo.githubRepoId)
    const trackedId = trackedIdByGitHubId.get(githubRepoId)
    if (repo.isTracked && trackedId) {
      remove.mutate(trackedId)
    } else if (!repo.isTracked) {
      add.mutate(githubRepoId)
    }
  }

  return (
    <section className="repo-picker" aria-label="Wybór repozytoriów">
      <form role="search" onSubmit={handleSearch}>
        <label htmlFor="repo-search">Szukaj repozytorium</label>
        <input
          id="repo-search"
          type="search"
          value={searchInput}
          maxLength={100}
          onChange={(event) => setSearchInput(event.target.value)}
          placeholder="np. octocat/hello-world"
        />
        <button type="submit">Szukaj</button>
        <button
          type="button"
          onClick={() => refresh.mutate()}
          disabled={refresh.isPending}
        >
          Odśwież listę z GitHuba
        </button>
      </form>

      {error && <PickerError error={error} />}

      {available.isPending && <p role="status">Ładowanie repozytoriów…</p>}

      {available.data && totalCount === 0 && (
        <p>
          {search
            ? `Brak repozytoriów pasujących do „${search}”.`
            : 'Nie znaleziono żadnych repozytoriów na Twoim koncie GitHub.'}
        </p>
      )}

      {available.data && totalCount > 0 && (
        <>
          <ul
            className="repo-picker__list"
            aria-label="Repozytoria na GitHubie"
          >
            {available.data.items.map((repo) => {
              const githubRepoId = Number(repo.githubRepoId)
              const trackedId = trackedIdByGitHubId.get(githubRepoId)
              const busy =
                busyRepoId === githubRepoId ||
                (trackedId !== undefined && busyRepoId === trackedId)
              return (
                <li key={githubRepoId}>
                  <span>
                    <strong>{repo.fullName}</strong>
                    {repo.isPrivate && <span className="tag">prywatne</span>}
                    {repo.description && (
                      <span className="muted"> – {repo.description}</span>
                    )}
                  </span>
                  <button
                    type="button"
                    onClick={() => toggle(repo)}
                    disabled={busy || (repo.isTracked && !trackedId)}
                    aria-label={`${repo.isTracked ? 'Przestań śledzić' : 'Śledź'} ${repo.fullName}`}
                  >
                    {repo.isTracked ? 'Przestań śledzić' : 'Śledź'}
                  </button>
                </li>
              )
            })}
          </ul>
          <nav className="pagination" aria-label="Strony repozytoriów">
            <button
              type="button"
              onClick={() => setPage((current) => current - 1)}
              disabled={page <= 1}
            >
              Poprzednia
            </button>
            <span>
              Strona {page} z {pageCount}
            </span>
            <button
              type="button"
              onClick={() => setPage((current) => current + 1)}
              disabled={page >= pageCount}
            >
              Następna
            </button>
          </nav>
        </>
      )}
    </section>
  )
}

interface PickerErrorProps {
  error: unknown
}

function PickerError({ error }: PickerErrorProps) {
  const location = useLocation()

  return (
    <p role="alert">
      {repositoryProblemMessage(error)}
      {isGitHubTokenInvalid(error) && (
        <>
          {' '}
          <a href={loginUrl(location.pathname + location.search)}>
            Zaloguj się ponownie
          </a>
        </>
      )}
    </p>
  )
}

/** Serwer oznaczył token jako nieważny – odświeżenie /me pokazuje baner „Zaloguj się ponownie” w całej aplikacji. */
function useRefreshSessionOnTokenInvalid(error: unknown) {
  const queryClient = useQueryClient()
  const tokenInvalid = isGitHubTokenInvalid(error)

  useEffect(() => {
    if (tokenInvalid) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.auth.me })
    }
  }, [tokenInvalid, queryClient])
}

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { httpClient } from '@/api/httpClient'
import { queryKeys } from '@/api/queryKeys'
import type { components } from '@/api/schema'

export type Settings = components['schemas']['SettingsResponse']
export type SettingsUpdate = components['schemas']['SettingsUpdateRequest']
type EmailChange = components['schemas']['EmailChangeRequest']
type EmailConfirm = components['schemas']['EmailConfirmRequest']
type EmailConfirmResult = components['schemas']['EmailConfirmResponse']

export function useSettings() {
  return useQuery({
    queryKey: queryKeys.settings.current,
    queryFn: () => httpClient.get<Settings>('/api/settings'),
  })
}

/** Każda mutacja zwraca pełny stan ustawień – od razu zastępuje cache (nowe `nextDigestAt`). */
function useSettingsMutation<TVariables>(
  mutationFn: (variables: TVariables) => Promise<Settings>,
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: (settings) =>
      queryClient.setQueryData(queryKeys.settings.current, settings),
  })
}

export function useUpdateSettings() {
  return useSettingsMutation((update: SettingsUpdate) =>
    httpClient.put<Settings>('/api/settings', update),
  )
}

export function useStartEmailChange() {
  return useSettingsMutation((request: EmailChange) =>
    httpClient.post<Settings>('/api/settings/email', request),
  )
}

export function useCancelEmailChange() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => httpClient.delete('/api/settings/email/pending'),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.settings.current }),
  })
}

export function useConfirmEmail() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: EmailConfirm) =>
      httpClient.post<EmailConfirmResult>(
        '/api/settings/email/confirm',
        request,
      ),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.settings.current }),
  })
}

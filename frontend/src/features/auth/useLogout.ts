import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { httpClient } from '@/api/httpClient'

/** Wylogowanie: usuwa cookie sesji i cały cache zapytań (dane poprzedniego użytkownika). */
export function useLogout() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  return useMutation({
    mutationFn: () => httpClient.post<void>('/api/auth/logout'),
    // Także przy błędzie (np. sesja już wygasła) – użytkownik ma trafić na ekran logowania.
    onSettled: async () => {
      await navigate('/login', { replace: true })
      queryClient.clear()
    },
  })
}

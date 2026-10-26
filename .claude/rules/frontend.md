---
paths:
  - "frontend/**"
---

# DevPulse – konwencje React / TypeScript

Stack: React 19 + TypeScript (strict) + Vite, React Router, TanStack Query, Vitest + React Testing Library, Prettier.

## Struktura
```
frontend/src/
  app/          ← router, QueryClientProvider, layout, ProtectedRoute
  api/          ← httpClient, queryKeys.ts, schema.d.ts (GENEROWANY – nie edytuj ręcznie)
  features/     ← auth, dashboard, repositories, settings, digests
                  (każdy: strony, komponenty, hooki, własne routes.tsx)
  components/   ← współdzielone UI: Button, Card, Spinner, EmptyState, ErrorBoundary
  hooks/, lib/  ← formatowanie dat, strefy czasowe
```

## Komponenty
- Wyłącznie funkcyjne komponenty z jawnym `interface XxxProps`
- PascalCase dla komponentów, hooki `useXxx`
- Testy `*.test.tsx` obok komponentu; testuj zachowanie widoczne dla użytkownika (role, teksty), nie implementację

## Dane z API
- Stan serwera tylko w TanStack Query – bez globalnego store'a
- Klucze zapytań ZAWSZE z `@/api/queryKeys.ts` (invalidateQueries wymaga identycznych kluczy)
- Dashboard: `staleTime` i `refetchInterval` 15 min – bez real-time
- Typy request/response tylko z `@/api/schema.d.ts` (openapi-typescript); nie definiuj ręcznie kształtów DTO
- Wszystkie requesty przez `@/api/httpClient` (`credentials: 'include'`, nagłówek `X-XSRF-TOKEN`)
- Błędy rozpoznawaj po `problem.type`: `unauthenticated` → /login, `github-token-invalid` → baner
  „Zaloguj się ponownie”, `rate-limited` / `github-rate-limited` → komunikat z Retry-After

## TypeScript
- Nigdy `any` (użyj `unknown` + zawężenie)
- Importy przez alias `@/`
- Daty z API to ISO 8601 UTC – formatowanie i strefy tylko przez helpery z `lib/`

## Bezpieczeństwo
- CSP produkcji (`SecurityHeadersMiddleware`): `script-src 'self'; style-src 'self'` – żadnych skryptów ani tagów `<style>` inline;
  biblioteka wstrzykująca style (CSS-in-JS) zostanie zablokowana – przed dodaniem takiej zależności zaktualizuj CSP
- HTML digestu wyłącznie w `<iframe sandbox src="/api/digests/{id}/html">` – nigdy `dangerouslySetInnerHTML`
- Strona /confirm-email wysyła POST z tokenem – sam GET linku nie może niczego potwierdzać
- Linki zewnętrzne (PR-y, repo, checki): `target="_blank" rel="noopener noreferrer"`

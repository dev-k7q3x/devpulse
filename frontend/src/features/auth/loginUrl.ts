// Odrzucane jak w walidatorze na backendzie (char.IsControl) – inaczej przycisk logowania kończyłby się 400.
// eslint-disable-next-line no-control-regex
const CONTROL_CHARACTERS = /[\u0000-\u001f\u007f-\u009f]/

/** Tylko lokalna ścieżka aplikacji – ta sama reguła co walidator `returnUrl` na backendzie. */
export function toLocalPath(value: string | null | undefined): string {
  if (
    !value ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value.startsWith('/\\') ||
    CONTROL_CHARACTERS.test(value)
  ) {
    return '/'
  }
  return value
}

/** Start GitHub OAuth – pełna nawigacja przeglądarki, nie fetch (302 na github.com). */
export function loginUrl(returnUrl?: string | null): string {
  const path = toLocalPath(returnUrl)
  return path === '/'
    ? '/api/auth/login'
    : `/api/auth/login?returnUrl=${encodeURIComponent(path)}`
}

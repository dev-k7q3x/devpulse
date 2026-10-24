import { describe, expect, it } from 'vitest'
import { loginUrl, toLocalPath } from './loginUrl'

describe('toLocalPath', () => {
  it.each([
    ['/settings?tab=email', '/settings?tab=email'],
    ['/', '/'],
  ])('keeps local path %s', (value, expected) => {
    expect(toLocalPath(value)).toBe(expected)
  })

  it.each([
    [null],
    [''],
    ['https://evil.example'],
    ['//evil.example'],
    ['/\\evil.example'],
    ['settings'],
    ['/settings\nSet-Cookie: x'],
    ['/settings\u0000'],
  ])('falls back to root for %s', (value) => {
    expect(toLocalPath(value)).toBe('/')
  })
})

describe('loginUrl', () => {
  it('omits return url for root', () => {
    expect(loginUrl('/')).toBe('/api/auth/login')
  })

  it('encodes return url', () => {
    expect(loginUrl('/settings?tab=email')).toBe(
      '/api/auth/login?returnUrl=%2Fsettings%3Ftab%3Demail',
    )
  })
})

import { describe, expect, it } from 'vitest'
import { formatRelative, formatRetryAfter } from './dateTime'

const NOW = Date.parse('2026-10-26T12:00:00Z')

describe('formatRelative', () => {
  it.each([
    ['2026-10-26T11:59:40Z', 'przed chwilą'],
    ['2026-10-26T11:55:00Z', '5 minut temu'],
    ['2026-10-26T09:00:00Z', '3 godziny temu'],
    ['2026-10-25T12:00:00Z', 'wczoraj'],
    ['2026-10-21T12:00:00Z', '5 dni temu'],
  ])('formats %s as %s', (iso, expected) => {
    expect(formatRelative(iso, NOW)).toBe(expected)
  })
})

describe('formatRetryAfter', () => {
  it.each([
    [undefined, 'za chwilę'],
    [30, 'za 30 sekund'],
    [91, 'za 2 minuty'],
  ])('formats %s seconds as %s', (seconds, expected) => {
    expect(formatRetryAfter(seconds)).toBe(expected)
  })
})

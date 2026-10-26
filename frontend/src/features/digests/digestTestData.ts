import type { DigestDetail, DigestListItem } from './useDigests'

export const currentUser = {
  id: '0192f0c4-0000-7000-8000-000000000001',
  login: 'octocat',
  avatarUrl: null,
  githubTokenValid: true,
  missingScopes: [],
  isAdmin: false,
}

export const sentDigest: DigestListItem = {
  id: '0192f0c4-0000-7000-8000-0000000000a1',
  digestDate: '2026-10-21',
  status: 'Sent',
  subject: 'DevPulse – 2 rzeczy na dziś',
  sentAt: '2026-10-21T05:00:00Z',
  aiUsed: true,
  priorities: [
    {
      repoFullName: 'octocat/hello-world',
      text: 'CI na main nie przechodzi od wczoraj.',
      severity: 'High',
    },
    {
      repoFullName: 'octocat/spoon-knife',
      text: 'PR #41 czeka na review od 9 dni.',
      severity: 'Medium',
    },
  ],
}

export const failedDigest: DigestListItem = {
  id: '0192f0c4-0000-7000-8000-0000000000a2',
  digestDate: '2026-10-20',
  status: 'Failed',
  subject: null,
  sentAt: null,
  aiUsed: false,
  priorities: [],
}

export function detailOf(
  item: DigestListItem,
  overrides: Partial<DigestDetail> = {},
): DigestDetail {
  return {
    ...item,
    timeZoneId: 'Europe/Warsaw',
    recipientEmail: 'octocat@example.com',
    attemptCount: 1,
    error: null,
    hasHtml: true,
    ...overrides,
  }
}

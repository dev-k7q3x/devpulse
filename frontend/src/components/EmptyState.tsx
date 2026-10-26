import type { ReactNode } from 'react'

interface EmptyStateProps {
  title: string
  description?: string
  children?: ReactNode
}

/** Pusty stan widoku – tytuł, krótki opis i akcja (np. wybór repozytoriów). */
export function EmptyState({ title, description, children }: EmptyStateProps) {
  return (
    <section className="empty-state" aria-label={title}>
      <h2>{title}</h2>
      {description && <p>{description}</p>}
      {children}
    </section>
  )
}

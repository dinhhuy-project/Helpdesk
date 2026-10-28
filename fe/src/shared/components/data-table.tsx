import type { Key, ReactNode } from 'react'
import { EmptyState, ErrorState, LoadingState } from './async-state'

export interface TableColumn<TRow> {
  key: string
  header: ReactNode
  render: (row: TRow) => ReactNode
}

export function DataTable<TRow>({ caption, columns, rows, rowKey, loading = false, error, onRetry, emptyMessage }: {
  caption: string
  columns: readonly TableColumn<TRow>[]
  rows: readonly TRow[]
  rowKey: (row: TRow) => Key
  loading?: boolean
  error?: unknown
  onRetry?: () => void
  emptyMessage?: string
}) {
  if (loading) return <LoadingState />
  if (error) return <ErrorState error={error} onRetry={onRetry} />
  if (!rows.length) return <EmptyState description={emptyMessage} />
  return (
    <div className="table-scroll" role="region" aria-label={caption} tabIndex={0}>
      <table className="data-table">
        <caption className="sr-only">{caption}</caption>
        <thead><tr>{columns.map(column => <th scope="col" key={column.key}>{column.header}</th>)}</tr></thead>
        <tbody>{rows.map(row => <tr key={rowKey(row)}>{columns.map(column => <td key={column.key}>{column.render(row)}</td>)}</tr>)}</tbody>
      </table>
    </div>
  )
}

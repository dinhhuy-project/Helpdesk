import { useEffect, useId, useRef } from 'react'
import type { ReactNode } from 'react'
import { Button } from './button'

export function Dialog({ open, onOpenChange, title, description, children }: {
  open: boolean; onOpenChange: (open: boolean) => void; title: string; description?: string; children: ReactNode
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const descriptionId = useId()

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog ref={ref} className="dialog" aria-labelledby={titleId}
      aria-describedby={description ? descriptionId : undefined}
      onCancel={event => { event.preventDefault(); onOpenChange(false) }}
      onClose={() => { if (open) onOpenChange(false) }}>
      <h2 id={titleId}>{title}</h2>
      {description && <p id={descriptionId} className="dialog-description">{description}</p>}
      <div className="dialog-content">{children}</div>
      <div className="dialog-actions"><Button variant="secondary" onClick={() => onOpenChange(false)}>Đóng</Button></div>
    </dialog>
  )
}

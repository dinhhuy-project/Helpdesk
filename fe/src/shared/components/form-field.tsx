import { useId } from 'react'
import type { ComponentProps, ReactNode } from 'react'

interface FieldControlProps {
  id: string
  required?: boolean
  'aria-invalid'?: true
  'aria-describedby'?: string
}

interface FormFieldProps {
  id?: string
  label: string
  hint?: string
  error?: string
  required?: boolean
  children: (props: FieldControlProps) => ReactNode
}

export function FormField({ id, label, hint, error, required, children }: FormFieldProps) {
  const generatedId = useId()
  const controlId = id ?? generatedId
  const descriptionIds = [hint && `${controlId}-hint`, error && `${controlId}-error`].filter(Boolean).join(' ')
  return (
    <div className="form-field">
      <label htmlFor={controlId}>{label}{required && <span aria-hidden="true"> *</span>}</label>
      {children({ id: controlId, required, 'aria-invalid': error ? true : undefined,
        'aria-describedby': descriptionIds || undefined })}
      {hint && <p id={`${controlId}-hint`} className="field-hint">{hint}</p>}
      {error && <p id={`${controlId}-error`} className="field-error" role="alert">{error}</p>}
    </div>
  )
}

export function Input({ className = '', ...props }: ComponentProps<'input'>) {
  return <input {...props} className={`form-control ${className}`} />
}
export function Textarea({ className = '', ...props }: ComponentProps<'textarea'>) {
  return <textarea {...props} className={`form-control ${className}`} />
}
export function Select({ className = '', ...props }: ComponentProps<'select'>) {
  return <select {...props} className={`form-control ${className}`} />
}

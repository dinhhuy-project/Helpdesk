import type { ComponentProps } from 'react'

interface ButtonProps extends ComponentProps<'button'> {
  variant?: 'primary' | 'secondary' | 'danger'
  pending?: boolean
}

export function Button({ variant = 'primary', pending = false, disabled, className = '', children,
  type = 'button', ...props }: ButtonProps) {
  return (
    <button {...props} type={type} className={`button button--${variant} ${className}`}
      disabled={disabled || pending} aria-busy={pending || undefined}>
      {children}
    </button>
  )
}

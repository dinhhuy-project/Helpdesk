import { useState } from 'react'
import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/api-error'
import { Button } from './button'
import { FormField, Input } from './form-field'
import { DataTable } from './data-table'
import { Dialog } from './dialog'
import { Pagination } from './pagination'

describe('shared accessible components', () => {
  it('associates field label, hint and server validation with the input', async () => {
    render(<FormField label="Email" hint="Email công ty" error="Email chưa hợp lệ" required>
      {props => <Input {...props} type="email" />}
    </FormField>)
    const input = screen.getByRole('textbox', { name: /Email/ })
    expect(input).toBeRequired()
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAccessibleDescription('Email công ty Email chưa hợp lệ')
    await userEvent.type(input, 'person@example.test')
    expect(input).toHaveValue('person@example.test')
  })

  it('prevents repeat submissions while a button is pending', async () => {
    const onClick = vi.fn()
    render(<Button pending onClick={onClick}>Lưu</Button>)
    await userEvent.click(screen.getByRole('button', { name: 'Lưu' }))
    expect(onClick).not.toHaveBeenCalled()
    expect(screen.getByRole('button')).toHaveAttribute('aria-busy', 'true')
  })

  it('paginates using server totals and disables boundary controls', async () => {
    const onPageChange = vi.fn()
    const { rerender } = render(<Pagination page={1} pageSize={20} totalCount={21} onPageChange={onPageChange} />)
    expect(screen.getByRole('button', { name: 'Trang trước' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Trang sau' }))
    expect(onPageChange).toHaveBeenCalledWith(2)
    rerender(<Pagination page={2} pageSize={20} totalCount={21} onPageChange={onPageChange} />)
    expect(screen.getByRole('button', { name: 'Trang sau' })).toBeDisabled()
  })

  it('does not offer navigation when there are no results', () => {
    render(<Pagination page={1} pageSize={20} totalCount={0} onPageChange={vi.fn()} />)
    for (const button of screen.getAllByRole('button')) expect(button).toBeDisabled()
  })

  it('does not render stale rows while loading or when the query fails', () => {
    const props = { caption: 'Ticket', rows: [{ id: '1', title: 'Private ticket' }], rowKey: (row: { id: string }) => row.id,
      columns: [{ key: 'title', header: 'Tiêu đề', render: (row: { title: string }) => row.title }] }
    const { rerender } = render(<DataTable {...props} loading />)
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(screen.queryByText('Private ticket')).not.toBeInTheDocument()
    rerender(<DataTable {...props} error={new ApiError('Không có quyền', 'http', 403, { traceId: 'trace-id' })} />)
    expect(screen.getByRole('alert')).toHaveTextContent('trace-id')
    expect(screen.queryByText('Private ticket')).not.toBeInTheDocument()
  })

  it('renders user content as text instead of executable HTML', () => {
    render(<DataTable caption="Ticket" rows={['<script>alert(1)</script>']} rowKey={row => row}
      columns={[{ key: 'title', header: 'Tiêu đề', render: row => row }]} />)
    expect(within(screen.getByRole('table')).getByText('<script>alert(1)</script>')).toBeInTheDocument()
    expect(document.querySelector('script')).toBeNull()
  })

  it('keeps the native dialog synchronized with controlled close and Escape cancellation', async () => {
    function Example() {
      const [open, setOpen] = useState(false)
      return <><Button onClick={() => setOpen(true)}>Mở</Button>
        <Dialog open={open} onOpenChange={setOpen} title="Xác nhận" description="Kiểm tra trước khi lưu"><p>Nội dung</p></Dialog></>
    }
    render(<Example />)
    await userEvent.click(screen.getByRole('button', { name: 'Mở' }))
    const dialog = screen.getByRole('dialog', { name: 'Xác nhận' })
    expect(dialog).toHaveAttribute('open')
    expect(dialog).toHaveAccessibleDescription('Kiểm tra trước khi lưu')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Đóng' }))
    expect(dialog).not.toHaveAttribute('open')
    await userEvent.click(screen.getByRole('button', { name: 'Mở' }))
    fireEvent(dialog, new Event('cancel', { bubbles: false, cancelable: true }))
    expect(dialog).not.toHaveAttribute('open')
  })
})

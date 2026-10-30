import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { FormField, Input } from '../../../shared/components/form-field'
import { Pagination } from '../../../shared/components/pagination'
import { createTicketApi } from '../api/ticket-api'
import { ticketKeys } from '../hooks/use-tickets'
import type { AssignmentAgent, AssignmentTeam } from '../types/ticket-workflow'

type PickerProps = { id: string; disabled: boolean } & (
  { kind: 'team'; onSelect: (value: AssignmentTeam) => void } |
  { kind: 'agent'; teamId: string; onSelect: (value: AssignmentAgent | null) => void }
)
export function TicketAssignmentPicker(props: PickerProps) {
  const [open, setOpen] = useState(false)
  const label = props.kind === 'team' ? 'Chọn nhóm hỗ trợ' : 'Chọn người xử lý'
  return <><Button variant="secondary" disabled={props.disabled} onClick={() => setOpen(true)}>{label}</Button>
    <Dialog open={open && !props.disabled} onOpenChange={setOpen} title={label}>
      {open && !props.disabled && <AssignmentChoices {...props} onClose={() => setOpen(false)} />}
    </Dialog></>
}
function AssignmentChoices(props: PickerProps & { onClose: () => void }) {
  const { client, scope } = useAuth()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const teamId = props.kind === 'agent' ? props.teamId : null
  const query = useQuery({ queryKey: [...ticketKeys.resource(scope, props.id), 'assignment', props.kind, teamId, { search, page }],
    staleTime: 0, gcTime: 0, refetchOnMount: 'always',
    queryFn: async ({ signal }) => {
      const api = createTicketApi(client)
      const filter = { page, pageSize: 20, search }
      return props.kind === 'team' ? api.assignmentTeams(props.id, filter, signal) : api.assignmentAgents(props.id, props.teamId, filter, signal)
    } })
  const apply = () => { setSearch(text.trim()); setPage(1) }
  return <div className="assignment-choices">
    <div role="search" aria-label="Tìm lựa chọn phân công" onKeyDown={event => {
      if (event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); apply() }
    }}><FormField label={props.kind === 'team' ? 'Tìm nhóm hoặc phòng ban' : 'Tìm người xử lý'}>{field =>
      <Input {...field} value={text} maxLength={200} onChange={event => setText(event.target.value)} />}</FormField>
      <Button variant="secondary" onClick={apply}>Tìm kiếm</Button></div>
    {query.isFetching || query.isPending ? <LoadingState /> : query.isError ? <ErrorState error={query.error} onRetry={() => { void query.refetch() }} /> : <>
      {props.kind === 'agent' && <Button variant="secondary" onClick={() => { props.onSelect(null); props.onClose() }}>Chỉ giao nhóm, bỏ người xử lý</Button>}
      {query.data.items.length === 0 ? <EmptyState title="Không có lựa chọn phù hợp" /> : <ul className="assignment-options">
        {query.data.items.map(item => <li key={item.id}><Button variant="secondary" title={item.id} onClick={() => {
          if (props.kind === 'team' && 'name' in item) props.onSelect(item)
          else if (props.kind === 'agent' && 'displayName' in item) props.onSelect(item)
          props.onClose()
        }}>{'name' in item ? `${item.name} · ${item.departmentName}` : `${item.displayName} · ${item.id.slice(-8)}`}</Button></li>)}
      </ul>}
      <Pagination page={page} pageSize={20} totalCount={query.data.totalCount} onPageChange={setPage} />
    </>}
  </div>
}

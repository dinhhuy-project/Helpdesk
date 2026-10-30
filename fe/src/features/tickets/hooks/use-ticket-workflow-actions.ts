import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { ApiError, toApiError } from '../../../shared/api/api-error'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { createTicketApi } from '../api/ticket-api'
import { ticketKeys, useTicketWorkflow } from './use-tickets'
import type { TicketWorkflowContext, WorkflowDraft } from '../types/ticket-workflow'

export function useTicketWorkflowActions(id: string, enabled: boolean) {
  const { client, scope } = useAuth()
  const queries = useQueryClient()
  const command = useApiCommand()
  const [draft, setDraft] = useState<WorkflowDraft | null>(null)
  const [success, setSuccess] = useState('')
  const [refreshing, setRefreshing] = useState(false)
  const [readError, setReadError] = useState<ApiError | null>(null)
  const [review, setReview] = useState<TicketWorkflowContext | null>(null)
  const reading = useRef(false)
  const controller = useRef<AbortController | null>(null)
  const workflow = useTicketWorkflow(id, enabled && !refreshing && !readError)
  useEffect(() => () => controller.current?.abort(), [])
  const rejected = command.error?.status === 403 || command.error?.status === 404 ||
    (workflow.error instanceof ApiError && (workflow.error.status === 403 || workflow.error.status === 404))
  useEffect(() => {
    if (rejected) {
      void queries.cancelQueries({ queryKey: ticketKeys.resource(scope, id) })
      queries.removeQueries({ queryKey: ticketKeys.resource(scope, id), predicate: query => query.queryKey[3] !== 'access' && query.queryKey[3] !== 'workflow' })
    }
  }, [rejected, queries, scope, id])

  async function verify(forReview: boolean) {
    if (reading.current || !command.isCurrent()) return
    reading.current = true
    const abort = new AbortController(); controller.current = abort
    setRefreshing(true); setReadError(null); setReview(null)
    try {
      // Hide and remove private content before checking scope. A transfer receipt is not a detail DTO.
      await queries.cancelQueries({ queryKey: ticketKeys.resource(scope, id) })
      queries.removeQueries({ queryKey: ticketKeys.resource(scope, id), predicate: query => query.queryKey[3] !== 'access' && query.queryKey[3] !== 'workflow' })
      const api = createTicketApi(client)
      const access = await api.access(id, abort.signal)
      if (!access.canRead) throw new ApiError('Yêu cầu không còn trong phạm vi truy cập.', 'http', 404)
      const context = await api.workflow(id, abort.signal)
      const detail = await api.detail(id, abort.signal)
      if (context.version !== detail.version) throw new ApiError('Yêu cầu vừa thay đổi. Hãy tải lại trước khi tiếp tục.', 'unexpected')
      if (!command.isCurrent() || abort.signal.aborted) return
      queries.setQueryData(ticketKeys.access(scope, id), access)
      queries.setQueryData(ticketKeys.workflow(scope, id), context)
      queries.setQueryData(ticketKeys.detail(scope, id), detail)
      if (forReview) setReview(context)
    } catch (failure) {
      if (!command.isCurrent() || abort.signal.aborted) return
      queries.removeQueries({ queryKey: ticketKeys.resource(scope, id) })
      setReadError(toApiError(failure))
    } finally {
      reading.current = false
      if (controller.current === abort) controller.current = null
      if (command.isCurrent()) setRefreshing(false)
    }
  }

  const blocked = command.disabled || command.error?.status === 409 || refreshing || !!readError || !!review ||
    workflow.isFetching || !workflow.data || workflow.isError || (draft !== null && draft.version !== workflow.data.version)
  async function submit(kind: 'assign' | 'transition', value: WorkflowDraft) {
    if (blocked || !workflow.data || value.version !== workflow.data.version || !command.isCurrent()) return
    const context = workflow.data
    if (kind === 'assign' && (!context.canAssign || !value.team || !value.agentChosen || !value.assignmentReviewed)) return
    if (kind === 'transition' && (!value.status || !context.transitions.includes(value.status) ||
      (value.status === 4 && (!value.resolutionSummary.trim() || value.resolutionSummary.length > 4000)))) return
    setSuccess('')
    await command.run(async signal => {
      const api = createTicketApi(client)
      const receipt = kind === 'assign'
        ? await api.assign(id, { teamId: value.team!.id, agentId: value.agent?.id ?? null, version: value.version }, signal)
        : await api.transition(id, { status: value.status!, resolutionSummary: value.status === 4 ? value.resolutionSummary : null, version: value.version }, signal)
      if (receipt.id !== id || !Number.isInteger(receipt.version) || receipt.version <= 0) throw new ApiError('Không xác nhận được kết quả thay đổi.', 'unexpected')
      return receipt
    }, async () => {
      setSuccess(kind === 'assign' ? 'Đã phân công yêu cầu.' : 'Đã chuyển trạng thái yêu cầu.')
      setDraft(null)
      await verify(false)
    })
  }
  function acceptReview() {
    if (!review || refreshing || workflow.isFetching || workflow.data?.version !== review.version || !command.resetAfterVerification()) return
    // Keep all entered content, but require selecting the destination again from fresh scoped lookups.
    setDraft(value => value ? { ...value, version: review.version, assignmentReviewed: false } : null)
    setReview(null)
  }
  return { workflow, draft, setDraft, command, submit, success, refreshing, readError, review, blocked,
    reload: () => { void verify(true) }, acceptReview }
}
export type TicketWorkflowActions = ReturnType<typeof useTicketWorkflowActions>

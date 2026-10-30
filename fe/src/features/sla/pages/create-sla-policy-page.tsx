import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/use-auth'
import { PageTitle } from '../../../shared/components/page-title'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { createSlaApi } from '../api/sla-api'
import { SlaPolicyForm } from '../components/sla-policy-form'
import { SlaCommandFeedback, SlaError } from '../components/sla-state'
import { emptySlaDraft } from '../lib/sla-policy-form'
import type { SlaPolicyInput } from '../types/sla'

export function CreateSlaPolicyPage() {
  const { client } = useAuth()
  const [draft, setDraft] = useState(emptySlaDraft)
  const command = useApiCommand()
  const navigate = useNavigate()
  async function save(value: SlaPolicyInput) {
    const result = await command.run(signal => createSlaApi(client).create(value, signal))
    if (result) void navigate(`/admin/sla-policies/${result.id}`, { replace: true, state: { slaCreated: true } })
  }
  if (command.error?.status === 403 || command.error?.status === 404) return <SlaError error={command.error} />
  return <section className="account-page"><PageTitle title="Tạo policy SLA" /><h1>Tạo policy SLA</h1><p><Link to="/admin/sla-policies">Về danh sách policy</Link></p>
    <div className="panel"><SlaPolicyForm draft={draft} onChange={setDraft} onSave={value => { void save(value) }} creating
      disabled={command.disabled} pending={command.pending} error={command.error} /><SlaCommandFeedback command={command} /></div>
  </section>
}

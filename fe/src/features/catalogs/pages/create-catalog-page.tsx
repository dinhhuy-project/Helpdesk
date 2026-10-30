import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/use-auth'
import { ForbiddenPage } from '../../../app/router/forbidden-page'
import { LoadingState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { useCatalogApi, useTeamAccess } from '../hooks/use-catalogs'
import { CatalogEditor } from '../components/catalog-editor'
import type { CatalogEditorDraft } from '../components/catalog-editor'
import { CatalogError, CatalogFeedback } from '../components/catalog-state'
import { verifyCatalogReceipt } from '../lib/catalog-receipt'
import { catalogLabels } from '../lib/catalog-search'
import type { CatalogKind } from '../types/catalog'

export function CreateCatalogPage({ kind }: { kind: CatalogKind }) {
  const { scope } = useAuth()
  const access = useTeamAccess(undefined, kind === 'teams')
  if (kind === 'teams') {
    if (access.error) return <CatalogError error={access.error} onRetry={() => { void access.refetch() }} />
    if (!access.data) return <LoadingState label="Đang kiểm tra phạm vi tạo nhóm…" />
    if (!access.data.canCreate) return <ForbiddenPage />
  }
  return <CreateCatalogForm key={`${scope}:${kind}`} kind={kind} />
}
function CreateCatalogForm({ kind }: { kind: CatalogKind }) {
  const { api } = useCatalogApi()
  const command = useApiCommand()
  const navigate = useNavigate()
  const label = catalogLabels[kind]
  async function save(draft: CatalogEditorDraft) {
    if (kind === 'teams' && !draft.departmentId) return
    const name = { name: draft.name, description: draft.description }
    const result = await command.run(async signal => {
      if (kind === 'departments') return verifyCatalogReceipt(await api.create(kind, name, signal))
      if (kind === 'teams' && draft.departmentId) return verifyCatalogReceipt(await api.create(kind, { ...name, departmentId: draft.departmentId }, signal))
      return verifyCatalogReceipt(await api.create('categories', { ...name, parentId: draft.parentId }, signal))
    })
    if (result) void navigate(`${label.path}/${result.id}`, { replace: true, state: { catalogCreated: true } })
  }
  return <section className="account-page catalog-page"><PageTitle title={`Tạo ${label.singular}`} /><p className="eyebrow">Quản trị dữ liệu</p><h1>Tạo {label.singular}</h1>
    <p><Link to={label.path}>Về danh sách</Link></p>
    <div className="panel account-form"><CatalogEditor kind={kind} command={command} onSave={save} /><CatalogFeedback command={command} listPath={label.path} /></div>
  </section>
}

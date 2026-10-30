import { useContext } from 'react'
import { useStore } from 'zustand'
import { Button } from '../../../shared/components/button'
import { RealtimeContext } from '../realtime/realtime-context'
import type { NotificationRealtime } from '../realtime/notification-realtime'

export function RealtimeStatus() {
  const realtime = useContext(RealtimeContext)
  return realtime ? <ConnectionStatus realtime={realtime} /> : null
}
function ConnectionStatus({ realtime }: { realtime: NotificationRealtime }) {
  const status = useStore(realtime.store, state => state.status)
  return <div className="realtime-status" role="status" aria-live="polite">
    {status === 'connected' ? <span>Đang nhận cập nhật trực tiếp.</span> : <span>
      {status === 'offline' ? 'Cập nhật trực tiếp tạm ngắt.' : 'Đang kết nối cập nhật trực tiếp…'} Bạn vẫn có thể tải lại thông báo như bình thường.</span>}
    {status === 'offline' && <Button variant="secondary" onClick={realtime.resume}>Kết nối lại</Button>}
  </div>
}

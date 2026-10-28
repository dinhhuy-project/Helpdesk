import { ErrorState, LoadingState } from '../../../shared/components/async-state'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useHealth } from '../hooks/use-health'
import { PageTitle } from '../../../shared/components/page-title'

export function HomePage() {
  const health = useHealth()
  return (
    <>
      <PageTitle title="Trang chủ" />
      <section className="intro" aria-labelledby="welcome-title">
        <p className="eyebrow">Helpdesk / Hỗ trợ nội bộ</p>
        <h1 id="welcome-title">Kết nối hỗ trợ.<br />Tiếp nối công việc.</h1>
        <p className="intro-description">Một nơi để tiếp nhận yêu cầu, phối hợp giữa các bộ phận và theo dõi quá trình giải quyết vấn đề.</p>
      </section>
      <div className="information-grid">
        <section className="information-card"><span className="card-number">01 / TIẾP NHẬN</span><h2>Yêu cầu rõ ràng</h2><p>Mô tả vấn đề và thông tin liên quan để bộ phận hỗ trợ có thể bắt đầu đúng hướng.</p></section>
        <section className="information-card"><span className="card-number">02 / PHỐI HỢP</span><h2>Đúng người, đúng việc</h2><p>Kết nối người yêu cầu với nhóm hỗ trợ phù hợp trong doanh nghiệp.</p></section>
        <section className="information-card"><span className="card-number">03 / THEO DÕI</span><h2>Thông tin xuyên suốt</h2><p>Trao đổi tập trung và theo dõi tiến trình xử lý của từng yêu cầu.</p></section>
      </div>
      <section className="service-card" aria-labelledby="service-title">
        <h2 id="service-title">Kết nối dịch vụ</h2>
        {health.isPending && <LoadingState label="Đang kiểm tra kết nối…" />}
        {health.isError && <ErrorState error={health.error} onRetry={() => { void health.refetch() }} />}
        {health.isSuccess && <>
          <p className="service-status" role="status"><span className="status-dot" aria-hidden="true" />Dịch vụ đang phản hồi</p>
          <p className="field-hint">Kiểm tra gần nhất: {formatDateTime(new Date(health.dataUpdatedAt).toISOString())}</p>
        </>}
      </section>
    </>
  )
}

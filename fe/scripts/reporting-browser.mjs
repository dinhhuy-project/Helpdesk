import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runReportingBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || undefined, headless: true })
  const pages = [], errors = []
  let closing = false
  try {
    async function open(credentials) {
      // Daily labels must remain UTC dates even where UTC midnight is the preceding local day.
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, timezoneId: 'America/New_York' })
      // Verify report REST/focus/manual reconciliation with realtime unavailable.
      await context.route('**/hubs/**', route => route.abort())
      await context.route('**/api/v1/**', async route => {
        try {
          const url = new URL(route.request().url())
          await route.fulfill({ response: await route.fetch({ url: input.baseUrl + url.pathname + url.search }) })
        } catch {
          if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed')
          await route.abort().catch(() => {})
        }
      })
      const page = await context.newPage(); pages.push(page)
      page.on('pageerror', () => errors.push('browser page error'))
      page.on('console', message => {
        if (message.type() === 'error' && !message.location().url.includes('/hubs/')) errors.push('browser console error')
      })
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      await page.getByRole('navigation', { name: 'Điều hướng chính', exact: true }).getByRole('link', { name: 'Tổng quan', exact: true }).click()
      await page.getByRole('table', { name: 'Chỉ số SLA', exact: true }).waitFor()
      // Local New York time corresponds exactly to the fixture's half-open UTC cohort.
      await page.getByLabel('Tạo từ', { exact: true }).fill('2025-12-31T19:00')
      await page.getByLabel('Tạo đến', { exact: true }).fill('2026-01-03T19:00')
      await page.getByRole('button', { name: 'Áp dụng bộ lọc', exact: true }).click()
      await page.getByRole('table', { name: 'Chỉ số SLA', exact: true }).waitFor()
      assert.equal(new URL(page.url()).searchParams.get('createdFrom'), input.filter.createdFrom)
      assert.equal(new URL(page.url()).searchParams.get('createdTo'), input.filter.createdTo)
      return page
    }
    const requester = await open(input.requester), agent = await open(input.agent)
    const total = page => page.locator('.report-totals > div').filter({ has: page.locator('dt', { hasText: /^Tổng Ticket$/ }) }).locator('dd')
    const waitTotal = (page, value) => total(page).filter({ hasText: new RegExp(`^${value}$`) }).waitFor()
    await waitTotal(requester, 3); await waitTotal(agent, 2)
    assert.equal(await total(requester).textContent(), '3'); assert.equal(await total(agent).textContent(), '2')
    await requester.getByText('Xem bảng số liệu từng ngày', { exact: true }).click()
    const daily = requester.getByRole('table', { name: 'Ticket tạo từng ngày UTC', exact: true })
    assert.equal(await daily.getByRole('cell', { name: '1/1/26', exact: true }).count(), 1)
    assert.equal(await daily.getByRole('cell', { name: '3/1/26', exact: true }).count(), 1)
    assert.equal(await daily.getByRole('cell', { name: '31/12/25', exact: true }).count(), 0)
    assert.equal(await requester.getByRole('cell', { name: '50%', exact: true }).count(), 2)
    await requester.getByRole('link', { name: 'Xem báo cáo cùng bộ lọc', exact: true }).focus()
    await requester.keyboard.press('Enter')
    const groups = requester.getByRole('table', { name: 'Theo nhóm hỗ trợ', exact: true })
    await groups.waitFor()
    await groups.getByRole('cell', { name: 'Chưa phân công', exact: true }).waitFor()
    await requester.getByRole('combobox', { name: 'Nhóm báo cáo', exact: true }).selectOption('agents')
    await requester.getByRole('table', { name: 'Theo nhân viên xử lý', exact: true }).waitFor()
    assert.equal(new URL(requester.url()).searchParams.get('view'), 'agents')
    await requester.goBack()
    await groups.waitFor()
    await requester.getByRole('button', { name: 'Team A', exact: true }).click()
    await requester.getByRole('button', { name: 'Bỏ lọc nhóm', exact: true }).waitFor()
    await requester.getByRole('table', { name: 'Theo nhóm hỗ trợ', exact: true }).waitFor()
    await waitTotal(requester, 2)
    assert.equal(await total(requester).textContent(), '2')
    await requester.getByRole('button', { name: 'Bỏ lọc nhóm', exact: true }).click()
    await requester.getByRole('table', { name: 'Theo nhóm hỗ trợ', exact: true }).waitFor()
    await waitTotal(requester, 3)
    assert.equal(await total(requester).textContent(), '3')
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) await requester.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'reports-desktop.png'), fullPage: true })
    await requester.setViewportSize({ width: 390, height: 844 })
    assert(await requester.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    const region = requester.getByRole('region', { name: 'Theo nhóm hỗ trợ', exact: true })
    await region.focus(); await requester.keyboard.press('ArrowRight')
    assert(await region.evaluate(element => element.scrollWidth > element.clientWidth))
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) await requester.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'reports-mobile.png'), fullPage: true })
    await input.setMembership(false)
    await agent.getByRole('button', { name: 'Tải lại số liệu', exact: true }).click()
    await agent.getByText('Không có Ticket trong phạm vi và bộ lọc này.', { exact: true }).waitFor()
    assert.equal(await total(agent).textContent(), '0')
    assert(await agent.getByText('Chưa có mẫu đo', { exact: true }).count() >= 2)
    await input.setMembership(true)
    await agent.getByRole('button', { name: 'Tải lại số liệu', exact: true }).click()
    await agent.getByRole('table', { name: 'Chỉ số SLA', exact: true }).waitFor()
    await waitTotal(agent, 2)
    assert.equal(await total(agent).textContent(), '2')
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS)
      for (const [index, page] of pages.entries()) await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `reports-failure-${index}.png`), fullPage: true }).catch(() => {})
    throw failure
  } finally { closing = true; await browser.close() }
}

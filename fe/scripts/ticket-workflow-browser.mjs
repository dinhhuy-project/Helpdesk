import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runWorkflowBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || 'msedge', headless: true })
  const errors = [], pages = []
  let closing = false
  try {
    async function open(credentials) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
      await context.route('**/api/v1/**', async route => {
        try {
          const url = new URL(route.request().url())
          const response = await route.fetch({ url: input.baseUrl + url.pathname + url.search })
          await route.fulfill({ response })
        } catch {
          if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed')
          await route.abort().catch(() => {})
        }
      })
      const page = await context.newPage(); pages.push(page)
      page.on('pageerror', () => errors.push('browser page error'))
      page.on('console', message => {
        // The stale-version case deliberately receives 409. Chromium also logs that HTTP status to console.
        if (message.type() === 'error' && !message.text().includes('status of 409')) errors.push('browser console error')
      })
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const allocator = await open(input.allocator), leader = await open(input.leader)
    const agent = await open(input.agent), requester = await open(input.requester)
    const agentOther = await open(input.agent)
    const ticket = await input.create('Browser workflow')
    async function detail(page, selected = ticket, title = input.prefix + 'Browser workflow') {
      // Credentials are memory-only: navigate inside the SPA rather than reload the document.
      if ((await page.viewportSize()).width < 900) await page.setViewportSize({ width: 1440, height: 1000 })
      await page.locator('.desktop-sidebar').getByRole('link', { name: 'Danh sách yêu cầu', exact: true }).click()
      await page.getByRole('textbox', { name: 'Tìm trong tiêu đề', exact: true }).fill(title)
      await page.getByRole('button', { name: 'Áp dụng bộ lọc', exact: true }).click()
      await page.locator(`a[href="/tickets/${selected.id}"]`).click()
      await page.getByRole('heading', { name: title, exact: true }).waitFor()
      await page.getByRole('heading', { name: 'Phân công và xử lý', exact: true }).waitFor()
    }
    async function confirm(page, kind = 'chuyển trạng thái') {
      await page.getByRole('dialog', { name: `Xác nhận ${kind}`, exact: true }).getByRole('button', { name: 'Xác nhận thao tác', exact: true }).click()
      await page.getByText(kind === 'phân công' ? 'Đã phân công yêu cầu.' : 'Đã chuyển trạng thái yêu cầu.', { exact: true }).waitFor()
      await page.getByRole('heading', { name: 'Phân công và xử lý', exact: true }).waitFor()
    }
    async function assign(page, teamId, groupOnly) {
      await page.getByRole('button', { name: 'Chọn nhóm hỗ trợ', exact: true }).click()
      const teams = page.getByRole('dialog', { name: 'Chọn nhóm hỗ trợ', exact: true })
      const response = await input.services.allocator.api.assignmentTeams(ticket.id, { page: 1, pageSize: 100, search: '' }, new AbortController().signal)
      const team = response.items.find(item => item.id === teamId)
      assert(team)
      await teams.getByRole('button', { name: `${team.name} · ${team.departmentName}`, exact: true }).click()
      assert(await page.getByRole('button', { name: 'Lưu phân công', exact: true }).isDisabled())
      await page.getByRole('button', { name: 'Chọn người xử lý', exact: true }).click()
      await page.getByRole('dialog', { name: 'Chọn người xử lý', exact: true }).getByRole('button', {
        name: groupOnly ? 'Chỉ giao nhóm, bỏ người xử lý' : `${input.agentName} · ${input.agentId.slice(-8)}`, exact: true,
      }).click()
      await page.getByRole('button', { name: 'Lưu phân công', exact: true }).click()
      await confirm(page, 'phân công')
    }
    async function transition(page, status, summary) {
      const select = page.getByRole('combobox', { name: 'Thao tác', exact: true })
      await select.selectOption(String(status))
      if (summary) await page.getByRole('textbox', { name: 'Kết quả giải quyết', exact: true }).fill(summary)
      await page.getByRole('button', { name: 'Xem lại chuyển trạng thái', exact: true }).click()
      await confirm(page)
    }
    await detail(allocator)
    assert.equal(await allocator.locator('.desktop-sidebar').getByRole('link', { name: 'Nhóm hỗ trợ', exact: true }).count(), 0)
    await assign(allocator, input.teamA, true)
    await detail(leader)
    assert.equal(await leader.getByRole('combobox', { name: 'Thao tác', exact: true }).count(), 0)
    await assign(leader, input.teamB, false)
    await detail(agent)
    assert.equal(await agent.getByRole('button', { name: 'Chọn nhóm hỗ trợ', exact: true }).count(), 0)
    await transition(agent, 2)
    await transition(agent, 3)
    await transition(agent, 2)
    await detail(agentOther)
    const summary = 'Kết quả được giữ qua xung đột phiên bản.'
    await agent.getByRole('combobox', { name: 'Thao tác', exact: true }).selectOption('4')
    await agent.getByRole('textbox', { name: 'Kết quả giải quyết', exact: true }).fill(summary)
    // Another authenticated browser session commits first while this form remains open.
    await transition(agentOther, 3)
    await agent.getByRole('button', { name: 'Xem lại chuyển trạng thái', exact: true }).click()
    await agent.getByRole('dialog', { name: 'Xác nhận chuyển trạng thái', exact: true }).getByRole('button', { name: 'Xác nhận thao tác', exact: true }).click()
    await agent.getByText(/Yêu cầu đã thay đổi. Bản nhập được giữ lại/).waitFor()
    assert.equal(await agent.getByRole('textbox', { name: 'Kết quả giải quyết', exact: true }).inputValue(), summary)
    await agent.getByRole('button', { name: 'Tải lại để xem xét', exact: true }).click()
    await agent.getByRole('button', { name: 'Dùng phiên bản mới để tiếp tục', exact: true }).click()
    assert(await agent.getByRole('button', { name: 'Xem lại chuyển trạng thái', exact: true }).isDisabled())
    assert.equal(await agent.getByRole('textbox', { name: 'Kết quả giải quyết', exact: true }).inputValue(), summary)
    await agent.setViewportSize({ width: 390, height: 844 })
    await agent.getByRole('textbox', { name: 'Kết quả giải quyết', exact: true }).waitFor()
    assert(await agent.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await agent.getByRole('heading', { name: 'Phân công và xử lý', exact: true }).scrollIntoViewIfNeeded()
      await agent.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'workflow-conflict-mobile.png'), fullPage: true })
    }
    await transition(agent, 2)
    await transition(agent, 4, 'Đã giải quyết sau khi xem lại.')
    await detail(requester)
    assert.equal(await requester.getByRole('button', { name: 'Chọn nhóm hỗ trợ', exact: true }).count(), 0)
    await transition(requester, 7)
    await detail(agent)
    await transition(agent, 2); await transition(agent, 4, 'Hoàn tất.')
    await detail(requester); await transition(requester, 5)
    await requester.getByText('Hiện không có bước chuyển trạng thái được phép.', { exact: true }).waitFor()
    const cancelTicket = await input.create('Browser cancel')
    await detail(requester, cancelTicket, input.prefix + 'Browser cancel')
    await transition(requester, 6)
    await requester.getByText('Hiện không có bước chuyển trạng thái được phép.', { exact: true }).waitFor()
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await leader.evaluate(() => scrollTo(0, 0))
      await leader.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'workflow-assignment-desktop.png'), fullPage: true })
    }
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS)
      for (const [index, page] of pages.entries()) await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `workflow-failure-${index}.png`), fullPage: true }).catch(() => {})
    throw failure
  } finally { closing = true; await browser.close() }
}

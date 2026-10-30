import assert from 'node:assert/strict'
import AxeBuilder from '@axe-core/playwright'
import { writeFile } from 'node:fs/promises'
import { join } from 'node:path'

/** Extra release checks reuse the workflow fixture; all writes remain in its disposable database. */
export async function runReleaseChecks(browser, input, pages, ticket) {
  const audits = []
  async function audit(page, name) {
    const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()
    // Never save DOM snippets, input values, request headers or browser traces containing tokens.
    const violations = result.violations.map(item => ({ id: item.id, impact: item.impact, count: item.nodes.length }))
    audits.push({ name, violations })
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) await writeFile(join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'release-accessibility.json'), JSON.stringify(audits, null, 2))
    assert.deepEqual(violations, [], `Accessibility violations on ${name}: ${violations.map(item => item.id).join(', ')}`)
  }
  for (const [index, page] of pages.entries()) await audit(page, `workflow-role-${index}`)
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
  const page = await context.newPage(), errors = []
  let failLists = false, loseCreateResponse = false, createPosts = 0, created
  page.on('pageerror', () => errors.push('browser page error'))
  await context.route('**/hubs/**', route => route.abort())
  await context.route('**/api/v1/**', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.pathname === '/api/v1/tickets' && request.method() === 'GET' && failLists) { await route.abort(); return }
    const creating = url.pathname === '/api/v1/tickets' && request.method() === 'POST'
    if (creating) createPosts++
    try {
      const response = await route.fetch({ url: input.baseUrl + url.pathname + url.search })
      if (creating && loseCreateResponse && response.ok()) {
        created = await response.json(); await route.abort(); return // Commit succeeded; the browser never receives the receipt.
      }
      await route.fulfill({ response })
    } catch { await route.abort().catch(() => {}) }
  })
  const login = async credentials => {
    await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
    await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
  }
  try {
    await page.goto(`${process.env.HELPDESK_FRONTEND_URL}/tickets/${ticket.id}`)
    await page.getByRole('heading', { name: 'Đăng nhập', exact: true }).waitFor()
    assert.equal(new URL(page.url()).searchParams.get('returnTo'), `/tickets/${ticket.id}`)
    await audit(page, 'login-deep-link')
    await login(input.requester)
    await page.getByRole('heading', { name: input.prefix + 'Browser workflow', exact: true }).waitFor()
    await page.locator('.desktop-sidebar').getByRole('link', { name: 'Tạo yêu cầu', exact: true }).click()
    const title = input.prefix + 'Lost create receipt'
    await page.getByRole('textbox', { name: 'Tiêu đề', exact: false }).fill(title)
    await page.getByRole('textbox', { name: 'Nội dung yêu cầu', exact: false }).fill('A committed create must never be retried automatically.')
    await page.getByRole('searchbox', { name: 'Tìm danh mục trong cấp đang mở', exact: true }).fill(input.prefix)
    await page.getByRole('button', { name: 'Tìm danh mục', exact: true }).click()
    await page.locator(`input[type="radio"][value="${input.categoryId}"]`).check()
    await audit(page, 'create-ticket')
    loseCreateResponse = true
    await page.getByRole('button', { name: 'Gửi yêu cầu', exact: true }).click()
    await page.getByRole('heading', { name: 'Chưa xác định được kết quả tạo', exact: true }).waitFor()
    assert.equal(createPosts, 1)
    assert(await page.getByRole('button', { name: 'Gửi yêu cầu', exact: true }).isDisabled())
    assert.equal((await input.services.requester.api.list({ page: 1, pageSize: 1, search: title, sortBy: 'createdAt', descending: true }, new AbortController().signal)).totalCount, 1)
    assert(created?.id)
    failLists = true
    await page.getByRole('link', { name: 'Kiểm tra danh sách yêu cầu', exact: true }).click()
    await page.getByRole('heading', { name: 'Không thể tải dữ liệu', exact: true }).waitFor()
    failLists = false
    await page.getByRole('button', { name: 'Thử lại', exact: true }).click()
    await page.locator(`a[href="/tickets/${created.id}"]`).click()
    await page.getByRole('heading', { name: title, exact: true }).waitFor()
    assert.equal(createPosts, 1)
    const storage = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))
    assert(!/accessToken|refreshToken|eyJ[A-Za-z0-9_-]+\./.test(storage))
    await page.reload() // Memory-only credentials require login; private data must disappear.
    await page.getByRole('heading', { name: 'Đăng nhập', exact: true }).waitFor()
    assert.equal(await page.getByRole('heading', { name: title, exact: true }).count(), 0)
    await login(input.agent) // Same URL, another identity: unassigned requester Ticket is outside its scope.
    await page.getByRole('heading', { name: 'Không tìm thấy yêu cầu', exact: true }).waitFor()
    assert.equal(await page.getByText(title, { exact: true }).count(), 0)
    await page.setViewportSize({ width: 390, height: 844 })
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    const menu = page.getByRole('button', { name: 'Mở menu điều hướng', exact: true })
    await menu.focus(); await page.keyboard.press('Enter')
    const dialog = page.getByRole('dialog', { name: 'Điều hướng', exact: true })
    await dialog.waitFor(); await audit(page, 'mobile-navigation')
    await page.keyboard.press('Tab')
    assert(await dialog.evaluate(element => element.contains(document.activeElement)))
    await page.keyboard.press('Escape')
    assert(await menu.evaluate(element => element === document.activeElement))
    await audit(page, 'mobile-outside-scope')
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click()
    await page.getByRole('heading', { name: 'Đăng nhập', exact: true }).waitFor()
    assert.deepEqual(errors, [])
  } finally { await context.close() }
}

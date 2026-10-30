import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runAttachmentBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || undefined, headless: true })
  const errors = [], pages = [], apiRequests = []
  let closing = false
  try {
    async function open(credentials) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, acceptDownloads: true })
      await context.route('**/api/v1/**', async route => {
        try {
          const request = route.request(), url = new URL(request.url())
          apiRequests.push({ path: url.pathname, params: url.searchParams, bearer: !!request.headers().authorization })
          const response = await route.fetch({ url: input.baseUrl + url.pathname + url.search })
          await route.fulfill({ response })
        } catch {
          if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed')
          await route.abort().catch(() => {})
        }
      })
      await context.route('**/hubs/**', route => route.abort())
      const page = await context.newPage(); pages.push(page)
      page.on('pageerror', () => errors.push('browser page error'))
      page.on('console', message => { if (message.type() === 'error' && !message.location().url.includes('/hubs/')) errors.push('browser console error') })
      page.on('request', request => {
        const url = new URL(request.url())
        if (url.protocol.startsWith('http') && url.origin !== new URL(process.env.HELPDESK_FRONTEND_URL).origin) errors.push('Unexpected direct storage/external request')
      })
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const requester = await open(input.requester), agent = await open(input.agent)
    const ticket = await input.create('Attachments browser')
    await input.services.coordinator.tickets.assign(ticket.id, { teamId: input.teamId, agentId: input.agentId, version: ticket.version }, input.signal())
    async function detail(page) {
      await page.locator('.desktop-sidebar').getByRole('link', { name: 'Danh sách yêu cầu', exact: true }).click()
      await page.getByRole('textbox', { name: 'Tìm trong tiêu đề', exact: true }).fill(input.prefix + 'Attachments browser')
      await page.getByRole('button', { name: 'Áp dụng bộ lọc', exact: true }).click()
      await page.locator(`a[href="/tickets/${ticket.id}"]`).click()
      await page.getByRole('heading', { name: input.prefix + 'Attachments browser', exact: true }).waitFor()
    }
    async function upload(page, region, name, content) {
      await region.getByLabel('Chọn file đính kèm', { exact: true }).setInputFiles({ name, mimeType: 'text/plain', buffer: Buffer.from(content) })
      await region.getByRole('button', { name: 'Tải file lên', exact: true }).click()
      await page.getByText('Đã tải file lên.', { exact: true }).waitFor()
      await region.getByText(name, { exact: true }).waitFor()
    }
    await detail(requester)
    const files = requester.getByRole('region', { name: 'File của yêu cầu', exact: true })
    assert.equal(await files.getByRole('button', { name: 'File nội bộ', exact: true }).count(), 0)
    const body = 'Browser UTF8 content: tiếng Việt.'
    await upload(requester, files, 'browser-evidence.txt', body)
    const downloadPromise = requester.waitForEvent('download')
    await files.getByRole('button', { name: 'Tải xuống browser-evidence.txt', exact: true }).click()
    const download = await downloadPromise
    assert.equal(download.suggestedFilename(), 'browser-evidence.txt')
    assert.equal((await readFile(await download.path())).toString('utf8'), body)
    const text = 'Bình luận công khai có file sau khi lưu.'
    await requester.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill(text)
    await requester.getByRole('button', { name: 'Gửi bình luận', exact: true }).click()
    const row = requester.locator('.comment-item').filter({ hasText: text })
    await row.getByRole('button', { name: 'File của bình luận', exact: true }).click()
    const dialog = requester.getByRole('dialog', { name: 'File đính kèm bình luận', exact: true })
    await upload(requester, dialog, 'browser-comment.txt', 'Comment file uploaded after persisted comment.')
    await dialog.getByRole('button', { name: 'Đóng', exact: true }).click()
    await detail(agent)
    await agent.locator('.comment-item').filter({ hasText: text }).getByRole('button', { name: 'File của bình luận', exact: true }).click()
    const other = agent.getByRole('dialog', { name: 'File đính kèm bình luận', exact: true })
    await other.getByText('Bạn hiện không được đính kèm file tại đây.', { exact: true }).waitFor()
    assert.equal(await other.getByLabel('Chọn file đính kèm', { exact: true }).count(), 0)
    await other.getByText('browser-comment.txt', { exact: true }).waitFor()
    await other.getByRole('button', { name: 'Đóng', exact: true }).click()
    await agent.getByRole('button', { name: 'Ghi chú nội bộ', exact: true }).click()
    const note = 'Ghi chú nội bộ có file riêng.'
    await agent.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill(note)
    await agent.getByRole('button', { name: 'Gửi ghi chú nội bộ', exact: true }).click()
    await agent.locator('.comment-item').filter({ hasText: note }).getByRole('button', { name: 'File của bình luận', exact: true }).click()
    const internalDialog = agent.getByRole('dialog', { name: 'File đính kèm bình luận', exact: true })
    await upload(agent, internalDialog, 'browser-internal.txt', 'Private support note file.')
    assert.equal(await internalDialog.getByRole('button', { name: 'File công khai', exact: true }).count(), 0)
    await internalDialog.getByRole('button', { name: 'Đóng', exact: true }).click()
    const agentFiles = agent.getByRole('region', { name: 'File của yêu cầu', exact: true })
    await agentFiles.getByRole('button', { name: 'File nội bộ', exact: true }).click()
    await agentFiles.getByText('browser-internal.txt', { exact: true }).waitFor()
    await agent.setViewportSize({ width: 390, height: 844 })
    await agentFiles.getByText('browser-internal.txt', { exact: true }).waitFor()
    assert(await agent.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await detail(requester)
    assert.equal(await requester.getByRole('button', { name: 'File nội bộ', exact: true }).count(), 0)
    assert.equal(await requester.getByText('browser-internal.txt', { exact: true }).count(), 0)
    assert.equal(await requester.getByText(note, { exact: true }).count(), 0)
    assert(apiRequests.filter(request => request.path.includes('/attachments')).every(request => request.bearer))
    assert(apiRequests.some(request => request.path.endsWith('/download')))
    assert(apiRequests.some(request => request.path.endsWith('/attachments') && request.params.get('commentId')))
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await requester.evaluate(() => scrollTo(0, 0))
      await requester.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'attachments-requester-desktop.png'), fullPage: true })
      await agentFiles.getByRole('heading', { name: 'File đính kèm', exact: true }).scrollIntoViewIfNeeded()
      await agent.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'attachments-internal-mobile.png'), fullPage: true })
    }
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS)
      for (const [index, page] of pages.entries()) await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `attachments-failure-${index}.png`), fullPage: true }).catch(() => {})
    throw failure
  } finally { closing = true; await browser.close() }
}

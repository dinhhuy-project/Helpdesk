import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'

export async function runCommunityBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || undefined, headless: true })
  const frontend = process.env.HELPDESK_FRONTEND_URL
  const pages = []; const errors = []; let closing = false
  try {
    const context = await browser.newContext({ viewport: { width: 1366, height: 900 } })
    await context.route('**/api/v1/**', async route => {
      try {
        const url = new URL(route.request().url())
        if (url.pathname.startsWith('/api/v1/public/')) assert.equal(route.request().headers().authorization, undefined)
        const response = await route.fetch({ url: input.baseUrl + url.pathname + url.search })
        await route.fulfill({ response })
      } catch { if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed'); await route.abort().catch(() => {}) }
    })
    // JS10/native server hub transport is exercised separately in community-journey.
    await context.route('**/hubs/**', route => route.abort())
    const page = await context.newPage(); pages.push(page); page.on('pageerror', () => errors.push('browser page error'))
    await page.goto(frontend + '/knowledge')
    await page.getByRole('heading', { name: 'Kho tri thức', exact: true }).waitFor()
    await page.getByRole('link', { name: input.article.title, exact: true }).waitFor()
    await page.getByRole('link', { name: input.article.title, exact: true }).click()
    await page.getByRole('heading', { name: input.article.title, exact: true }).waitFor()
    assert.equal(await page.getByRole('button', { name: 'Hướng dẫn hữu ích', exact: true }).count(), 0)
    await page.setViewportSize({ width: 390, height: 844 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
    const { default: AxeBuilder } = await import('@axe-core/playwright')
    const accessibility = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()
    assert.equal(accessibility.violations.length, 0, 'Public article accessibility')
    await page.goto(frontend + `/incidents/${input.incident.id}`)
    await page.getByRole('heading', { name: input.incident.title, exact: true }).waitFor()
    await page.getByRole('heading', { name: 'Tiến trình xử lý', exact: true }).waitFor()
    await page.goto(frontend + '/login'); await page.getByRole('textbox', { name: 'Email', exact: false }).fill(input.admin.email)
    await page.getByLabel('Mật khẩu', { exact: false }).fill(input.admin.password)
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
    await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
    await page.setViewportSize({ width: 1366, height: 900 })
    await page.locator('.desktop-sidebar').getByRole('link', { name: 'Biên soạn tri thức', exact: true }).click()
    await page.getByRole('link', { name: 'Soạn nội dung mới', exact: true }).click()
    await page.getByRole('heading', { name: 'Soạn nội dung mới', exact: true }).waitFor()
    await page.getByRole('textbox', { name: 'Tiêu đề', exact: true }).fill('Browser community safe guide')
    await page.getByRole('textbox', { name: 'Chủ đề', exact: true }).fill('Demo')
    await page.getByRole('textbox', { name: 'Tóm tắt', exact: true }).fill('Public browser test summary')
    await page.getByRole('textbox', { name: 'Nội dung hướng dẫn', exact: true }).fill('Step one. Step two.')
    await page.getByRole('button', { name: 'Lưu bản soạn', exact: true }).click()
    await page.getByRole('heading', { name: 'Biên soạn và xuất bản', exact: true }).waitFor()
    assert.equal(await page.getByRole('button', { name: 'Xuất bản công khai', exact: true }).isEnabled(), false)
    await page.getByRole('checkbox', { name: /Tôi đã kiểm tra nội dung/ }).check()
    await page.getByRole('button', { name: 'Xuất bản công khai', exact: true }).click()
    await page.getByText('Đã xuất bản nội dung công khai.', { exact: true }).waitFor()
    const id = new URL(page.url()).pathname.split('/').at(-1)
    assert.equal((await input.publicApi.get('knowledge', id)).title, 'Browser community safe guide')
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click()
    assert.deepEqual(errors, [])
  } finally { closing = true; for (const page of pages) await page.close().catch(() => {}); await browser.close() }
}

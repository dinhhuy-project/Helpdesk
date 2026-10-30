import assert from 'node:assert/strict'
import { randomBytes } from 'node:crypto'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runAccountBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || 'msedge', headless: true })
  let closing = false
  const errors = []
  const pages = []
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
      const page = await context.newPage(); pages.push(page); page.on('pageerror', () => errors.push('browser page error'))
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const admin = await open(input.admin)
    await admin.locator('.desktop-sidebar').getByRole('link', { name: 'Tài khoản người dùng', exact: true }).click()
    await admin.getByRole('link', { name: 'Tạo tài khoản', exact: true }).click()
    await admin.getByRole('textbox', { name: 'Email', exact: true }).fill(`${input.prefix}-browser@example.test`)
    await admin.getByRole('textbox', { name: 'Tên hiển thị', exact: true }).fill('Browser account')
    await admin.getByLabel('Mật khẩu ban đầu', { exact: false }).fill(randomBytes(24).toString('hex'))
    await admin.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click()
    await admin.getByRole('heading', { name: 'Browser account', exact: true }).waitFor()
    await admin.getByRole('button', { name: 'Chọn phòng ban', exact: true }).click()
    const departments = admin.getByRole('dialog', { name: 'Chọn phòng ban đang hoạt động', exact: true })
    await departments.getByRole('textbox', { name: 'Tìm phòng ban', exact: true }).fill(input.departmentName)
    await departments.getByRole('button', { name: 'Tìm phòng ban', exact: true }).click()
    await departments.getByRole('button', { name: input.departmentName, exact: true }).click()
    await admin.getByRole('button', { name: 'Lưu thông tin', exact: true }).click()
    await admin.getByText('Đã cập nhật tài khoản.', { exact: true }).waitFor()
    await admin.getByRole('link', { name: 'Quản lý vai trò của tài khoản này', exact: true }).click()
    await admin.getByRole('checkbox', { name: /Nhân viên hỗ trợ/ }).check()
    await admin.getByRole('button', { name: 'Xem lại và lưu vai trò', exact: true }).click()
    await admin.getByRole('dialog').getByRole('button', { name: 'Xác nhận lưu vai trò', exact: true }).click()
    await admin.getByText('Đã cập nhật vai trò.', { exact: true }).waitFor()
    await admin.locator('.desktop-sidebar').getByRole('link', { name: 'Vai trò và quyền', exact: true }).click()
    await admin.getByRole('link', { name: 'Nhật ký quản trị', exact: true }).click()
    await admin.getByRole('table', { name: 'Nhật ký quản trị Identity', exact: true }).waitFor()
    const manager = await open(input.manager)
    assert.equal(await manager.locator('.desktop-sidebar').getByRole('link', { name: 'Vai trò và quyền', exact: true }).count(), 0)
    await manager.locator('.desktop-sidebar').getByRole('link', { name: 'Tài khoản người dùng', exact: true }).click()
    await manager.getByRole('searchbox', { name: 'Tìm theo tên hoặc email', exact: true }).fill(`${input.prefix}-browser@example.test`)
    await manager.getByRole('button', { name: 'Áp dụng bộ lọc', exact: true }).click()
    await manager.getByRole('link', { name: 'Browser account', exact: true }).click()
    await manager.getByRole('button', { name: 'Vô hiệu hóa tài khoản', exact: true }).click()
    await manager.getByRole('dialog').getByRole('button', { name: 'Xác nhận thay đổi trạng thái', exact: true }).click()
    await manager.getByText('Đã cập nhật tài khoản.', { exact: true }).waitFor()
    await manager.getByRole('button', { name: 'Kích hoạt tài khoản', exact: true }).waitFor()
    await manager.setViewportSize({ width: 390, height: 844 })
    await manager.getByRole('heading', { name: 'Browser account', exact: true }).waitFor()
    assert(await manager.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await manager.getByRole('button', { name: 'Chọn phòng ban', exact: true }).focus()
    assert(await manager.getByRole('button', { name: 'Chọn phòng ban', exact: true }).evaluate(el => el === document.activeElement))
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await manager.evaluate(() => scrollTo(0, 0))
      await manager.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'accounts-mobile.png'), fullPage: true })
      await admin.evaluate(() => scrollTo(0, 0))
      await admin.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'accounts-audit-desktop.png'), fullPage: true })
    }
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      for (const [index, page] of pages.entries()) {
        await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `accounts-failure-${index}.png`), fullPage: true }).catch(() => {})
      }
    }
    throw failure
  } finally { closing = true; await browser.close() }
}

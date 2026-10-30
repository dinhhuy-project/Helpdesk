import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runCatalogBrowser(input) {
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
      page.on('console', message => { if (message.type() === 'error') errors.push('browser console error') })
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const admin = await open(input.admin)
    const departmentName = `${input.prefix} Browser department`, teamName = `${input.prefix} Browser team`
    const rootName = `${input.prefix} Browser root`, childName = `${input.prefix} Browser child`
    await admin.locator('.desktop-sidebar').getByRole('link', { name: 'Phòng ban', exact: true }).click()
    await admin.getByRole('link', { name: 'Tạo phòng ban', exact: true }).click()
    await admin.getByRole('textbox', { name: 'Tên', exact: true }).fill(departmentName)
    await admin.getByRole('button', { name: 'Tạo mới', exact: true }).click()
    await admin.getByRole('heading', { name: departmentName, exact: true }).waitFor()
    await admin.getByRole('link', { name: 'Nhóm thuộc phòng ban này', exact: true }).click()
    await admin.getByRole('link', { name: 'Tạo nhóm hỗ trợ', exact: true }).click()
    await admin.getByRole('textbox', { name: 'Tên', exact: true }).fill(teamName)
    await admin.getByRole('button', { name: 'Chọn phòng ban hoạt động', exact: true }).click()
    let dialog = admin.getByRole('dialog', { name: 'Chọn phòng ban hoạt động', exact: true })
    await dialog.getByRole('textbox', { name: 'Tìm phòng ban', exact: true }).fill(departmentName)
    await dialog.getByRole('button', { name: 'Tìm kiếm', exact: true }).click()
    await dialog.getByRole('button', { name: departmentName, exact: true }).click()
    await admin.getByRole('button', { name: 'Tạo mới', exact: true }).click()
    await admin.getByRole('heading', { name: teamName, exact: true }).waitFor()
    async function confirmMember(page) {
      await page.getByRole('dialog', { name: 'Xác nhận thay đổi thành viên', exact: true }).getByRole('button', { name: 'Xác nhận thay đổi thành viên', exact: true }).click()
    }
    async function addMember(name) {
      await admin.getByRole('button', { name: 'Thêm thành viên', exact: true }).click()
      const candidates = admin.getByRole('dialog', { name: 'Chọn người dùng hoạt động', exact: true })
      await candidates.getByRole('textbox', { name: 'Tìm thành viên theo tên', exact: true }).fill(name)
      await candidates.getByRole('button', { name: 'Tìm kiếm', exact: true }).click()
      await candidates.getByRole('button', { name: new RegExp(name) }).click()
      await confirmMember(admin)
      await admin.getByRole('row').filter({ hasText: name }).waitFor()
    }
    await addMember(input.leaderName)
    const leaderRow = admin.getByRole('row').filter({ hasText: input.leaderName })
    await leaderRow.getByRole('button', { name: 'Chỉ định trưởng nhóm', exact: true }).click()
    await confirmMember(admin)
    await leaderRow.getByRole('button', { name: 'Gỡ trưởng nhóm', exact: true }).waitFor()
    await addMember(input.candidateName)
    const memberRow = admin.getByRole('row').filter({ hasText: input.candidateName })
    await memberRow.getByRole('button', { name: 'Gỡ khỏi nhóm', exact: true }).click()
    await confirmMember(admin)
    await memberRow.getByRole('button', { name: 'Kích hoạt lại', exact: true }).click()
    await confirmMember(admin)
    await memberRow.getByRole('button', { name: 'Ngừng tham gia', exact: true }).waitFor()
    const leader = await open(input.leader)
    await leader.locator('.desktop-sidebar').getByRole('link', { name: 'Nhóm hỗ trợ', exact: true }).click()
    await leader.getByRole('link', { name: teamName, exact: true }).click()
    await leader.getByRole('table', { name: 'Thành viên nhóm hỗ trợ', exact: true }).waitFor()
    assert.equal(await leader.getByRole('button', { name: 'Gỡ trưởng nhóm', exact: true }).count(), 0)
    assert.equal(await leader.getByRole('button', { name: 'Vô hiệu hóa', exact: true }).count(), 0)
    assert.equal(await leader.locator('.desktop-sidebar').getByRole('link', { name: 'Tài khoản người dùng', exact: true }).count(), 0)
    await leader.setViewportSize({ width: 390, height: 844 })
    await leader.getByRole('heading', { name: teamName, exact: true }).waitFor()
    assert(await leader.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await leader.getByRole('textbox', { name: 'Tên', exact: true }).focus()
    assert(await leader.getByRole('textbox', { name: 'Tên', exact: true }).evaluate(element => element === document.activeElement))
    await admin.locator('.desktop-sidebar').getByRole('link', { name: 'Danh mục yêu cầu', exact: true }).click()
    await admin.getByRole('link', { name: 'Tạo danh mục', exact: true }).click()
    await admin.getByRole('textbox', { name: 'Tên', exact: true }).fill(rootName)
    await admin.getByRole('button', { name: 'Tạo mới', exact: true }).click()
    await admin.getByRole('heading', { name: rootName, exact: true }).waitFor()
    await admin.getByRole('link', { name: 'Xem danh mục con', exact: true }).click()
    await admin.getByRole('link', { name: 'Tạo danh mục', exact: true }).click()
    await admin.getByRole('textbox', { name: 'Tên', exact: true }).fill(childName)
    await admin.getByRole('button', { name: 'Chọn danh mục cha', exact: true }).click()
    dialog = admin.getByRole('dialog', { name: 'Chọn danh mục cha khả dụng', exact: true })
    await dialog.getByRole('textbox', { name: 'Tìm danh mục cha', exact: true }).fill(rootName)
    await dialog.getByRole('button', { name: 'Tìm kiếm', exact: true }).click()
    await dialog.getByRole('button', { name: rootName, exact: true }).click()
    await admin.getByRole('button', { name: 'Tạo mới', exact: true }).click()
    await admin.getByRole('heading', { name: childName, exact: true }).waitFor()
    const requester = await open(input.requester)
    assert.equal(await requester.locator('.desktop-sidebar').getByRole('link', { name: 'Danh mục yêu cầu', exact: true }).count(), 0)
    await requester.locator('.desktop-sidebar').getByRole('link', { name: 'Tạo yêu cầu', exact: true }).click()
    await requester.getByRole('textbox', { name: 'Tiêu đề', exact: true }).fill(`${input.prefix} Browser ticket`)
    await requester.getByRole('textbox', { name: 'Nội dung yêu cầu', exact: true }).fill('Ticket uses the category administered in another browser session.')
    await requester.getByRole('button', { name: `Mở danh mục ${rootName}`, exact: true }).click()
    await requester.getByRole('radio', { name: childName, exact: true }).check()
    await requester.getByRole('button', { name: 'Gửi yêu cầu', exact: true }).click()
    await requester.getByRole('heading', { name: `${input.prefix} Browser ticket`, exact: true }).waitFor()
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await leader.evaluate(() => scrollTo(0,0))
      await leader.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'catalog-team-mobile.png'), fullPage: true })
      await admin.evaluate(() => scrollTo(0,0))
      await admin.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'catalog-category-desktop.png'), fullPage: true })
      await requester.evaluate(() => scrollTo(0,0))
      await requester.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'catalog-ticket-desktop.png'), fullPage: true })
    }
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      for (const [index, page] of pages.entries()) await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `catalog-failure-${index}.png`), fullPage: true }).catch(() => {})
    }
    throw failure
  } finally { closing = true; await browser.close() }
}

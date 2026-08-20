import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/layouts/MainLayout.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

test('侧栏子菜单默认全部收起', () => {
  assert.match(source, /const defaultOpeneds: string\[\] = \[\]/)
  assert.equal(source.match(/:default-openeds="defaultOpeneds"/g)?.length, 2)
})

test('Telegram API 和设备指纹作为侧栏独立入口', async () => {
  const routerSource = await readFile(new URL('../src/router/index.ts', import.meta.url), 'utf8')
  assert.match(source, /label: 'Telegram API'/)
  assert.match(source, /label: '设备指纹'/)
  assert.doesNotMatch(source, /label: 'Telegram 设置'/)
  assert.match(routerSource, /path: 'telegram-api'/)
  assert.match(routerSource, /path: 'device-profiles'/)
})

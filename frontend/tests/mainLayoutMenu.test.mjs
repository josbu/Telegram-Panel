import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/layouts/MainLayout.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

test('侧栏子菜单默认全部收起', () => {
  assert.match(source, /const defaultOpeneds: string\[\] = \[\]/)
  assert.equal(source.match(/:default-openeds="defaultOpeneds"/g)?.length, 2)
})

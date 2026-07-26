import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/views/Accounts.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

test('批量切换账号代理时不默认选择直连', () => {
  assert.match(source, /strategy: '' as AccountProxyStrategy \| ''/)
  assert.match(
    source,
    /proxyDialog\.strategy = row[\s\S]*\? row\.proxy \? 'existing'[\s\S]*: ''/,
  )
  assert.match(source, /v-if="!proxyDialog\.strategy"/)
})

test('账号代理策略未明确选择时禁止提交', () => {
  assert.match(source, /if \(!strategy\) \{[\s\S]*请先明确选择本次账号切换使用的代理方式/)
})

test('账号列表使用代理编号作为主要标识并保留名称提示', () => {
  assert.equal(source.match(/#\{\{ row\.proxy\.id \}\}/g)?.length, 2)
  assert.equal(source.match(/:content="row\.proxy\.name \|\| `代理 #\$\{row\.proxy\.id\}`"/g)?.length, 2)
  assert.doesNotMatch(source, /<span class="proxy-name">\{\{ row\.proxy\.name \}\}<\/span>/)
})

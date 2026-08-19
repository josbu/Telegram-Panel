import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/views/Accounts.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

test('批量切换账号代理时不默认选择直连', () => {
  assert.match(source, /strategy: '' as AccountProxyBatchStrategy \| ''/)
  assert.match(
    source,
    /proxyDialog\.strategy = row[\s\S]*\? row\.proxy \? 'existing'[\s\S]*: ''/,
  )
  assert.match(source, /v-if="!proxyDialog\.strategy"/)
})

test('账号代理策略未明确选择时禁止提交', () => {
  assert.match(source, /if \(!strategy\) \{[\s\S]*请先明确选择本次账号切换使用的代理方式/)
})

test('账号列表批量代理一对一需要代理文本并走批量接口', () => {
  assert.match(source, /value="proxy_per_account">批量代理一对一/)
  assert.match(source, /proxyText: ''/)
  assert.match(source, /function countEffectiveProxyLines\(text: string\)/)
  assert.match(source, /strategy === 'proxy_per_account' \? proxyDialog\.proxyText : null/)
  assert.match(source, /accountIds\.length === 1 && strategy !== 'proxy_per_account'/)
  assert.match(source, /panelApi\.batchSetAccountProxy\(accountIds, payload\)/)
})

test('账号列表使用代理编号作为主要标识并保留名称提示', () => {
  assert.equal(source.match(/#\{\{ row\.proxy\.id \}\}/g)?.length, 2)
  assert.equal(source.match(/:content="row\.proxy\.name \|\| `代理 #\$\{row\.proxy\.id\}`"/g)?.length, 2)
  assert.doesNotMatch(source, /<span class="proxy-name">\{\{ row\.proxy\.name \}\}<\/span>/)
})

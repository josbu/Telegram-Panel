import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const proxiesSource = await readFile(new URL('../src/views/Proxies.vue', import.meta.url), 'utf8')
const panelApiSource = await readFile(new URL('../src/api/panel.ts', import.meta.url), 'utf8')
const typesSource = await readFile(new URL('../src/api/types.ts', import.meta.url), 'utf8')

test('代理列表支持对勾选项执行批量删除', () => {
  assert.match(proxiesSource, />\s*批量删除/)
  assert.match(proxiesSource, /selectedProxies\.value\.map\(\(proxy\) => proxy\.id\)/)
  assert.match(proxiesSource, /panelApi\.batchDeleteProxies\(proxyIds\)/)
  assert.match(panelApiSource, /\/proxies\/batch\/delete/)
})

test('批量删除展示逐项失败且保留被占用代理', () => {
  assert.match(typesSource, /interface ProxyBatchResult/)
  assert.match(proxiesSource, /result\.items/)
  assert.match(proxiesSource, /item\.error \|\| item\.summary/)
  assert.match(proxiesSource, /已绑定账号或正在作为全局代理使用的项目会逐项失败并保留/)
})

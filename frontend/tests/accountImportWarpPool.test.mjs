import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../src/views/AccountImport.vue', import.meta.url), 'utf8')

test('账号导入只允许自动分配已有 WARP', () => {
  assert.match(source, /value="warp_pool"[^>]*>自动分配已有 WARP/)
  assert.doesNotMatch(source, /value="warp_per_account"/)
  assert.doesNotMatch(source, /每个账号都会创建一个独立 Docker 容器和数据卷/)
  assert.match(source, /不会创建新的 Docker 容器|不会创建新容器/)
})

test('自动 WARP 池不依赖创建环境并随导入请求提交', () => {
  assert.doesNotMatch(source, /proxyStrategy\.value === 'warp_pool' && !warpAvailable\.value/)
  assert.match(source, /const selectedStrategy = proxyStrategy\.value/)
  assert.match(source, /form\.append\('proxyStrategy', strategy\)/)
})

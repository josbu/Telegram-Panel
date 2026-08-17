import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const appSource = await readFile(new URL('../src/App.vue', import.meta.url), 'utf8')
const mainSource = await readFile(new URL('../src/main.ts', import.meta.url), 'utf8')
const runtimeErrorsSource = await readFile(new URL('../src/utils/runtimeErrors.ts', import.meta.url), 'utf8')

test('正常取消和路由取消不会触发页面加载失败遮罩', () => {
  assert.match(runtimeErrorsSource, /isNavigationFailure\(error\)/)
  assert.match(runtimeErrorsSource, /\^\(cancel\|close\|canceled\|cancelled\)\$/)
  assert.match(runtimeErrorsSource, /Navigation \(aborted\|cancelled\|canceled\|duplicated\)/)
  assert.match(runtimeErrorsSource, /error\.name === 'AbortError'/)
  assert.match(mainSource, /if \(isBenignRuntimeError\(error\)\) return/)
  assert.match(appSource, /isBenignRuntimeError\(custom\.detail\)/)
})

test('致命 Promise 错误仍保留页面加载失败提示', () => {
  assert.match(runtimeErrorsSource, /Failed to fetch dynamically imported module/)
  assert.match(runtimeErrorsSource, /Cannot read properties/)
  assert.match(appSource, /if \(!isFatalPromiseError\(event\.reason\)\) return/)
  assert.match(appSource, /errorMessage\.value = toRuntimeErrorMessage\(event\.reason\)/)
})

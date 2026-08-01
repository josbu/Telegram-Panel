import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const utilityUrl = new URL('../src/utils/clipboard.ts', import.meta.url)
const utilitySource = await readFile(utilityUrl, 'utf8')

const viewFiles = ['BotChannels.vue', 'ChatResources.vue', 'ApiCenter.vue']
const viewSources = await Promise.all(
  viewFiles.map(async (file) => readFile(new URL(`../src/views/${file}`, import.meta.url), 'utf8')),
)

test('剪贴板工具在 Clipboard API 缺失或失败时回退到传统复制', () => {
  assert.match(utilitySource, /clipboard\?\.writeText/)
  assert.match(utilitySource, /catch\s*\{/)
  assert.match(utilitySource, /document\.execCommand\('copy'\)/)
  assert.match(utilitySource, /textarea\.remove\(\)/)
})

test('所有复制入口统一使用兼容剪贴板工具', () => {
  for (const source of viewSources) {
    assert.match(source, /import \{ writeClipboardText \} from '@\/utils\/clipboard'/)
    assert.doesNotMatch(source, /navigator\.clipboard\.writeText/)
  }
  assert.equal(viewSources.reduce((count, source) => count + (source.match(/await writeClipboardText\(/g)?.length ?? 0), 0), 4)
})

import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import ts from 'typescript'

const utilityUrl = new URL('../src/utils/clipboard.ts', import.meta.url)
const utilitySource = await readFile(utilityUrl, 'utf8')
const utilityModuleSource = ts.transpileModule(utilitySource, {
  compilerOptions: {
    module: ts.ModuleKind.ESNext,
    target: ts.ScriptTarget.ES2022,
  },
}).outputText
const utilityModuleUrl = `data:text/javascript;base64,${Buffer.from(utilityModuleSource).toString('base64')}`
const { writeClipboardText } = await import(utilityModuleUrl)

const viewFiles = ['BotChannels.vue', 'ChatResources.vue', 'ApiCenter.vue']
const viewSources = await Promise.all(
  viewFiles.map(async (file) => readFile(new URL(`../src/views/${file}`, import.meta.url), 'utf8')),
)

async function withBrowserEnvironment({ clipboard, execCommandResult }, action) {
  const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator')
  const originalDocument = Object.getOwnPropertyDescriptor(globalThis, 'document')
  const state = {
    appended: 0,
    copied: 0,
    focused: 0,
    selected: 0,
    rangeSelected: 0,
    removed: 0,
  }
  const textarea = {
    value: '',
    style: {},
    setAttribute() {},
    focus() { state.focused += 1 },
    select() { state.selected += 1 },
    setSelectionRange() { state.rangeSelected += 1 },
    remove() { state.removed += 1 },
  }
  const document = {
    body: {
      appendChild(node) {
        assert.equal(node, textarea)
        state.appended += 1
      },
    },
    createElement(tag) {
      assert.equal(tag, 'textarea')
      return textarea
    },
    execCommand(command) {
      assert.equal(command, 'copy')
      state.copied += 1
      return execCommandResult
    },
  }

  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { clipboard } })
  Object.defineProperty(globalThis, 'document', { configurable: true, value: document })
  try {
    await action(state)
  } finally {
    if (originalNavigator) Object.defineProperty(globalThis, 'navigator', originalNavigator)
    else delete globalThis.navigator
    if (originalDocument) Object.defineProperty(globalThis, 'document', originalDocument)
    else delete globalThis.document
  }
}

test('缺少 Clipboard API 时执行传统复制并清理临时文本框', async () => {
  await withBrowserEnvironment({ clipboard: undefined, execCommandResult: true }, async (state) => {
    await writeClipboardText('fallback text')
    assert.deepEqual(state, {
      appended: 1,
      copied: 1,
      focused: 1,
      selected: 1,
      rangeSelected: 1,
      removed: 1,
    })
  })
})

test('Clipboard API 拒绝访问时回退传统复制', async () => {
  const clipboard = { writeText: async () => { throw new Error('denied') } }
  await withBrowserEnvironment({ clipboard, execCommandResult: true }, async (state) => {
    await writeClipboardText('fallback after rejection')
    assert.equal(state.copied, 1)
    assert.equal(state.removed, 1)
  })
})

test('传统复制返回 false 时报告失败且仍清理临时文本框', async () => {
  await withBrowserEnvironment({ clipboard: undefined, execCommandResult: false }, async (state) => {
    await assert.rejects(writeClipboardText('cannot copy'), /请改用 HTTPS 访问后重试/)
    assert.equal(state.copied, 1)
    assert.equal(state.removed, 1)
  })
})

test('所有复制入口统一使用兼容剪贴板工具', () => {
  for (const source of viewSources) {
    assert.match(source, /import \{ writeClipboardText \} from '@\/utils\/clipboard'/)
    assert.doesNotMatch(source, /navigator\.clipboard\.writeText/)
  }
  assert.equal(viewSources.reduce((count, source) => count + (source.match(/await writeClipboardText\(/g)?.length ?? 0), 0), 4)
})

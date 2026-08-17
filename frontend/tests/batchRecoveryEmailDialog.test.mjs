import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../src/components/BatchRecoveryEmailDialog.vue', import.meta.url), 'utf8')

test('批量换绑邮箱按账号逐个提交，避免一个长连接处理全部账号', () => {
  assert.match(source, /async function runAccountsSequentially\(basePayload: BatchChangeRecoveryEmailRequest\)/)
  assert.match(source, /for \(let index = 0; index < ids\.length; index \+= 1\)/)
  assert.match(source, /progressText\.value = `正在处理 \$\{index \+ 1\}\/\$\{ids\.length\}：账号 #\$\{accountId\}`/)
  assert.match(source, /panelApi\.batchChangeRecoveryEmail\(\{\s*\.\.\.basePayload,\s*accountIds: \[accountId\],\s*\}\)/s)
  assert.doesNotMatch(source, /accountIds:\s*accountIds\.value/)
})

test('批量换绑邮箱单账号请求中断后停止剩余账号，避免后端幽灵任务叠加', () => {
  assert.match(source, /catch \(error\) \{/)
  assert.match(source, /summary: '请求中断'/)
  assert.match(source, /单账号请求失败：\$\{message\}/)
  assert.match(source, /for \(const remainingId of ids\.slice\(index \+ 1\)\)/)
  assert.match(source, /summary: '未执行'/)
  assert.match(source, /避免重复并发操作/)
  assert.match(source, /break/)
})

test('批量换绑邮箱运行期间禁用关闭和重复提交', () => {
  assert.match(source, /<el-alert\s+v-if="running"[\s\S]*:title="progressText"/)
  assert.match(source, /<el-button :disabled="running" @click="visible = false">关闭<\/el-button>/)
  assert.match(source, /:loading="running" :disabled="running" @click="submit"/)
})

test('批量换绑邮箱先显示弹窗，再异步读取 Cloud Mail 默认配置', () => {
  assert.match(source, /visible\.value = true\s+void loadDefaults\(\)/)
  assert.doesNotMatch(source, /await loadDefaults\(\)\s+visible\.value = true/)
})

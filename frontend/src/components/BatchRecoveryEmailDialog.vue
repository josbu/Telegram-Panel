<template>
  <el-dialog v-model="visible" title="批量换绑邮箱（找回+登录）" width="680px" @closed="resetRuntime">
    <el-alert
      title="将按手机号生成邮箱，使用 Cloud Mail 收取 Telegram 邮件验证码，批量完成找回邮箱绑定/换绑；可选同时变更登录邮箱。"
      type="info"
      :closable="false"
      show-icon
      class="mb-3"
    />

    <el-form label-position="top">
      <div class="form-grid">
        <el-form-item label="Cloud Mail URL">
          <el-input v-model="form.cloudMailBaseUrl" />
        </el-form-item>
        <el-form-item label="邮箱域名">
          <el-input v-model="form.domain" placeholder="example.com" />
        </el-form-item>
      </div>

      <el-form-item label="Authorization Token">
        <el-input v-model="form.cloudMailToken" type="password" show-password />
      </el-form-item>

      <div class="switch-list">
        <el-checkbox v-model="form.changeLoginEmail">同时修改登录邮箱（用于接收登录确认邮件）</el-checkbox>
        <el-checkbox v-if="form.changeLoginEmail" v-model="form.trySetLoginEmailWhenMissing">
          无登录邮箱时也尝试设置（可能失败）
        </el-checkbox>
        <el-checkbox v-model="form.useStoredPasswords">优先使用数据库中保存的原二级密码</el-checkbox>
      </div>

      <el-form-item :label="form.useStoredPasswords ? '原二级密码（统一兜底）' : '原二级密码（统一）'">
        <el-input
          v-model="form.currentPassword"
          type="password"
          show-password
          :placeholder="form.useStoredPasswords ? '账号未保存二级密码时使用；留空则该账号失败' : '所有账号统一使用此密码'"
        />
      </el-form-item>

      <el-checkbox v-model="form.autoConfirm">自动从邮箱取码并确认绑定</el-checkbox>
      <div v-if="form.autoConfirm" class="form-grid mt-3">
        <el-form-item label="收码轮询间隔（秒）">
          <el-input-number v-model="form.pollIntervalSeconds" :min="2" :max="30" />
        </el-form-item>
        <el-form-item label="收码超时（秒）">
          <el-input-number v-model="form.pollTimeoutSeconds" :min="10" :max="300" />
        </el-form-item>
        <el-form-item label="发件人过滤（支持 %）">
          <el-input v-model="form.sendEmailFilter" placeholder="留空不过滤，例如 %telegram%" />
        </el-form-item>
        <el-form-item label="主题过滤（支持 %）">
          <el-input v-model="form.subjectFilter" placeholder="留空不过滤，例如 %Telegram%" />
        </el-form-item>
      </div>

      <el-alert
        v-if="running || completed"
        class="mt-3"
        :type="failures.length > 0 ? 'warning' : 'success'"
        :closable="false"
        show-icon
        :title="progressText"
      />
      <div v-if="failures.length > 0" class="failure-list mt-3">
        <div class="failure-list-title">失败账号（实时）</div>
        <el-scrollbar max-height="190px">
          <div v-for="failure in failures" :key="`${failure.accountId}-${failure.summary}-${failure.error || ''}`" class="failure-item">
            <div class="failure-item-title">账号 #{{ failure.accountId }} {{ failure.phone || '' }} · {{ failure.summary }}</div>
            <div class="failure-item-error">{{ failure.error || '未提供失败原因' }}</div>
          </div>
        </el-scrollbar>
      </div>
      <el-alert
        class="mt-3"
        type="warning"
        :closable="false"
        show-icon
        :title="`已选账号：${accountIds.length}；邮箱格式：{手机号数字}@${form.domain || '域名'}`"
      />
    </el-form>

    <template #footer>
      <el-button :disabled="running" @click="visible = false">关闭</el-button>
      <el-button type="primary" :loading="running" :disabled="running || completed" @click="submit">开始批量换绑</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { AccountBatchOperationResult, AccountOperationItem, BatchChangeRecoveryEmailRequest } from '@/api/types'

const emit = defineEmits<{
  completed: [result: AccountBatchOperationResult]
}>()

const visible = ref(false)
const running = ref(false)
const accountIds = ref<number[]>([])
const progressText = ref('')
const completed = ref(false)
const failures = ref<AccountOperationItem[]>([])

const form = reactive({
  cloudMailBaseUrl: '',
  cloudMailToken: '',
  domain: '',
  changeLoginEmail: true,
  trySetLoginEmailWhenMissing: false,
  useStoredPasswords: true,
  currentPassword: '',
  autoConfirm: true,
  pollIntervalSeconds: 3,
  pollTimeoutSeconds: 60,
  sendEmailFilter: '',
  subjectFilter: '',
})

async function open(ids: number[]) {
  accountIds.value = [...new Set(ids.filter((id) => id > 0))]
  if (accountIds.value.length === 0) {
    ElMessage.info('请先选择账号')
    return
  }

  completed.value = false
  failures.value = []
  progressText.value = ''
  visible.value = true
  void loadDefaults()
}

async function loadDefaults() {
  try {
    const settings = await panelApi.settings()
    form.cloudMailBaseUrl = settings.cloudMail.baseUrl || form.cloudMailBaseUrl
    form.cloudMailToken = settings.cloudMail.token || form.cloudMailToken
    form.domain = (settings.cloudMail.domain || form.domain).replace(/^@+/, '')
  } catch {
    // 设置读取失败时仍允许手动填写。
  }
}

function resetRuntime() {
  running.value = false
  completed.value = false
  failures.value = []
  progressText.value = ''
}

async function submit() {
  if (accountIds.value.length === 0) {
    ElMessage.info('请先选择账号')
    return
  }
  if (!form.cloudMailBaseUrl.trim() || !form.cloudMailToken.trim()) {
    ElMessage.warning('请填写 Cloud Mail URL 和 Token')
    return
  }
  if (!form.domain.trim()) {
    ElMessage.warning('请填写邮箱域名')
    return
  }
  if (!form.useStoredPasswords && !form.currentPassword.trim()) {
    ElMessage.warning('请填写统一原二级密码')
    return
  }

  await ElMessageBox.confirm(
    `将对 ${accountIds.value.length} 个账号批量换绑找回邮箱${form.changeLoginEmail ? '，并同时修改登录邮箱' : ''}。邮箱格式为：{手机号数字}@${form.domain.replace(/^@+/, '')}。是否继续？`,
    '确认批量换绑',
    { type: 'warning', confirmButtonText: '继续', cancelButtonText: '取消' },
  )

  running.value = true
  completed.value = false
  progressText.value = `准备处理 ${accountIds.value.length} 个账号`
  try {
    const result = await runAccountsSequentially({
      accountIds: [],
      cloudMailBaseUrl: form.cloudMailBaseUrl,
      cloudMailToken: form.cloudMailToken,
      domain: form.domain.replace(/^@+/, ''),
      changeLoginEmail: form.changeLoginEmail,
      trySetLoginEmailWhenMissing: form.trySetLoginEmailWhenMissing,
      useStoredPasswords: form.useStoredPasswords,
      currentPassword: form.currentPassword || null,
      autoConfirm: form.autoConfirm,
      pollIntervalSeconds: form.pollIntervalSeconds,
      pollTimeoutSeconds: form.pollTimeoutSeconds,
      sendEmailFilter: form.sendEmailFilter || null,
      subjectFilter: form.subjectFilter || null,
    })
    completed.value = true
    progressText.value = result.failed > 0
      ? `处理完成：成功 ${result.success} 个，失败 ${result.failed} 个`
      : `处理完成：${result.success} 个账号全部成功`
    emit('completed', result)
  } finally {
    running.value = false
  }
}

async function runAccountsSequentially(basePayload: BatchChangeRecoveryEmailRequest): Promise<AccountBatchOperationResult> {
  const ids = [...accountIds.value]
  const items: AccountOperationItem[] = []

  const recordItems = (nextItems: AccountOperationItem[]) => {
    items.push(...nextItems)
    failures.value.push(...nextItems.filter((item) => !item.success))
  }

  for (let index = 0; index < ids.length; index += 1) {
    const accountId = ids[index]
    progressText.value = `正在处理 ${index + 1}/${ids.length}：账号 #${accountId}；失败 ${failures.value.length}`

    try {
      const result = await panelApi.batchChangeRecoveryEmail({
        ...basePayload,
        accountIds: [accountId],
      })
      recordItems(result.items.length > 0 ? result.items : [{
        accountId,
        phone: `#${accountId}`,
        success: result.failed === 0,
        summary: result.failed === 0 ? '已处理' : '处理失败',
        error: result.failed === 0 ? null : '接口未返回账号明细',
      }])
    } catch (error) {
      const message = toRequestErrorMessage(error)
      const interruptedItems: AccountOperationItem[] = [{
        accountId,
        phone: `#${accountId}`,
        success: false,
        summary: '请求中断',
        error: `单账号请求失败：${message}`,
      }]

      for (const remainingId of ids.slice(index + 1)) {
        interruptedItems.push({
          accountId: remainingId,
          phone: `#${remainingId}`,
          success: false,
          summary: '未执行',
          error: '已停止：前一个账号请求中断，避免重复并发操作',
        })
      }
      recordItems(interruptedItems)
      progressText.value = `处理在账号 #${accountId} 处中断；失败 ${failures.value.length}`
      break
    }
  }

  return {
    success: items.filter((item) => item.success).length,
    failed: items.filter((item) => !item.success).length,
    items,
  }
}


function toRequestErrorMessage(error: unknown) {
  if (error instanceof Error && error.message) return error.message
  return 'Network Error'
}

defineExpose({ open })
</script>

<style scoped>
.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.switch-list {
  display: grid;
  gap: 8px;
  margin-bottom: 12px;
}
.failure-list {
  border: 1px solid var(--el-color-warning-light-5);
  border-radius: 6px;
  padding: 10px 12px;
  background: var(--el-color-warning-light-9);
}

.failure-list-title {
  margin-bottom: 8px;
  font-weight: 600;
  color: var(--el-color-warning-dark-2);
}

.failure-item + .failure-item {
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--el-border-color-lighter);
}

.failure-item-title {
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.failure-item-error {
  margin-top: 4px;
  color: var(--el-text-color-regular);
  line-height: 1.5;
  word-break: break-word;
}

@media (max-width: 720px) {
  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>

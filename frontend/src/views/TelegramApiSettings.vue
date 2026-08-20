<template>
  <div>
    <el-alert
      class="mb-4"
      type="info"
      :closable="false"
      show-icon
      :title="`写入位置：${settings?.localConfigPath || '-'}`"
    />

    <div class="settings-columns">
      <div class="settings-column">
        <el-card shadow="never" class="page-card">
          <template #header>Telegram API 配置</template>
          <el-form label-position="top">
            <el-form-item label="自定义默认 API ID（可选）">
              <el-input v-model="telegram.apiId" :placeholder="`留空使用内置官方 Android API ${officialApiId}`" />
              <div class="muted mt-2">只有未启用 API 配置池时才使用；留空表示回退到内置官方 API。</div>
            </el-form-item>
            <el-form-item label="自定义默认 API Hash（可选）">
              <el-input v-model="telegram.apiHash" type="password" show-password placeholder="留空使用内置官方 API Hash" />
              <div class="muted mt-2">不需要手工填写官方 Hash；如需自建 API 或其他客户端 API 再填写。</div>
            </el-form-item>
            <el-alert
              class="official-api-alert mb-3"
              :type="isBuiltInOfficialActive ? 'success' : 'info'"
              :closable="false"
              show-icon
            >
              <template #title>内置官方 Android API：ApiId {{ officialApiId }}{{ isBuiltInOfficialActive ? '（当前生效）' : '（可回退）' }}</template>
              <div>ApiHash 已内置，默认不会写入 appsettings.local.json；只有自定义默认 API 和启用的 API 配置池都为空时使用。</div>
              <div>当前来源：{{ effectiveApiSourceLabel }}</div>
            </el-alert>
            <el-divider />
            <div class="section-title">API 配置池</div>
            <el-alert type="info" :closable="false" show-icon class="mb-3">
              <template #title>新账号登录、Session 文件、StringSession 和 TData 导入会在启用的配置间按已保存账号数均衡分配；Zip 内自带 api_id/api_hash 的账号保持包内配置。</template>
            </el-alert>
            <div v-for="(profile, index) in telegram.profiles" :key="index" class="api-profile-row">
              <el-row :gutter="8">
                <el-col :xs="24" :sm="6">
                  <el-form-item label="名称">
                    <el-input v-model="profile.name" placeholder="主 API" />
                  </el-form-item>
                </el-col>
                <el-col :xs="24" :sm="5">
                  <el-form-item label="ApiId">
                    <el-input v-model="profile.apiId" />
                  </el-form-item>
                </el-col>
                <el-col :xs="24" :sm="6">
                  <el-form-item label="ApiHash">
                    <el-input v-model="profile.apiHash" />
                  </el-form-item>
                </el-col>
                <el-col :xs="12" :sm="4">
                  <el-form-item label="权重">
                    <el-input-number v-model="profile.weight" :min="1" :max="1000" :controls="false" class="full" />
                  </el-form-item>
                </el-col>
                <el-col :xs="12" :sm="3">
                  <el-form-item label="启用">
                    <el-switch v-model="profile.enabled" />
                  </el-form-item>
                </el-col>
              </el-row>
              <el-form-item label="备注">
                <el-input v-model="profile.notes" placeholder="可选" />
              </el-form-item>
              <el-button text type="danger" @click="removeApiProfile(index)">删除此配置</el-button>
            </div>
            <el-button plain @click="addApiProfile">添加 API 配置</el-button>
          </el-form>
          <el-alert :type="settings?.telegram.hasUsableApi === false ? 'warning' : 'info'" :closable="false" show-icon class="mb-3">
            <template #title>Telegram API 状态</template>
            <div>写入位置：{{ settings?.localConfigPath || '-' }}</div>
            <div>文件存在：{{ settings?.localConfigExists ? '是' : '否' }}</div>
            <div>当前来源：{{ effectiveApiSourceLabel }}</div>
            <div>当前生效 ApiId：{{ effectiveApiId || '（不可用）' }}</div>
          </el-alert>
          <el-button type="primary" :loading="saving.telegram" @click="saveTelegram">保存配置</el-button>
        </el-card>
      </div>

      <div class="settings-column">
        <el-card shadow="never" class="page-card">
          <template #header>内置官方 API 说明</template>
          <el-alert type="info" :closable="false" show-icon class="mb-3">
            <template #title>内置 API 是运行时回退，不是 appsettings.local.json 中的一条配置。</template>
            <div>{{ officialApiName }}：ApiId {{ officialApiId }}；ApiHash 已内置，不需要手工填写。</div>
            <div>如果当前来源不是内置，说明仍有自定义默认 API 或启用的 API 配置池在覆盖它。</div>
          </el-alert>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item label="内置官方 ApiId">{{ officialApiId }}</el-descriptions-item>
            <el-descriptions-item label="当前来源">{{ effectiveApiSourceLabel }}</el-descriptions-item>
            <el-descriptions-item label="当前生效 ApiId">{{ effectiveApiId || '（不可用）' }}</el-descriptions-item>
            <el-descriptions-item label="启用中的 API 配置">{{ enabledApiProfiles.length }}</el-descriptions-item>
          </el-descriptions>
          <div class="button-row mt-3">
            <el-button plain @click="useBuiltInOfficialApiFallback">改用内置官方 API</el-button>
          </div>
        </el-card>

      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { SettingsPayload, TelegramApiSettings, TelegramApiProfile } from '@/api/types'

const fallbackOfficialApiId = '6'
const fallbackOfficialApiName = 'Telegram 官方 Android API'
const settings = ref<SettingsPayload | null>(null)
const telegram = reactive({
  apiId: '',
  apiHash: '',
  profiles: [] as TelegramApiProfile[],
})
const saving = reactive({
  telegram: false,
})

const officialApiId = computed(() => settings.value?.telegram.officialApiId || fallbackOfficialApiId)
const officialApiName = computed(() => settings.value?.telegram.officialApiName || fallbackOfficialApiName)
const enabledApiProfiles = computed(() => telegram.profiles.filter((profile) => profile.enabled !== false))
const effectiveApiId = computed(() => settings.value?.telegram.effectiveApiId || settings.value?.system.effectiveApiId || '')
const effectiveApiSourceLabel = computed(() => {
  const source = settings.value?.telegram.effectiveApiSource
  if (source === 'built_in_official') return '内置官方 Android API'
  if (source === 'api_profile') return settings.value?.telegram.effectiveApiName || 'API 配置池'
  if (source === 'custom_default') return '自定义默认 API'
  if (source === 'invalid') return '配置不可用'
  return settings.value ? '未配置' : '加载中'
})
const isBuiltInOfficialActive = computed(() => settings.value?.telegram.effectiveApiSource === 'built_in_official')

function normalizeTelegramSettings(source: TelegramApiSettings) {
  telegram.apiId = !source.apiId || source.apiId === '0' ? '' : source.apiId
  telegram.apiHash = source.apiHash || ''
  const profiles = source.profiles || []
  telegram.profiles = profiles.map((profile) => ({
    name: profile.name || '',
    apiId: profile.apiId || '',
    apiHash: profile.apiHash || '',
    enabled: profile.enabled !== false,
    weight: profile.weight || 1,
    notes: profile.notes || '',
  }))
}

async function load() {
  const data = await panelApi.settings()
  settings.value = data
  normalizeTelegramSettings(data.telegram)
}

async function saveTelegram() {
  saving.telegram = true
  try {
    const current = await panelApi.settings()
    const result = await panelApi.saveTelegramApiSettings({
      apiId: telegram.apiId,
      apiHash: telegram.apiHash,
      profiles: telegram.profiles,
      deviceProfiles: current.telegram.deviceProfiles || [],
      defaultDeviceProfileKey: current.telegram.defaultDeviceProfileKey || null,
    })
    if (result.message) ElMessage.success(result.message)
    await load()
  } finally {
    saving.telegram = false
  }
}

function hasSavableProfile(profile: TelegramApiProfile) {
  return !!(profile.apiId?.trim() && profile.apiHash?.trim())
}

function useBuiltInOfficialApiFallback() {
  const enabledCount = telegram.profiles.filter((profile) => profile.enabled !== false).length
  telegram.apiId = ''
  telegram.apiHash = ''
  telegram.profiles = telegram.profiles
    .filter(hasSavableProfile)
    .map((profile) => ({ ...profile, enabled: false }))
  ElMessage.info(enabledCount > 0
    ? '已清空自定义默认 API 并停用 API 配置池；点击保存配置后使用内置官方 API'
    : '已清空自定义默认 API；点击保存配置后使用内置官方 API')
}

function addApiProfile() {
  telegram.profiles.push({ name: '', apiId: '', apiHash: '', enabled: true, weight: 1, notes: '' })
}

function removeApiProfile(index: number) {
  telegram.profiles.splice(index, 1)
}

onMounted(load)
</script>

<style scoped>
.settings-columns {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.settings-column {
  display: grid;
  gap: 16px;
  align-content: start;
}

.button-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.full {
  width: 100%;
}

.mb-4 {
  margin-bottom: 16px;
}

.mt-2 {
  margin-top: 8px;
}

.mt-3 {
  margin-top: 12px;
}

.section-title {
  font-weight: 600;
  margin-bottom: 8px;
}

.api-profile-row {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 12px;
}

.official-api-alert {
  margin-top: 4px;
}

@media (max-width: 960px) {
  .settings-columns {
    grid-template-columns: 1fr;
  }
}
</style>

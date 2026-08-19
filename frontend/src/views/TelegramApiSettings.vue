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
            <el-form-item label="默认 API ID">
              <el-input v-model="telegram.apiId" />
              <div class="muted mt-2">Telegram 官方 API 的默认 ApiId；未启用 API 配置池时，新账号登录和导入将使用这里的 ApiId / ApiHash。</div>
            </el-form-item>
            <el-form-item label="默认 API Hash">
              <el-input v-model="telegram.apiHash" />
              <div class="muted mt-2">兼容旧配置；留空时会回退到启用的 API 配置池或账号已有的 ApiId / ApiHash。</div>
            </el-form-item>
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
          <el-alert type="info" :closable="false" show-icon class="mb-3">
            <template #title>Telegram API 状态</template>
            <div>写入位置：{{ settings?.localConfigPath || '-' }}</div>
            <div>文件存在：{{ settings?.localConfigExists ? '是' : '否' }}</div>
            <div>当前生效 ApiId：{{ settings?.system.effectiveApiId || '（未配置）' }}</div>
          </el-alert>
          <el-button type="primary" :loading="saving.telegram" @click="saveTelegram">保存配置</el-button>
        </el-card>
      </div>

      <div class="settings-column">
        <el-card shadow="never" class="page-card">
          <template #header>官方 API 说明</template>
          <el-alert type="info" :closable="false" show-icon class="mb-3">
            <template #title>未配置自定义 API 时，系统默认使用 Telegram 官方 Android API。</template>
            <div>官方默认：ApiId {{ officialApiId }}；ApiHash 已内置，不需要手工填写。</div>
            <div>如需其他官方客户端或自建 API，请在上方填写并保存；已有账号仍优先使用账号保存的 ApiId / ApiHash。</div>
          </el-alert>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item label="官方默认 ApiId">{{ officialApiId }}</el-descriptions-item>
            <el-descriptions-item label="当前默认 ApiId">{{ telegram.apiId || officialApiId }}</el-descriptions-item>
            <el-descriptions-item label="启用中的 API 配置">{{ telegram.profiles.filter((profile) => profile.enabled !== false).length }}</el-descriptions-item>
          </el-descriptions>
          <div class="button-row mt-3">
            <el-button plain @click="useOfficialApi">恢复官方默认</el-button>
          </div>
        </el-card>

      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { SettingsPayload, TelegramApiSettings, TelegramApiProfile } from '@/api/types'

const officialApiId = '6'
const officialApiHash = 'eb06d4abfb49dc3eeb1aeb98ae0f581e'
const settings = ref<SettingsPayload | null>(null)
const telegram = reactive({
  apiId: '',
  apiHash: '',
  profiles: [] as TelegramApiProfile[],
})
const saving = reactive({
  telegram: false,
})

function normalizeTelegramSettings(source: TelegramApiSettings) {
  const hasProfiles = (source.profiles?.some((profile) => profile.enabled !== false) || false)
  telegram.apiId = !source.apiId || source.apiId === '0' ? (hasProfiles ? '' : officialApiId) : source.apiId
  telegram.apiHash = source.apiHash || (hasProfiles ? '' : officialApiHash)
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

function useOfficialApi() {
  telegram.apiId = officialApiId
  telegram.apiHash = officialApiHash
  ElMessage.info('已恢复官方默认 API；点击保存配置后生效')
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

@media (max-width: 960px) {
  .settings-columns {
    grid-template-columns: 1fr;
  }
}
</style>

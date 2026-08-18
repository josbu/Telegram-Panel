<template>
  <router-view />

  <div v-if="errorMessage" class="fatal-overlay">
    <div class="fatal-card">
      <div class="fatal-title">页面加载失败</div>
      <div class="fatal-message">{{ errorMessage }}</div>
      <div class="fatal-actions">
        <el-button type="primary" @click="reload">刷新页面</el-button>
        <el-button @click="clear">关闭提示</el-button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { isBenignRuntimeError, isFatalPromiseError, isHttpClientError, toRuntimeErrorMessage } from '@/utils/runtimeErrors'

const errorMessage = ref('')

function onError(event: Event) {
  const custom = event as CustomEvent<unknown>
  if (isHttpClientError(custom.detail) || isBenignRuntimeError(custom.detail)) return
  errorMessage.value = toRuntimeErrorMessage(custom.detail)
}

function onUnhandledRejection(event: PromiseRejectionEvent) {
  if (!isFatalPromiseError(event.reason)) return
  errorMessage.value = toRuntimeErrorMessage(event.reason)
}

function reload() {
  window.location.reload()
}

function clear() {
  errorMessage.value = ''
}

onMounted(() => {
  window.addEventListener('telegram-panel:error', onError)
  window.addEventListener('unhandledrejection', onUnhandledRejection)
})

onUnmounted(() => {
  window.removeEventListener('telegram-panel:error', onError)
  window.removeEventListener('unhandledrejection', onUnhandledRejection)
})
</script>

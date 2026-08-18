<template>
  <el-dropdown trigger="click" :hide-on-click="false" :disabled="disabled">
    <el-button :icon="Grid">
      显示列<el-icon class="el-icon--right"><ArrowDown /></el-icon>
    </el-button>
    <template #dropdown>
      <el-dropdown-menu>
        <div class="column-menu" @click.stop>
          <div class="column-menu-actions">
            <el-button link type="primary" @click="emit('show-all')">全选</el-button>
            <el-button link type="primary" @click="emit('reset')">恢复默认</el-button>
          </div>
          <el-checkbox-group v-model="selectedKeys" class="column-menu-list">
            <el-checkbox v-for="column in columns" :key="column.key" :value="column.key">
              {{ column.label }}
            </el-checkbox>
          </el-checkbox-group>
        </div>
      </el-dropdown-menu>
    </template>
  </el-dropdown>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { ArrowDown, Grid } from '@element-plus/icons-vue'
import type { ColumnVisibilityOption } from '@/utils/columnVisibility'

const props = defineProps<{
  columns: ColumnVisibilityOption[]
  modelValue: string[]
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string[]]
  reset: []
  'show-all': []
}>()

const selectedKeys = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value),
})
</script>

<style scoped>
.column-menu {
  width: 180px;
  padding: 8px 12px 10px;
}

.column-menu-actions {
  display: flex;
  justify-content: space-between;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--tp-border);
}

.column-menu-list {
  display: grid;
  gap: 4px;
  padding-top: 8px;
}

.column-menu-list :deep(.el-checkbox) {
  height: 24px;
  margin-right: 0;
}
</style>

<template>
  <div class="category-page">
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header category-header">
          <div>
            <div class="card-title">账号批量改分类</div>
            <div class="muted">和账号列表一致：先筛选/多选账号，再在顶部操作栏批量改到目标分类。</div>
          </div>
          <el-button :icon="Refresh" :loading="accountsLoading || loading" @click="loadAll">刷新</el-button>
        </div>
      </template>

      <div class="toolbar category-account-toolbar">
        <el-select v-model="filterCategoryId" class="filter" placeholder="全部分类" @change="clearAccountSelection">
          <el-option label="全部分类" :value="-1" />
          <el-option label="未分类" :value="0" />
          <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
        </el-select>
        <el-input
          v-model="accountSearch"
          class="search"
          placeholder="搜索账号编号、手机号、昵称、用户名..."
          clearable
          :prefix-icon="Search"
          @clear="clearAccountSelection"
          @input="clearAccountSelection"
        />
        <el-select v-model="batchCategoryId" class="filter" placeholder="目标分类">
          <el-option label="改为未分类" :value="0" />
          <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
        </el-select>
        <el-button :icon="Select" :disabled="accountsLoading || filteredAccounts.length === 0" @click="selectFilteredAccounts">
          全选当前筛选
        </el-button>
        <el-button :disabled="selectedAccountIds.length === 0" @click="clearAccountSelection">清空选择</el-button>
        <el-button
          type="primary"
          :icon="Select"
          :loading="savingBatchCategory"
          :disabled="selectedAccountIds.length === 0 || batchCategoryId === undefined"
          @click="applyBatchCategory"
        >
          批量修改分类（已选）
        </el-button>
        <el-tag v-if="selectedAccountIds.length > 0" type="info">已选 {{ selectedAccountIds.length }}</el-tag>
        <span v-else class="muted">账号 {{ filteredAccounts.length }} / {{ accounts.length }}</span>
      </div>

      <el-table
        ref="accountTableRef"
        v-loading="accountsLoading"
        :data="filteredAccounts"
        row-key="id"
        stripe
        class="mt-4"
        @selection-change="onAccountSelectionChange"
      >
        <el-table-column type="selection" width="48" reserve-selection />
        <el-table-column prop="displayNumber" label="编号" width="96">
          <template #default="{ row }">#{{ row.displayNumber }}</template>
        </el-table-column>
        <el-table-column prop="displayPhone" label="手机号" min-width="150" />
        <el-table-column label="账号信息" min-width="220">
          <template #default="{ row }">
            <div>{{ row.nickname || row.remark || '-' }}</div>
            <div class="cell-sub">{{ row.username ? `@${row.username}` : '无用户名' }}</div>
          </template>
        </el-table-column>
        <el-table-column label="当前分类" min-width="150">
          <template #default="{ row }">
            <el-tag v-if="row.category" effect="plain" class="category-name-tag" :style="accountCategoryTagStyle(row.category)">
              {{ row.category.name }}
            </el-tag>
            <el-tag v-else type="info" effect="plain">未分类</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'" size="small">{{ row.isActive ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <template #header>
        <div class="card-header category-header">
          <div>
            <div class="card-title">分类管理</div>
            <div class="muted">维护分类名称、描述和“是否排除在创建/批量任务之外”。</div>
          </div>
          <el-button :icon="Refresh" :loading="loading" @click="loadCategories">刷新分类</el-button>
        </div>
      </template>

      <el-form label-position="top" class="category-create-form">
        <el-form-item label="分类名称" class="category-name-field">
          <el-input v-model="createForm.name" placeholder="例如：AI广告" />
        </el-form-item>
        <el-form-item label="描述" class="category-description-field">
          <el-input v-model="createForm.description" type="textarea" :rows="2" placeholder="可选，说明此分类用途" />
        </el-form-item>
        <el-form-item label="操作排除" class="category-exclude-field">
          <el-checkbox v-model="createForm.excludeFromOperations">不出现在创建/批量任务中</el-checkbox>
        </el-form-item>
        <el-form-item class="category-submit-field">
          <el-button type="primary" class="full-btn" :icon="Plus" :disabled="!createForm.name.trim()" :loading="creating" @click="createCategory">
            添加分类
          </el-button>
        </el-form-item>
      </el-form>

      <el-table v-loading="loading" :data="categories" stripe class="mt-4">
        <el-table-column label="分类名称" min-width="150">
          <template #default="{ row }">
            <el-tag effect="plain" class="category-name-tag" :style="accountCategoryTagStyle(row)">
              {{ row.name }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="description" label="描述" min-width="180">
          <template #default="{ row }">{{ row.description || '-' }}</template>
        </el-table-column>
        <el-table-column label="排除操作" width="110">
          <template #default="{ row }">
            <el-tag v-if="row.excludeFromOperations" type="warning" size="small">是</el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="accountCount" label="账号数量" width="100" />
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" :icon="Edit" title="编辑" @click="openEdit(row)" />
            <el-button link type="danger" :icon="Delete" title="删除" @click="deleteCategory(row)" />
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="editDialog.visible" title="编辑分类" width="min(460px, calc(100vw - 24px))">
      <el-form label-position="top">
        <el-form-item label="分类名称">
          <el-input v-model="editDialog.form.name" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="editDialog.form.description" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="editDialog.form.excludeFromOperations">排除操作（不出现在创建/批量任务中）</el-checkbox>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialog.visible = false">取消</el-button>
        <el-button type="primary" :disabled="!editDialog.form.name.trim()" :loading="editDialog.saving" @click="saveEdit">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import type { TableInstance } from 'element-plus'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Plus, Refresh, Search, Select } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type { AccountCategory, AccountListItem } from '@/api/types'
import { accountCategoryTagStyle } from '@/utils/categoryStyle'

const categories = ref<AccountCategory[]>([])
const accounts = ref<AccountListItem[]>([])
const loading = ref(false)
const accountsLoading = ref(false)
const creating = ref(false)
const savingBatchCategory = ref(false)
const filterCategoryId = ref(-1)
const batchCategoryId = ref<number | undefined>(undefined)
const accountSearch = ref('')
const selectedAccountIds = ref<number[]>([])
const accountTableRef = ref<TableInstance>()

const createForm = reactive({
  name: '',
  description: '',
  excludeFromOperations: false,
})

const editDialog = reactive({
  visible: false,
  saving: false,
  id: 0,
  form: {
    name: '',
    description: '',
    excludeFromOperations: false,
  },
})

const filteredAccounts = computed(() => {
  const search = accountSearch.value.trim().toLowerCase()
  return accounts.value.filter((account) => {
    if (filterCategoryId.value === 0 && account.category) return false
    if (filterCategoryId.value > 0 && account.category?.id !== filterCategoryId.value) return false
    if (!search) return true
    return accountSearchText(account).includes(search)
  })
})

function accountSearchText(account: AccountListItem) {
  return [
    `#${account.displayNumber}`,
    String(account.displayNumber || ''),
    account.displayPhone,
    account.phone,
    account.nickname,
    account.username,
    account.remark,
    account.category?.name,
  ]
    .filter(Boolean)
    .join(' ')
    .toLowerCase()
}

function targetCategoryName() {
  if (batchCategoryId.value === 0) return '未分类'
  return categories.value.find((x) => x.id === batchCategoryId.value)?.name || ''
}

async function loadCategories() {
  loading.value = true
  try {
    categories.value = await panelApi.accountCategories()
  } finally {
    loading.value = false
  }
}

async function loadAccounts() {
  accountsLoading.value = true
  try {
    accounts.value = await loadAllAccounts()
  } finally {
    accountsLoading.value = false
  }
}

async function loadAllAccounts() {
  const pageSize = 500
  const items: AccountListItem[] = []
  let currentPage = 1
  let total = 0

  do {
    const data = await panelApi.accounts({
      page: currentPage,
      pageSize,
      categoryId: null,
      search: '',
      onlyWaste: false,
    })
    items.push(...data.items)
    total = data.total
    currentPage += 1
  } while (items.length < total)

  return items
}

async function loadAll() {
  await Promise.all([loadCategories(), loadAccounts()])
  clearAccountSelection()
}

async function createCategory() {
  creating.value = true
  try {
    const saved = await panelApi.createAccountCategory({
      name: createForm.name,
      color: null,
      description: createForm.description,
      excludeFromOperations: createForm.excludeFromOperations,
    })
    ElMessage.success(`分类 "${saved.name}" 添加成功`)
    createForm.name = ''
    createForm.description = ''
    createForm.excludeFromOperations = false
    await loadAll()
  } finally {
    creating.value = false
  }
}

function openEdit(category: AccountCategory) {
  editDialog.id = category.id
  editDialog.form.name = category.name
  editDialog.form.description = category.description || ''
  editDialog.form.excludeFromOperations = category.excludeFromOperations
  editDialog.visible = true
}

async function saveEdit() {
  editDialog.saving = true
  try {
    await panelApi.updateAccountCategory(editDialog.id, {
      name: editDialog.form.name,
      color: null,
      description: editDialog.form.description,
      excludeFromOperations: editDialog.form.excludeFromOperations,
    })
    ElMessage.success('分类已更新')
    editDialog.visible = false
    await loadAll()
  } finally {
    editDialog.saving = false
  }
}

async function deleteCategory(category: AccountCategory) {
  await ElMessageBox.confirm(`确定要删除分类 ${category.name} 吗？关联的账号将变为未分类。`, '确认删除', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  await panelApi.deleteAccountCategory(category.id)
  if (filterCategoryId.value === category.id) filterCategoryId.value = -1
  if (batchCategoryId.value === category.id) batchCategoryId.value = undefined
  ElMessage.success('删除成功')
  await loadAll()
}

function onAccountSelectionChange(selection: AccountListItem[]) {
  selectedAccountIds.value = selection.map((x) => x.id)
}

async function selectFilteredAccounts() {
  await nextTick()
  accountTableRef.value?.clearSelection()
  filteredAccounts.value.forEach((account) => accountTableRef.value?.toggleRowSelection(account, true))
}

function clearAccountSelection() {
  accountTableRef.value?.clearSelection()
  selectedAccountIds.value = []
}

async function applyBatchCategory() {
  if (selectedAccountIds.value.length === 0) {
    ElMessage.warning('请先选择账号')
    return
  }
  if (batchCategoryId.value === undefined) {
    ElMessage.warning('请选择目标分类')
    return
  }

  const target = targetCategoryName()
  await ElMessageBox.confirm(
    `将把已选 ${selectedAccountIds.value.length} 个账号修改为「${target}」。是否继续？`,
    '确认批量修改分类',
    { type: 'warning', confirmButtonText: '修改分类', cancelButtonText: '取消' },
  )

  savingBatchCategory.value = true
  try {
    const categoryId = batchCategoryId.value > 0 ? batchCategoryId.value : null
    await panelApi.batchSetAccountCategory(selectedAccountIds.value, categoryId)
    ElMessage.success(`分类已更新：${selectedAccountIds.value.length} 个账号`)
    await loadAll()
  } finally {
    savingBatchCategory.value = false
  }
}

watch([filterCategoryId, accountSearch], () => {
  clearAccountSelection()
})

onMounted(loadAll)
</script>

<style scoped>
.category-page {
  width: min(100%, 1536px);
  margin: 0 auto;
}

.category-page .page-card {
  width: 100%;
  margin-left: 0;
  margin-right: 0;
}

.category-header {
  align-items: flex-start;
  gap: 12px;
}

.card-title {
  font-weight: 600;
  line-height: 1.5;
}

.category-create-form {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) minmax(320px, 1.45fr) minmax(220px, 0.9fr) minmax(120px, auto);
  gap: 12px;
  align-items: end;
}

.category-create-form :deep(.el-form-item) {
  margin-bottom: 0;
}

.category-submit-field :deep(.el-form-item__content) {
  align-items: end;
}

.category-account-toolbar .filter {
  width: 220px;
}

.category-account-toolbar .search {
  width: min(360px, 100%);
}

.full-btn {
  width: 100%;
}

.category-name-tag {
  border-radius: 999px;
}

@media (max-width: 1180px) {
  .category-create-form {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  }
}

@media (max-width: 720px) {
  .category-create-form {
    grid-template-columns: 1fr;
  }

  .category-account-toolbar .filter,
  .category-account-toolbar .search,
  .category-account-toolbar .el-button {
    width: 100%;
  }
}
</style>

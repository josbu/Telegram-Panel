import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../src/views/AccountCategories.vue', import.meta.url), 'utf8')

test('账号分类页顶部提供账号筛选和批量修改分类操作栏', () => {
  assert.match(source, /账号批量改分类/)
  assert.match(source, /class="toolbar category-account-toolbar"/)
  assert.match(source, /v-model="filterCategoryId"/)
  assert.match(source, /v-model="accountSearch"/)
  assert.match(source, /v-model="batchCategoryId"/)
  assert.match(source, /全选当前筛选/)
  assert.match(source, /批量修改分类（已选）/)
  assert.match(source, /已选 \{\{ selectedAccountIds\.length \}\}/)
})

test('账号分类批量操作使用账号列表同一批量分类接口', () => {
  assert.match(source, /type="selection"/)
  assert.match(source, /@selection-change="onAccountSelectionChange"/)
  assert.match(source, /async function applyBatchCategory\(\)/)
  assert.match(source, /const categoryId = batchCategoryId\.value > 0 \? batchCategoryId\.value : null/)
  assert.match(source, /panelApi\.batchSetAccountCategory\(selectedAccountIds\.value, categoryId\)/)
  assert.match(source, /确认批量修改分类/)
  assert.doesNotMatch(source, /saveAccountCategoryAssignments|保存勾选到分类|分类绑定账号/)
})

test('账号分类页移动端操作栏和创建表单会纵向收缩', () => {
  assert.match(source, /@media \(max-width: 720px\)/)
  assert.match(source, /\.category-account-toolbar \.search/)
  assert.match(source, /\.category-account-toolbar \.el-button/)
  assert.match(source, /grid-template-columns: 1fr;/)
})

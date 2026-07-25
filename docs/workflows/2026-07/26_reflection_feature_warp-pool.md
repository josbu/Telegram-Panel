# 开发反思报告

**日期**：2026-07-26

**提交类型**：feature

## 1. 概述

账号导入不再按账号创建独立 WARP 容器，新增 `warp_pool` 导入策略，按现有 WARP
绑定数选择并冻结为已有代理；登录和账号管理仍保留显式创建独立 WARP 的能力。

## 2. 修改内容

- 移除 Vue 与 Razor 导入页面的“每账号独立 WARP”入口。
- 后端拒绝导入端旧 `warp_per_account` 参数，避免旧客户端绕过页面继续创建容器。
- 自动池只使用已启用、`DesiredEnabled=true`、`Status=active` 的现有 WARP；无候选或
  首连占用冲突时失败，不回退直连、不创建新容器。
- 更新 API、配置、安装、导入、代理管理和模块开发文档及回归测试。

## 3. 遇到的错误与根因

### 错误 1：默认 .NET 输出被锁定

运行标准 Release 构建时，已有 `TelegramPanel.Web` 进程锁定依赖 DLL，复制阶段重试后
失败。根因是验证命令与正在运行的服务共享输出目录，而不是代码编译错误。

**解决方案**：使用独立 `OutDir` 完成全解决方案构建与测试，保留标准命令失败证据，避免
停止用户正在运行的进程。

### 错误 2：候选 WARP 状态值假设错误

初版池化筛选使用了 `Status=running`，检查 `WarpContainerManager` 后确认持久化运行状态
实际为 `active`。这是由页面展示术语与数据库枚举值未对齐造成的。

**解决方案**：后端、前端和文档统一使用数据库 `active`，并补充两个现有 WARP 的均衡分配
测试。

## 4. 经验总结

- 对持久化状态过滤应先查实体写入路径，再决定 UI 文案和测试夹具值。
- 服务已运行时优先使用独立构建输出验证，减少对用户进程的干扰。
- 删除高资源功能必须同时收紧 API 合同，不能只删除页面按钮。

## 5. 测试与验证

- `pnpm --dir frontend run build`：通过。
- `pnpm --dir frontend test`：43/43 通过。
- `dotnet build TelegramPanel.sln -c Release --no-restore`：默认输出目录因进程锁定失败；
  使用独立输出目录构建成功，0 错误。
- 独立输出目录执行 `dotnet test TelegramPanel.sln -c Release --no-build`：317/317 通过。
- `mkdocs build --strict`：通过。

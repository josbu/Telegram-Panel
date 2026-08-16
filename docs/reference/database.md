# 数据库说明（简版）

默认使用 SQLite（Docker 下持久化到 `./docker-data/telegram-panel.db`）。

本页只列出核心表的“概念与用途”，避免把 README 写得太劝退；具体字段以 `src/TelegramPanel.Data/Migrations/` 为准。

## 核心表

- `Accounts`：账号信息、分类、最近状态检测结果缓存和用户可见账号编号 `DisplayNumber` 等

- `Channels`：频道信息（主要是账号创建的频道）与分组/展示字段
- `Groups`：群组信息（主要是账号创建的群组）
- `Bots` / `BotChannels`：机器人与其管理的频道（如果启用机器人管理）
- `BatchTasks`：批量任务（pending/running/completed/failed），`Name` 可保存计划任务触发后的用户可读名称
- `TaskLogs`：任务日志（用于任务中心展示与排障）


`DisplayNumber` 自 v1.31.57 起存在唯一索引。迁移会按既有 `Id` 顺序把旧账号回填为 `1..N`；
之后保存新账号时由 `AppDbContext` 在事务保存前分配当前最小可用正整数，因此删除账号后编号
允许复用。接口和任务配置仍以内部 `Id` 做关联，`DisplayNumber` 只用于后台展示、搜索和人工
填写账号范围。成功判据是迁移后 `Accounts.DisplayNumber` 全部大于 0 且唯一；失败时先检查
迁移日志和唯一索引冲突。回滚到旧版会删除该列，回滚前不要把外部自动化只绑定到显示编号。

## 常见问题

### Docker 下数据库/Session 在哪？

统一在 `./docker-data`：

- `./docker-data/telegram-panel.db`
- `./docker-data/sessions/`

### 为什么刷新页面任务还在跑？

批量任务由后台服务从数据库拉取并执行，前端只是提交任务与展示进度（见 `BatchTasks`/`TaskLogs`）。

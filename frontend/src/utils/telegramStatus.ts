const transientTelegramStatusMarkers = [
  '连接失败',
  '请求超时',
  '刷新失败',
]

const inconclusiveTelegramStatusMarkers = [
  '创建频道探测失败',
  '无法获取账号资料',
  '已取消',
  '触发限流',
  'Telegram API 配置无效',
  '账号触发 Telegram 风控',
]

export function isTransientTelegramStatus(summary?: string | null) {
  const normalized = (summary || '').trim()
  return normalized.length > 0
    && transientTelegramStatusMarkers.some((marker) => normalized.includes(marker))
}

export function isInconclusiveTelegramStatus(summary?: string | null) {
  const normalized = (summary || '').trim()
  return isTransientTelegramStatus(normalized)
    || inconclusiveTelegramStatusMarkers.some((marker) => normalized.includes(marker))
}

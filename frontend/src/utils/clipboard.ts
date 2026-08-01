const unsupportedMessage = '当前浏览器不支持自动复制，请改用 HTTPS 访问后重试'

/**
 * 写入系统剪贴板。非安全上下文（例如 HTTP）无法使用 Clipboard API，
 * 此时回退到浏览器传统的复制命令。
 */
export async function writeClipboardText(text: string): Promise<void> {
  const clipboard = globalThis.navigator?.clipboard
  if (clipboard?.writeText) {
    try {
      await clipboard.writeText(text)
      return
    } catch {
      // 权限被拒绝时，传统复制命令在部分浏览器中仍然可用。
    }
  }

  if (typeof document === 'undefined' || typeof document.execCommand !== 'function') {
    throw new Error(unsupportedMessage)
  }

  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', '')
  textarea.style.position = 'fixed'
  textarea.style.inset = '0 auto auto 0'
  textarea.style.opacity = '0'
  textarea.style.pointerEvents = 'none'
  document.body.appendChild(textarea)

  try {
    textarea.focus()
    textarea.select()
    textarea.setSelectionRange(0, textarea.value.length)
    if (!document.execCommand('copy')) {
      throw new Error(unsupportedMessage)
    }
  } finally {
    textarea.remove()
  }
}

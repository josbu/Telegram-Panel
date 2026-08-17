import { isNavigationFailure } from 'vue-router'

const FATAL_RUNTIME_ERROR_PATTERN = /Failed to fetch dynamically imported module|Importing a module script failed|Loading chunk|dynamically imported module|Script error|Cannot read properties|is not a function/i

export function toRuntimeErrorMessage(error: unknown) {
  if (error instanceof Error && error.message) return error.message
  if (typeof error === 'string' && error.trim()) return error
  return '前端运行时发生异常，请刷新页面重试。'
}

export function isHttpClientError(error: unknown) {
  if (!error || typeof error !== 'object') return false
  const value = error as Record<string, unknown>
  return value.isAxiosError === true || ('response' in value && 'config' in value)
}

export function isBenignRuntimeError(error: unknown) {
  if (!error) return false
  if (isNavigationFailure(error)) return true

  const message = toRuntimeErrorMessage(error).trim()
  if (/^(cancel|close|canceled|cancelled)$/i.test(message)) return true
  if (/^Navigation (aborted|cancelled|canceled|duplicated)/i.test(message)) return true
  if (/Avoided redundant navigation to current location/i.test(message)) return true

  if (error instanceof DOMException && error.name === 'AbortError') return true
  if (error instanceof Error && error.name === 'AbortError') return true

  return false
}

export function isFatalPromiseError(error: unknown) {
  if (isHttpClientError(error) || isBenignRuntimeError(error)) return false
  return FATAL_RUNTIME_ERROR_PATTERN.test(toRuntimeErrorMessage(error))
}

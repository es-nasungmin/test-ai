import { createApp } from 'vue'
import ReusableChatbot from './ReusableChatbot.vue'
import { API_BASE_URL } from '../../config'

function resolveTarget(target) {
  if (target == null) {
    const el = document.createElement('div')
    document.body.appendChild(el)
    return { el, created: true }
  }
  if (target instanceof Element) return target
  if (typeof target === 'string') {
    const el = document.querySelector(target)
    if (!el) throw new Error(`Chat widget mount target not found: ${target}`)
    return { el, created: false }
  }
  throw new Error('Chat widget mount target must be a DOM element or selector string.')
}

export function mountChatbotWidget(target, options = {}) {
  const resolved = resolveTarget(target)
  const container = resolved.el || resolved
  const isCreatedContainer = Boolean(resolved.created)

  const app = createApp(ReusableChatbot, {
    apiBaseUrl: options.apiBaseUrl || API_BASE_URL,
    role: options.role || 'user',
    title: options.title || 'AI Chat',
    defaultPlatform: options.defaultPlatform || '전체 플랫폼',
    showPlatformSelector: options.showPlatformSelector,
    fabLabel: options.fabLabel,
    accent: options.accent || '#0d6efd',
    inline: options.inline ?? false,
    initiallyOpen: options.initiallyOpen ?? false,
    fabRight: options.fabRight,
    fabBottom: options.fabBottom,
    popupRight: options.popupRight,
    popupBottom: options.popupBottom
  })

  app.mount(container)

  return {
    unmount() {
      app.unmount()
      if (isCreatedContainer && container.parentNode) {
        container.parentNode.removeChild(container)
      }
    }
  }
}

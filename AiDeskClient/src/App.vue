<script setup>
import { onBeforeUnmount, onMounted } from 'vue'
import ManagementPage from './views/ManagementPage.vue'

let adminWidget = null
let userWidget = null
let isDisposed = false

function scrollToTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

function loadChatWidgetScript() {
  if (typeof window === 'undefined') {
    return Promise.resolve(null)
  }

  if (typeof window.createCrmChatWidget === 'function') {
    return Promise.resolve(window.createCrmChatWidget)
  }

  return new Promise((resolve, reject) => {
    const existingScript = document.querySelector('script[data-crm-chat-widget="true"]')

    if (existingScript) {
      existingScript.addEventListener('load', () => resolve(window.createCrmChatWidget), { once: true })
      existingScript.addEventListener('error', () => reject(new Error('chat-widget.js 로드에 실패했습니다.')), { once: true })
      return
    }

    const script = document.createElement('script')
    script.src = '/chat-widget.js'
    script.async = true
    script.dataset.crmChatWidget = 'true'
    script.addEventListener('load', () => resolve(window.createCrmChatWidget), { once: true })
    script.addEventListener('error', () => reject(new Error('chat-widget.js 로드에 실패했습니다.')), { once: true })
    document.body.appendChild(script)
  })
}

function destroyWidgets() {
  adminWidget?.destroy?.()
  userWidget?.destroy?.()
  adminWidget = null
  userWidget = null
}

onMounted(async () => {
  const createWidget = await loadChatWidgetScript()
  if (isDisposed || typeof createWidget !== 'function') return

  destroyWidgets()

  adminWidget = createWidget({
    apiBaseUrl: 'http://localhost:8080/api',
    role: 'admin',
    title: '관리자 위젯',
    platformName: '테스트',
    buttonLabel: 'ADMIN',
    buttonRight: '20px',
    buttonBottom: '156px',
    popupRight: '20px',
    popupBottom: '224px',
    showPlatformSelector: true,
    defaultPlatform: '전체 플랫폼',
    initiallyOpen: false
  })

  userWidget = createWidget({
    apiBaseUrl: 'http://localhost:8080/api',
    role: 'user',
    title: '사용자 위젯',
    platformName: '테스트',
    buttonLabel: 'USER',
    buttonRight: '20px',
    buttonBottom: '88px',
    popupRight: '20px',
    popupBottom: '156px',
    showPlatformSelector: true,
    defaultPlatform: '전체 플랫폼',
    initiallyOpen: false
  })
})

onBeforeUnmount(() => {
  isDisposed = true
  destroyWidgets()
})
</script>

<template>
  <div class="app">
    <main class="app-main">
      <div class="page">
        <ManagementPage />
      </div>
    </main>
    <button class="top-button" type="button" @click="scrollToTop">TOP</button>
  </div>
</template>

<style scoped>
.app {
  min-height: 100vh;
  background: transparent;
  padding: 0;
}

.app-main {
  padding: 0 24px 16px;
}

.page {
  max-width: 1520px;
  margin: 0 auto;
  width: 100%;
}

.top-button {
  position: fixed;
  right: 20px;
  bottom: 20px;
  width: 56px;
  height: 56px;
  border: none;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #475569 0%, #334155 100%);
  color: #fff;
  font-size: 14px;
  font-weight: 700;
  cursor: pointer;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
  transition: transform 0.2s, box-shadow 0.2s;
  z-index: 9997;
}

.top-button:hover {
  transform: scale(1.05);
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.35);
}

</style>

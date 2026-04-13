<template>
  <div>
    <!-- 로그인 페이지 또는 메인 페이지 표시 -->
    <LoginPage v-if="!isLoggedIn" />
    <div v-else>
      <header class="app-header">
        <h1 class="app-title">AiDesk</h1>
        <div class="user-info">
          <span class="username">{{ displayUserName }}</span>
          <button class="logout-btn" @click="handleLogout">로그아웃</button>
        </div>
      </header>
      <ManagementPage :user="currentUser" />
    </div>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import ManagementPage from './views/ManagementPage.vue'
import LoginPage from './views/LoginPage.vue'

const isLoggedIn = ref(false)
const currentUser = ref(null)

const displayUserName = computed(() => {
  const name = currentUser.value?.username
  if (!name) return ''
  return String(name).toLowerCase() === 'admin' ? '관리자' : name
})

let adminWidget = null
let userWidget = null
let isDisposed = false

function scrollToTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

function handleLogout() {
  isLoggedIn.value = false
  currentUser.value = null
  localStorage.removeItem('token')
  localStorage.removeItem('user')
}

function checkLoginStatus() {
  const token = localStorage.getItem('token')
  const user = localStorage.getItem('user')
  
  if (token && user) {
    isLoggedIn.value = true
    currentUser.value = JSON.parse(user)
  } else {
    isLoggedIn.value = false
    currentUser.value = null
  }
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

function setupLoginListener() {
  window.addEventListener('login-success', (event) => {
    const user = event.detail
    isLoggedIn.value = true
    currentUser.value = user
    // 채팅 위젯 로드
    loadChatWidgets()
  })
}

async function loadChatWidgets() {
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
    buttonBottom: '110px',
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
    buttonBottom: '40px',
    popupRight: '20px',
    popupBottom: '156px',
    showPlatformSelector: true,
    defaultPlatform: '전체 플랫폼',
    initiallyOpen: false
  })
}

onMounted(async () => {
  checkLoginStatus()
  setupLoginListener()
  
  if (isLoggedIn.value) {
    await loadChatWidgets()
  }
})

onBeforeUnmount(() => {
  isDisposed = true
  destroyWidgets()
})
</script>

<style scoped>
.app-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
  background: linear-gradient(135deg, #0d6efd 0%, #764ba2 100%);
  color: white;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  margin-bottom: 24px;
}

.app-title {
  font-size: 24px;
  font-weight: 700;
  margin: 0;
}

.user-info {
  display: flex;
  align-items: center;
  gap: 16px;
}

.username {
  font-size: 14px;
  font-weight: 500;
}

.logout-btn {
  padding: 8px 16px;
  background: rgba(255, 255, 255, 0.2);
  color: white;
  border: 1px solid white;
  border-radius: 5px;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  transition: background 0.2s;
}

.logout-btn:hover {
  background: rgba(255, 255, 255, 0.3);
}

.app {
  min-height: 100vh;
  background: #f5f5f5;
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

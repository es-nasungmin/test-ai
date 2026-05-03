<template>
  <div>
    <!-- 로그인 페이지 또는 메인 페이지 표시 -->
    <LoginPage v-if="!isLoggedIn" />
    <div v-else>
      <header class="app-header">
        <h1 class="app-title">AiDesk</h1>
        <div class="user-info">
          <button class="username-link" type="button" @click="openMyPage">{{ displayUserName }}</button>
          <button class="logout-btn" @click="handleLogout">로그아웃</button>
        </div>
      </header>
      <ManagementPage :user="currentUser" />

      <div v-if="showMyPage" class="mypage-overlay" @click="closeMyPage">
        <div class="mypage-modal" @click.stop>
          <div class="mypage-head">
            <h3>마이페이지</h3>
            <button class="ghost-btn" type="button" @click="closeMyPage">닫기</button>
          </div>

          <div class="mypage-body">
            <div v-if="myPageError" class="error-message mypage-msg">{{ myPageError }}</div>
            <div v-if="myPageSuccess" class="success-message mypage-msg">{{ myPageSuccess }}</div>

            <section class="mypage-section">
              <h4>내 정보</h4>
              <div class="mypage-grid">
                <label>로그인 아이디</label>
                <input type="text" :value="myPage.loginId || '-'" disabled />
              </div>
            </section>

            <section class="mypage-section">
              <h4>이름 변경</h4>
              <form @submit.prevent="submitProfileUpdate">
                <div class="mypage-grid">
                  <label for="mypage-username">사용자명</label>
                  <input id="mypage-username" v-model="profileForm.username" type="text" required />
                </div>
                <button class="save-btn" type="submit" :disabled="savingProfile">
                  {{ savingProfile ? '저장 중...' : '이름 저장' }}
                </button>
              </form>
            </section>

            <section class="mypage-section">
              <h4>비밀번호 변경</h4>
              <form @submit.prevent="submitPasswordChange">
                <div class="mypage-grid">
                  <label for="mypage-current-password">현재 비밀번호</label>
                  <input id="mypage-current-password" v-model="passwordForm.currentPassword" type="password" required />
                  <label for="mypage-new-password">새 비밀번호</label>
                  <input id="mypage-new-password" v-model="passwordForm.newPassword" type="password" required />
                  <label for="mypage-confirm-password">새 비밀번호 확인</label>
                  <input id="mypage-confirm-password" v-model="passwordForm.confirmPassword" type="password" required />
                </div>
                <button class="save-btn" type="submit" :disabled="savingPassword">
                  {{ savingPassword ? '변경 중...' : '비밀번호 변경' }}
                </button>
              </form>
            </section>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import ManagementPage from './views/ManagementPage.vue'
import LoginPage from './views/LoginPage.vue'
import { API_BASE_URL } from './config'
import { authApi } from './api'

const isLoggedIn = ref(false)
const currentUser = ref(null)
const showMyPage = ref(false)
const loadingMyPage = ref(false)
const savingProfile = ref(false)
const savingPassword = ref(false)
const myPageError = ref('')
const myPageSuccess = ref('')
const myPage = ref({
  loginId: '',
  username: ''
})
const profileForm = ref({
  username: ''
})
const passwordForm = ref({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
})

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
  showMyPage.value = false
  localStorage.removeItem('token')
  localStorage.removeItem('user')
  destroyWidgets()
}

async function openMyPage() {
  showMyPage.value = true
  myPageError.value = ''
  myPageSuccess.value = ''
  await loadMyPage()
}

function closeMyPage() {
  showMyPage.value = false
  myPageError.value = ''
  myPageSuccess.value = ''
}

async function loadMyPage() {
  loadingMyPage.value = true
  myPageError.value = ''

  try {
    const response = await authApi.getMe()
    const data = response?.data || {}
    myPage.value = {
      loginId: data.loginId || '',
      username: data.username || ''
    }
    profileForm.value.username = data.username || ''
  } catch (error) {
    myPageError.value = error?.response?.data?.message || '내 정보를 불러오지 못했습니다.'
  } finally {
    loadingMyPage.value = false
  }
}

async function submitProfileUpdate() {
  const username = profileForm.value.username.trim()
  if (!username) {
    myPageError.value = '사용자명을 입력해주세요.'
    return
  }

  savingProfile.value = true
  myPageError.value = ''
  myPageSuccess.value = ''

  try {
    const response = await authApi.updateMyProfile(username)
    const updatedUser = response?.data?.user
    if (updatedUser) {
      currentUser.value = updatedUser
      localStorage.setItem('user', JSON.stringify(updatedUser))
      myPage.value.username = updatedUser.username || username
      profileForm.value.username = updatedUser.username || username
    }
    myPageSuccess.value = response?.data?.message || '이름이 변경되었습니다.'
  } catch (error) {
    myPageError.value = error?.response?.data?.message || '이름 변경에 실패했습니다.'
  } finally {
    savingProfile.value = false
  }
}

async function submitPasswordChange() {
  if (!passwordForm.value.currentPassword || !passwordForm.value.newPassword || !passwordForm.value.confirmPassword) {
    myPageError.value = '비밀번호 항목을 모두 입력해주세요.'
    return
  }

  if (passwordForm.value.newPassword !== passwordForm.value.confirmPassword) {
    myPageError.value = '새 비밀번호와 확인 비밀번호가 일치하지 않습니다.'
    return
  }

  savingPassword.value = true
  myPageError.value = ''
  myPageSuccess.value = ''

  try {
    const response = await authApi.changeMyPassword(
      passwordForm.value.currentPassword,
      passwordForm.value.newPassword,
      passwordForm.value.confirmPassword
    )
    myPageSuccess.value = response?.data?.message || '비밀번호가 변경되었습니다.'
    passwordForm.value = {
      currentPassword: '',
      newPassword: '',
      confirmPassword: ''
    }
  } catch (error) {
    myPageError.value = error?.response?.data?.message || '비밀번호 변경에 실패했습니다.'
  } finally {
    savingPassword.value = false
  }
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

function getWidgetUserContext() {
  const user = currentUser.value || {}
  return {
    userId: user.id != null ? String(user.id) : '',
    username: typeof user.username === 'string' ? user.username : '',
    userLoginId: typeof user.loginId === 'string' ? user.loginId : ''
  }
}

async function loadChatWidgets() {
  const createWidget = await loadChatWidgetScript()
  if (isDisposed || typeof createWidget !== 'function') return

  destroyWidgets()

  const userContext = getWidgetUserContext()

  adminWidget = createWidget({
    apiBaseUrl: API_BASE_URL,
    role: 'admin',
    themeColor: '#f56565',
    userId: userContext.userId,
    username: userContext.username,
    userLoginId: userContext.userLoginId,
    title: '관리자 위젯',
    platformLabel: '테스트',
    buttonLabel: 'ADMIN',
    buttonRight: '20px',
    buttonBottom: '110px',
    popupRight: '20px',
    popupBottom: '224px',
    showPlatformSelector: true,
    platform: '전체 플랫폼',
    initiallyOpen: false
  })

  userWidget = createWidget({
    apiBaseUrl: API_BASE_URL,
    role: 'user',
    userId: userContext.userId,
    username: userContext.username,
    userLoginId: userContext.userLoginId,
    title: '사용자 위젯',
    platformLabel: '테스트',
    buttonLabel: 'USER',
    buttonRight: '20px',
    buttonBottom: '40px',
    popupRight: '20px',
    popupBottom: '156px',
    showPlatformSelector: true,
    platform: '전체 플랫폼',
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

.username-link {
  border: none;
  background: transparent;
  color: #fff;
  font-size: 14px;
  font-weight: 700;
  cursor: pointer;
  padding: 0;
}

.username-link:hover {
  text-decoration: underline;
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

.mypage-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.35);
  z-index: 3000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}

.mypage-modal {
  width: 100%;
  max-width: 560px;
  background: #fff;
  border-radius: 12px;
  box-shadow: 0 14px 44px rgba(15, 23, 42, 0.28);
  overflow: hidden;
}

.mypage-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid #eef2f7;
}

.mypage-head h3 {
  margin: 0;
}

.ghost-btn {
  border: 1px solid #d0d7de;
  border-radius: 8px;
  padding: 6px 12px;
  background: #fff;
  cursor: pointer;
}

.mypage-body {
  padding: 16px 18px 18px;
}

.mypage-section + .mypage-section {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid #f1f4f8;
}

.mypage-section h4 {
  margin: 0 0 10px;
  font-size: 15px;
}

.mypage-section form {
  display: flex;
  flex-direction: column;
}

.mypage-grid {
  display: grid;
  grid-template-columns: 120px 1fr;
  gap: 8px 10px;
  align-items: center;
}

.mypage-grid label {
  font-size: 13px;
  color: #57606a;
  font-weight: 700;
}

.mypage-grid input {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid #d0d7de;
  border-radius: 8px;
  padding: 9px 10px;
  font-size: 14px;
}

.mypage-grid input:disabled {
  background: #f6f8fa;
  color: #57606a;
}

.save-btn {
  margin-top: 10px;
  align-self: flex-end;
  border: none;
  border-radius: 8px;
  padding: 7px 12px;
  background: #1f6feb;
  color: #fff;
  font-size: 13px;
  font-weight: 700;
  cursor: pointer;
}

.save-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.mypage-msg {
  margin-bottom: 10px;
}

@media (max-width: 640px) {
  .mypage-grid {
    grid-template-columns: 1fr;
  }
}

</style>

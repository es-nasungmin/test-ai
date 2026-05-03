<template>
  <div class="login-container">
    <div class="login-box">
      <h1 class="login-title">AiDesk</h1>
      <form @submit.prevent="handleSubmit">
        <div class="form-group">
          <label for="login-id">로그인 아이디</label>
          <input
            id="login-id"
            v-model="form.loginId"
            type="text"
            placeholder="로그인 아이디를 입력하세요"
            required
          />
        </div>

        <div class="form-group">
          <label for="password">비밀번호</label>
          <input
            id="password"
            v-model="form.password"
            type="password"
            placeholder="비밀번호를 입력하세요"
            required
          />
        </div>

        <div v-if="errorMessage" class="error-message">
          {{ errorMessage }}
        </div>

        <button
          type="submit"
          class="login-btn"
          :disabled="isLoading"
        >
          {{ isLoading ? '로그인 중...' : '로그인' }}
        </button>

        <button
          type="button"
          class="signup-link"
          :disabled="isLoading"
          @click="toggleMode"
        >
          회원가입
        </button>
      </form>
    </div>
  </div>

  <!-- 회원가입 모달 (임시 비활성 안내용) -->
  <div v-if="isRegisterMode" class="modal-overlay" @click="toggleMode">
    <div class="modal-box" @click.stop>
      <h2>회원가입</h2>
      <form @submit.prevent="handleRegister">
        <div class="form-group">
          <label for="reg-username">사용자명</label>
          <input
            id="reg-username"
            v-model="registerForm.username"
            type="text"
            placeholder="사용자명을 입력하세요"
            required
          />
        </div>

        <div class="form-group">
          <label for="reg-login-id">로그인 아이디</label>
          <div class="id-check-row">
            <input
              id="reg-login-id"
              v-model="registerForm.loginId"
              type="text"
              placeholder="로그인 아이디를 입력하세요"
              required
              @input="onRegisterLoginIdInput"
            />
            <button type="button" class="check-btn" :disabled="isCheckingLoginId || !registerForm.loginId.trim()" @click="checkLoginIdDuplicate">
              {{ isCheckingLoginId ? '확인 중...' : '중복확인' }}
            </button>
          </div>
          <div v-if="loginIdCheckMessage" :class="['hint-message', loginIdCheckOk ? 'hint-ok' : 'hint-error']">
            {{ loginIdCheckMessage }}
          </div>
        </div>

        <div class="form-group">
          <label for="reg-password">비밀번호</label>
          <input
            id="reg-password"
            v-model="registerForm.password"
            type="password"
            placeholder="비밀번호를 입력하세요"
            required
          />
        </div>

        <div class="form-group">
          <label for="reg-confirm">비밀번호 확인</label>
          <input
            id="reg-confirm"
            v-model="registerForm.confirmPassword"
            type="password"
            placeholder="비밀번호를 다시 입력하세요"
            required
          />
        </div>

        <div v-if="registerErrorMessage" class="error-message">
          {{ registerErrorMessage }}
        </div>

        <div v-if="registerSuccessMessage" class="success-message">
          {{ registerSuccessMessage }}
        </div>

        <button
          type="submit"
          class="login-btn"
          :disabled="isRegisterLoading"
        >
          {{ isRegisterLoading ? '가입 중...' : '회원가입' }}
        </button>
      </form>

      <button class="close-btn" @click="toggleMode">닫기</button>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { authApi } from '../api'

const form = ref({
  loginId: '',
  password: ''
})

const registerForm = ref({
  loginId: '',
  username: '',
  password: '',
  confirmPassword: ''
})

const isLoading = ref(false)
const isRegisterLoading = ref(false)
const errorMessage = ref('')
const registerErrorMessage = ref('')
const registerSuccessMessage = ref('')
const isRegisterMode = ref(false)
const isCheckingLoginId = ref(false)
const loginIdCheckOk = ref(false)
const loginIdCheckMessage = ref('')
const checkedLoginId = ref('')

const toggleMode = () => {
  isRegisterMode.value = !isRegisterMode.value
  errorMessage.value = ''
  registerErrorMessage.value = ''
  registerSuccessMessage.value = ''
  isCheckingLoginId.value = false
  loginIdCheckOk.value = false
  loginIdCheckMessage.value = ''
  checkedLoginId.value = ''
}

const onRegisterLoginIdInput = () => {
  if (registerForm.value.loginId.trim() !== checkedLoginId.value) {
    loginIdCheckOk.value = false
    loginIdCheckMessage.value = ''
  }
}

const checkLoginIdDuplicate = async () => {
  const loginId = registerForm.value.loginId.trim()
  if (!loginId) {
    loginIdCheckOk.value = false
    loginIdCheckMessage.value = '로그인 아이디를 입력해주세요.'
    return
  }

  isCheckingLoginId.value = true
  loginIdCheckMessage.value = ''

  try {
    const response = await authApi.checkLoginId(loginId)
    const exists = response?.data?.exists === true
    loginIdCheckOk.value = !exists
    checkedLoginId.value = loginId
    loginIdCheckMessage.value = response?.data?.message || (!exists ? '사용 가능한 아이디입니다.' : '이미 사용 중인 아이디입니다.')
  } catch {
    loginIdCheckOk.value = false
    loginIdCheckMessage.value = '중복확인 중 오류가 발생했습니다.'
  } finally {
    isCheckingLoginId.value = false
  }
}

const handleSubmit = async () => {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const response = await authApi.login(form.value.loginId, form.value.password)
    
    if (response.data.success) {
      // 토큰과 사용자 정보 저장
      localStorage.setItem('token', response.data.token)
      localStorage.setItem('user', JSON.stringify(response.data.user))
      
      // 로그인 성공 이벤트 발생
      window.dispatchEvent(new CustomEvent('login-success', { 
        detail: response.data.user 
      }))
    } else {
      errorMessage.value = response.data.message || '로그인에 실패했습니다.'
      form.value.loginId = ''
      form.value.password = ''
    }
  } catch (error) {
    errorMessage.value = error.response?.data?.message || '오류가 발생했습니다.'
    form.value.loginId = ''
    form.value.password = ''
  } finally {
    isLoading.value = false
  }
}

const handleRegister = async () => {
  if (!registerForm.value.username.trim()) {
    registerErrorMessage.value = '사용자명을 입력해주세요.'
    return
  }

  const currentLoginId = registerForm.value.loginId.trim()
  if (!loginIdCheckOk.value || checkedLoginId.value !== currentLoginId) {
    registerErrorMessage.value = '로그인 아이디 중복확인을 먼저 진행해주세요.'
    return
  }

  isRegisterLoading.value = true
  registerErrorMessage.value = ''
  registerSuccessMessage.value = ''

  try {
    const response = await authApi.register(
      registerForm.value.loginId,
      registerForm.value.username,
      registerForm.value.password,
      registerForm.value.confirmPassword
    )
    
    if (response.data.success) {
      // 회원가입 완료 메시지 표시 후 로그인 화면으로 돌아가기
      registerSuccessMessage.value = '회원가입이 완료되었습니다. 관리자의 승인 후 로그인해주세요.'
      registerForm.value = {
        loginId: '',
        username: '',
        password: '',
        confirmPassword: ''
      }
      loginIdCheckOk.value = false
      loginIdCheckMessage.value = ''
      checkedLoginId.value = ''
      
      // 2초 후 로그인 모달 닫기
      setTimeout(() => {
        isRegisterMode.value = false
        registerSuccessMessage.value = ''
      }, 2000)
    } else {
      registerErrorMessage.value = response.data.message || '회원가입에 실패했습니다.'
    }
  } catch (error) {
    registerErrorMessage.value = error.response?.data?.message || '오류가 발생했습니다.'
  } finally {
    isRegisterLoading.value = false
  }
}
</script>

<style scoped>
.login-container {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 100vh;
  background: linear-gradient(135deg, #0d6efd 0%, #764ba2 100%);
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
  position: relative;
}

.login-container::before {
  content: '';
  position: absolute;
  inset: 0;
  background:
    radial-gradient(circle at 18% 20%, rgba(255, 255, 255, 0.18), transparent 40%),
    radial-gradient(circle at 82% 75%, rgba(255, 255, 255, 0.12), transparent 38%);
  pointer-events: none;
}

.login-box {
  background: white;
  padding: 40px;
  border-radius: 10px;
  box-shadow: 0 12px 40px rgba(15, 23, 42, 0.28);
  width: 100%;
  max-width: 400px;
  position: relative;
  z-index: 1;
}

.login-title {
  text-align: center;
  margin-bottom: 30px;
  color: #333;
  font-size: 32px;
  font-weight: 700;
}

.form-group {
  margin-bottom: 20px;
}

.form-group label {
  display: block;
  margin-bottom: 8px;
  color: #555;
  font-weight: 500;
  font-size: 14px;
}

.form-group input {
  width: 100%;
  padding: 12px;
  border: 1px solid #ddd;
  border-radius: 5px;
  font-size: 14px;
  box-sizing: border-box;
  transition: border-color 0.3s;
}

.form-group input:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.1);
}

.id-check-row {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 8px;
}

.check-btn {
  padding: 10px 12px;
  border: 1px solid #cfd4da;
  border-radius: 5px;
  background: #f8f9fa;
  color: #495057;
  font-size: 13px;
  font-weight: 700;
  cursor: pointer;
  white-space: nowrap;
}

.check-btn:hover:not(:disabled) {
  background: #e9ecef;
}

.check-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.hint-message {
  margin-top: 8px;
  font-size: 12px;
  font-weight: 600;
}

.hint-ok {
  color: #2b8a3e;
}

.hint-error {
  color: #c92a2a;
}

.login-btn {
  width: 100%;
  padding: 12px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border: none;
  border-radius: 5px;
  font-size: 16px;
  font-weight: 600;
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s;
}

.login-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 10px 20px rgba(102, 126, 234, 0.2);
}

.login-btn:disabled {
  opacity: 0.7;
  cursor: not-allowed;
}

.signup-link {
  width: 100%;
  margin-top: 10px;
  padding: 10px;
  border: none;
  background: transparent;
  color: #4c6ef5;
  font-size: 14px;
  font-weight: 700;
  cursor: pointer;
}

.signup-link:hover:not(:disabled) {
  text-decoration: underline;
}

.signup-link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.error-message {
  color: #e74c3c;
  font-size: 14px;
  margin-bottom: 15px;
  padding: 10px;
  background: #fadad8;
  border-radius: 5px;
  text-align: center;
}

.success-message {
  color: #27ae60;
  font-size: 14px;
  margin-bottom: 15px;
  padding: 10px;
  background: #d5f4e6;
  border-radius: 5px;
  text-align: center;
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.modal-box {
  background: white;
  padding: 40px;
  border-radius: 10px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.3);
  width: 100%;
  max-width: 400px;
  position: relative;
}

.modal-box h2 {
  text-align: center;
  margin-bottom: 30px;
  color: #333;
  font-size: 24px;
}

.close-btn {
  width: 100%;
  padding: 10px;
  margin-top: 20px;
  background: #e5e5e5;
  color: #333;
  border: none;
  border-radius: 5px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.2s;
}

.close-btn:hover {
  background: #d0d0d0;
}
</style>

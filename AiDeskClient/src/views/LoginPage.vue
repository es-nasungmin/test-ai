<template>
  <div class="login-container">
    <div class="login-box">
      <h1 class="login-title">AiDesk</h1>
      <form @submit.prevent="handleSubmit">
        <div class="form-group">
          <label for="username">아이디</label>
          <input
            id="username"
            v-model="form.username"
            type="text"
            placeholder="아이디를 입력하세요"
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
      </form>
    </div>
  </div>

  <!-- 회원가입 모달 (임시 비활성 안내용) -->
  <div v-if="isRegisterMode" class="modal-overlay" @click="toggleMode">
    <div class="modal-box" @click.stop>
      <h2>회원가입</h2>
      <form @submit.prevent="handleRegister">
        <div class="form-group">
          <label for="reg-username">아이디</label>
          <input
            id="reg-username"
            v-model="registerForm.username"
            type="text"
            placeholder="아이디를 입력하세요"
            required
          />
        </div>

        <div class="form-group">
          <label for="reg-email">이메일</label>
          <input
            id="reg-email"
            v-model="registerForm.email"
            type="email"
            placeholder="이메일을 입력하세요"
            required
          />
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
  username: '',
  password: ''
})

const registerForm = ref({
  username: '',
  email: '',
  password: '',
  confirmPassword: ''
})

const isLoading = ref(false)
const isRegisterLoading = ref(false)
const errorMessage = ref('')
const registerErrorMessage = ref('')
const registerSuccessMessage = ref('')
const isRegisterMode = ref(false)

const toggleMode = () => {
  isRegisterMode.value = !isRegisterMode.value
  errorMessage.value = ''
  registerErrorMessage.value = ''
  registerSuccessMessage.value = ''
}

const handleSubmit = async () => {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const response = await authApi.login(form.value.username, form.value.password)
    
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
    }
  } catch (error) {
    errorMessage.value = error.response?.data?.message || '오류가 발생했습니다.'
  } finally {
    isLoading.value = false
  }
}

const handleRegister = async () => {
  registerErrorMessage.value = '현재는 admin 계정 로그인만 지원됩니다.'
  isRegisterLoading.value = false
  return

  isRegisterLoading.value = true
  registerErrorMessage.value = ''
  registerSuccessMessage.value = ''

  try {
    const response = await authApi.register(
      registerForm.value.username,
      registerForm.value.email,
      registerForm.value.password,
      registerForm.value.confirmPassword
    )
    
    if (response.data.success) {
      // 회원가입 완료 메시지 표시 후 로그인 화면으로 돌아가기
      registerSuccessMessage.value = '회원가입이 완료되었습니다. 관리자의 승인 후 로그인해주세요.'
      registerForm.value = {
        username: '',
        email: '',
        password: '',
        confirmPassword: ''
      }
      
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

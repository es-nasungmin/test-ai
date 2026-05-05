import axios from 'axios'
import { API_BASE_URL } from './config'

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  }
})

function attachAuthInterceptors(client) {
  client.interceptors.request.use(
    config => {
      const token = localStorage.getItem('token')
      if (token) {
        config.headers = config.headers || {}
        config.headers.Authorization = `Bearer ${token}`
      }
      return config
    },
    error => Promise.reject(error)
  )

  client.interceptors.response.use(
    response => response,
    error => {
      const status = error.response?.status
      const requestUrl = String(error.config?.url || '').toLowerCase()
      const hasToken = !!localStorage.getItem('token')
      const isAuthEntryRequest = requestUrl.includes('/auth/login') || requestUrl.includes('/auth/register')

      if (status === 401 && hasToken && !isAuthEntryRequest) {
        localStorage.removeItem('token')
        localStorage.removeItem('user')
        window.location.href = '/'
      }

      return Promise.reject(error)
    }
  )
}

// 커스텀 apiClient와 기본 axios 모두에 인증 인터셉터 적용
attachAuthInterceptors(apiClient)
attachAuthInterceptors(axios)

export const authApi = {
  login: (loginId, password) => 
    apiClient.post('/auth/login', { loginId, password }),

  checkLoginId: (loginId) =>
    apiClient.get('/auth/check-login-id', { params: { loginId } }),
  
  register: (loginId, username, password, confirmPassword) =>
    apiClient.post('/auth/register', { loginId, username, password, confirmPassword }),

  getMe: () =>
    apiClient.get('/auth/me'),

  updateMyProfile: (username) =>
    apiClient.put('/auth/me/profile', { username }),

  changeMyPassword: (currentPassword, newPassword, confirmPassword) =>
    apiClient.put('/auth/me/password', { currentPassword, newPassword, confirmPassword }),
  
  validate: () =>
    apiClient.post('/auth/validate'),

  // Admin endpoints
  getUsers: () =>
    apiClient.get('/auth/users'),

  getPendingUsers: () =>
    apiClient.get('/auth/pending-users'),

  approveUser: (userId) =>
    apiClient.post(`/auth/approve-user/${userId}`),

  updateUser: (userId, payload) =>
    apiClient.put(`/auth/users/${userId}`, payload),

  deleteUser: (userId) =>
    apiClient.delete(`/auth/users/${userId}`),

  rejectUser: (userId) =>
    apiClient.post(`/auth/reject-user/${userId}`)
}

export const chatApi = {
  chat: (message) =>
    apiClient.post('/chat/chat', { message })
}

export const customerApi = {
  getCustomers: () =>
    apiClient.get('/customer'),
  
  addCustomer: (customer) =>
    apiClient.post('/customer', customer),
  
  updateCustomer: (id, customer) =>
    apiClient.put(`/customer/${id}`, customer)
}

export const knowledgeBaseApi = {
  getKnowledgeBases: () =>
    apiClient.get('/knowledgebase'),
  
  addKnowledgeBase: (kb) =>
    apiClient.post('/knowledgebase', kb)
}

export default apiClient

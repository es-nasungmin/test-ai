import axios from 'axios'
import { API_BASE_URL } from './config'

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  }
})

// 요청 인터셉터: 토큰 추가
apiClient.interceptors.request.use(
  config => {
    const token = localStorage.getItem('token')
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }
    return config
  },
  error => Promise.reject(error)
)

// 응답 인터셉터: 401 처리
apiClient.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token')
      localStorage.removeItem('user')
      window.location.href = '/'
    }
    return Promise.reject(error)
  }
)

export const authApi = {
  login: (username, password) => 
    apiClient.post('/auth/login', { username, password }),
  
  register: (username, email, password, confirmPassword) =>
    apiClient.post('/auth/register', { username, email, password, confirmPassword }),
  
  validate: () =>
    apiClient.post('/auth/validate'),

  // Admin endpoints
  getUsers: () =>
    apiClient.get('/auth/users'),

  getPendingUsers: () =>
    apiClient.get('/auth/pending-users'),

  approveUser: (userId) =>
    apiClient.post(`/auth/approve-user/${userId}`),

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

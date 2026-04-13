<template>
  <div class="user-approval-container">
    <div class="header">
      <h2>사용자 관리</h2>
      <button @click="loadUsers" class="btn-refresh">
        <span v-if="!loadingUsers">새로고침</span>
        <span v-else>로딩 중...</span>
      </button>
    </div>

    <div v-if="error" class="error-message">
      {{ error }}
    </div>

    <div v-if="approvalMessage" class="success-message">
      {{ approvalMessage }}
    </div>

    <div v-if="loadingUsers" class="loading">
      사용자 목록을 불러오는 중...
    </div>

    <div v-else-if="users.length === 0" class="no-data">
      등록된 사용자가 없습니다.
    </div>

    <div v-else class="users-list">
      <table>
        <thead>
          <tr>
            <th>사용자명</th>
            <th>이메일</th>
            <th>권한</th>
            <th>상태</th>
            <th>가입일</th>
            <th>최근 로그인</th>
            <th>작업</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="user in users" :key="user.id" class="user-row">
            <td class="username">{{ user.username }}</td>
            <td class="email">{{ user.email }}</td>
            <td>{{ user.role }}</td>
            <td>
              <span :class="['status-badge', user.isApproved ? 'status-approved' : 'status-pending']">
                {{ user.isApproved ? '승인됨' : '대기' }}
              </span>
            </td>
            <td class="created-at">{{ formatDate(user.createdAt) }}</td>
            <td class="created-at">{{ formatDate(user.lastLoginAt) }}</td>
            <td class="actions">
              <button
                @click="approveUser(user.id)"
                class="btn btn-approve"
                :disabled="approvingUserId === user.id || user.isApproved || user.role === 'admin'"
              >
                {{ approvingUserId === user.id ? '승인 중...' : (user.isApproved ? '승인완료' : '승인') }}
              </button>
              <button
                @click="rejectUser(user.id)"
                class="btn btn-reject"
                :disabled="rejectingUserId === user.id || user.role === 'admin'"
              >
                {{ rejectingUserId === user.id ? '거절 중...' : '거절' }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { authApi } from '../../api'

const users = ref([])
const loadingUsers = ref(false)
const error = ref(null)
const approvalMessage = ref(null)
const approvingUserId = ref(null)
const rejectingUserId = ref(null)

const loadUsers = async () => {
  loadingUsers.value = true
  error.value = null
  approvalMessage.value = null

  try {
    const response = await authApi.getUsers()
    users.value = response.data
  } catch (err) {
    error.value = '사용자 목록을 불러오지 못했습니다.'
    console.error(err)
  } finally {
    loadingUsers.value = false
  }
}

const approveUser = async (userId) => {
  approvingUserId.value = userId
  error.value = null

  try {
    await authApi.approveUser(userId)
    approvalMessage.value = '사용자가 승인되었습니다.'
    await loadUsers()
    setTimeout(() => {
      approvalMessage.value = null
    }, 3000)
  } catch (err) {
    error.value = '사용자 승인에 실패했습니다.'
    console.error(err)
  } finally {
    approvingUserId.value = null
  }
}

const rejectUser = async (userId) => {
  if (!confirm('이 사용자를 거절하시겠습니까?')) {
    return
  }

  rejectingUserId.value = userId
  error.value = null

  try {
    await authApi.rejectUser(userId)
    approvalMessage.value = '사용자가 거절되었습니다.'
    await loadUsers()
    setTimeout(() => {
      approvalMessage.value = null
    }, 3000)
  } catch (err) {
    error.value = '사용자 거절에 실패했습니다.'
    console.error(err)
  } finally {
    rejectingUserId.value = null
  }
}

const formatDate = (dateString) => {
  if (!dateString) {
    return '-'
  }

  const date = new Date(dateString)
  return date.toLocaleDateString('ko-KR', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  })
}

onMounted(() => {
  loadUsers()
})
</script>

<style scoped>
.user-approval-container {
  padding: 20px;
  background: white;
  border-radius: 8px;
}

.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  border-bottom: 2px solid #f0f0f0;
  padding-bottom: 15px;
}

.header h2 {
  margin: 0;
  font-size: 20px;
  color: #333;
}

.btn-refresh {
  padding: 8px 16px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 14px;
  transition: opacity 0.3s;
}

.btn-refresh:hover {
  opacity: 0.9;
}

.error-message {
  padding: 12px;
  background: #fee;
  color: #c33;
  border-radius: 6px;
  margin-bottom: 15px;
  font-size: 14px;
}

.success-message {
  padding: 12px;
  background: #efe;
  color: #3c3;
  border-radius: 6px;
  margin-bottom: 15px;
  font-size: 14px;
}

.loading,
.no-data {
  text-align: center;
  padding: 40px;
  color: #999;
  font-size: 16px;
}

.users-list {
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

thead {
  background: #f9f9f9;
  border-bottom: 2px solid #e0e0e0;
}

th {
  padding: 12px;
  text-align: left;
  font-weight: 600;
  color: #333;
}

tbody tr {
  border-bottom: 1px solid #f0f0f0;
}

tbody tr:hover {
  background: #fafafa;
}

td {
  padding: 12px;
}

.username {
  font-weight: 500;
  color: #333;
  min-width: 150px;
}

.email {
  color: #666;
  min-width: 200px;
}

.created-at {
  color: #999;
  min-width: 150px;
  font-size: 13px;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  padding: 4px 10px;
}

.status-approved {
  background: #dff5e4;
  color: #207a32;
}

.status-pending {
  background: #fff2d6;
  color: #8a5700;
}

.actions {
  display: flex;
  gap: 8px;
  min-width: 200px;
}

.btn {
  padding: 6px 12px;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  font-weight: 500;
  transition: all 0.3s;
}

.btn-approve {
  background: #4caf50;
  color: white;
}

.btn-approve:hover:not(:disabled) {
  background: #45a049;
}

.btn-reject {
  background: #f44336;
  color: white;
}

.btn-reject:hover:not(:disabled) {
  background: #da190b;
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* 모바일 반응형 */
@media (max-width: 768px) {
  .user-approval-container {
    padding: 15px;
  }

  .header {
    flex-direction: column;
    align-items: flex-start;
    gap: 10px;
  }

  table {
    font-size: 12px;
  }

  th,
  td {
    padding: 8px;
  }

  .actions {
    flex-direction: column;
  }

  .btn {
    width: 100%;
  }
}
</style>

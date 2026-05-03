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
            <th>관리자명</th>
            <th>아이디</th>
            <th>권한</th>
            <th>상태</th>
            <th>가입일</th>
            <th>최근 로그인</th>
            <th>작업</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="user in users" :key="user.id" class="user-row">
            <td class="username">{{ formatDisplayName(user) }}</td>
            <td class="username-id">{{ user.loginId || '-' }}</td>
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
                @click="openEditModal(user)"
                class="btn btn-edit"
                :disabled="savingEditUserId === user.id"
              >
                {{ savingEditUserId === user.id ? '저장 중...' : '수정' }}
              </button>
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

    <div v-if="editUserModalVisible" class="edit-modal-overlay" @click="closeEditModal">
      <div class="edit-modal" @click.stop>
        <div class="edit-modal-head">
          <h3>사용자 정보 수정</h3>
          <button type="button" class="edit-modal-close" @click="closeEditModal">닫기</button>
        </div>
        <form @submit.prevent="submitEditUser" class="edit-form">
          <label>
            로그인 아이디
            <input v-model.trim="editForm.loginId" type="text" required />
          </label>
          <label>
            사용자명
            <input v-model.trim="editForm.username" type="text" required />
          </label>
          <label>
            권한
            <select v-model="editForm.role" required>
              <option value="admin">admin</option>
              <option value="user">user</option>
            </select>
          </label>
          <div class="edit-actions">
            <button type="submit" class="btn btn-edit-save" :disabled="savingEditUserId === editForm.id">
              {{ savingEditUserId === editForm.id ? '저장 중...' : '저장' }}
            </button>
          </div>
        </form>
      </div>
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
const savingEditUserId = ref(null)
const editUserModalVisible = ref(false)
const editForm = ref({
  id: null,
  loginId: '',
  username: '',
  role: 'user'
})

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

const openEditModal = (user) => {
  editForm.value = {
    id: user.id,
    loginId: user.loginId || '',
    username: user.username || '',
    role: user.role || 'user'
  }
  editUserModalVisible.value = true
}

const closeEditModal = () => {
  editUserModalVisible.value = false
  editForm.value = {
    id: null,
    loginId: '',
    username: '',
    role: 'user'
  }
}

const submitEditUser = async () => {
  if (!editForm.value.id) return

  if (!editForm.value.loginId || !editForm.value.username || !editForm.value.role) {
    error.value = '로그인 아이디, 사용자명, 권한을 모두 입력해주세요.'
    return
  }

  savingEditUserId.value = editForm.value.id
  error.value = null

  try {
    await authApi.updateUser(editForm.value.id, {
      loginId: editForm.value.loginId,
      username: editForm.value.username,
      role: editForm.value.role
    })
    approvalMessage.value = '사용자 정보가 수정되었습니다.'
    await loadUsers()
    closeEditModal()
    setTimeout(() => {
      approvalMessage.value = null
    }, 3000)
  } catch (err) {
    error.value = err?.response?.data?.message || '사용자 정보 수정에 실패했습니다.'
    console.error(err)
  } finally {
    savingEditUserId.value = null
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

const formatDisplayName = (user) => {
  const username = String(user?.username || '').trim()
  return username || '-'
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

.username-id {
  color: #666;
  min-width: 140px;
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

.btn-edit {
  background: #4c6ef5;
  color: white;
}

.btn-edit:hover:not(:disabled) {
  background: #3b5bdb;
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

.edit-modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2100;
}

.edit-modal {
  width: 100%;
  max-width: 420px;
  background: #fff;
  border-radius: 10px;
  overflow: hidden;
  box-shadow: 0 10px 34px rgba(0, 0, 0, 0.28);
}

.edit-modal-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px;
  border-bottom: 1px solid #eef0f2;
}

.edit-modal-head h3 {
  margin: 0;
  font-size: 16px;
}

.edit-modal-close {
  border: 1px solid #d0d7de;
  border-radius: 8px;
  background: #fff;
  padding: 6px 10px;
  cursor: pointer;
}

.edit-form {
  padding: 14px 16px 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.edit-form label {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 13px;
  color: #495057;
  font-weight: 600;
}

.edit-form input,
.edit-form select {
  border: 1px solid #d0d7de;
  border-radius: 8px;
  padding: 9px 10px;
  font-size: 14px;
}

.edit-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 6px;
}

.btn-edit-save {
  background: #1f6feb;
  color: #fff;
}

.btn-edit-save:hover:not(:disabled) {
  background: #1a5fcc;
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

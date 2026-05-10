<template>
  <div class="user-approval-container">
    <div class="header">
      <h2>사용자 관리</h2>
      <button type="button" class="btn btn-create" @click="openCreateModal">계정 생성</button>
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
              <span :class="['status-badge', statusClass(user)]">
                {{ statusLabel(user) }}
              </span>
            </td>
            <td class="created-at">{{ formatDate(user.createdAt) }}</td>
            <td class="created-at">{{ formatDate(user.lastLoginAt) }}</td>
            <td class="actions">
              <button
                v-if="!isAdminAccount(user)"
                @click="openEditModal(user)"
                class="btn btn-edit"
                :disabled="savingEditUserId === user.id || deletingUserId === user.id"
              >
                {{ savingEditUserId === user.id ? '저장 중...' : '수정' }}
              </button>
              <button
                v-if="!isAdminAccount(user)"
                @click="deleteUser(user)"
                class="btn btn-delete"
                :disabled="savingEditUserId === user.id || deletingUserId === user.id"
              >
                {{ deletingUserId === user.id ? '삭제 중...' : '삭제' }}
              </button>
              <span v-else class="action-placeholder">-</span>
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
          <label>
            상태
            <select v-model="editForm.status" required>
              <option value="pending">승인대기</option>
              <option value="approved">승인</option>
              <option value="rejected">거절</option>
              <option value="deleted">삭제</option>
            </select>
          </label>
          <label class="edit-inline-checkbox">
            <input v-model="editForm.resetPassword" type="checkbox" />
            비밀번호 초기화
          </label>
          <small v-if="editForm.resetPassword" class="reset-password-hint">
            저장 시 비밀번호가 a1234567890 으로 초기화됩니다.
          </small>
          <div class="edit-actions">
            <button type="submit" class="btn btn-edit-save" :disabled="savingEditUserId === editForm.id">
              {{ savingEditUserId === editForm.id ? '저장 중...' : '저장' }}
            </button>
          </div>
        </form>
      </div>
    </div>

    <div v-if="createUserModalVisible" class="edit-modal-overlay" @click="closeCreateModal">
      <div class="edit-modal create-user-modal" @click.stop>
        <div class="edit-modal-head">
          <h3>계정 생성</h3>
          <button type="button" class="edit-modal-close" @click="closeCreateModal">닫기</button>
        </div>
        <form class="edit-form create-user-form" @submit.prevent="submitCreateUser">
          <label>
            로그인 아이디
            <input v-model.trim="createForm.loginId" type="text" required />
          </label>
          <label>
            사용자명
            <input v-model.trim="createForm.username" type="text" required />
          </label>
          <label>
            비밀번호
            <input v-model="createForm.password" type="password" required minlength="8" />
          </label>
          <label>
            비밀번호 확인
            <input v-model="createForm.confirmPassword" type="password" required minlength="8" />
          </label>
          <label>
            권한
            <select v-model="createForm.role" required>
              <option value="user">user</option>
              <option value="admin">admin</option>
            </select>
          </label>
          <small class="create-user-hint">관리자 화면에서 생성한 계정은 바로 승인 상태로 저장됩니다.</small>
          <div class="create-user-actions">
            <button type="button" class="ghost-btn" :disabled="creatingUser" @click="closeCreateModal">취소</button>
            <button type="submit" class="btn btn-create" :disabled="creatingUser">
              {{ creatingUser ? '생성 중...' : '계정 생성' }}
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
const savingEditUserId = ref(null)
const deletingUserId = ref(null)
const editUserModalVisible = ref(false)
const createUserModalVisible = ref(false)
const creatingUser = ref(false)
const createForm = ref({
  loginId: '',
  username: '',
  password: '',
  confirmPassword: '',
  role: 'user'
})
const editForm = ref({
  id: null,
  loginId: '',
  username: '',
  role: 'user',
  status: 'pending',
  resetPassword: false
})
const DEFAULT_RESET_PASSWORD = 'a1234567890'

function resetCreateForm() {
  createForm.value = {
    loginId: '',
    username: '',
    password: '',
    confirmPassword: '',
    role: 'user'
  }
}

function openCreateModal() {
  error.value = null
  approvalMessage.value = null
  resetCreateForm()
  createUserModalVisible.value = true
}

function closeCreateModal() {
  if (creatingUser.value) {
    return
  }

  createUserModalVisible.value = false
  resetCreateForm()
}

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

function resolveStatus(user) {
  const status = typeof user?.status === 'string' ? user.status.trim().toLowerCase() : ''
  if (status === 'approved' || status === 'pending' || status === 'rejected' || status === 'deleted') {
    return status
  }
  if (!user?.isActive) return 'rejected'
  if (user?.isApproved) return 'approved'
  return 'pending'
}

function statusLabel(user) {
  const status = resolveStatus(user)
  if (status === 'approved') return '승인'
  if (status === 'pending') return '승인대기'
  if (status === 'rejected') return '거절됨'
  if (status === 'deleted') return '삭제됨'
  return '승인대기'
}

function statusClass(user) {
  const status = resolveStatus(user)
  if (status === 'approved') return 'status-approved'
  if (status === 'pending') return 'status-pending'
  if (status === 'rejected') return 'status-rejected'
  if (status === 'deleted') return 'status-deleted'
  return 'status-pending'
}

function isAdminAccount(user) {
  return String(user?.role || '').trim().toLowerCase() === 'admin'
}

const submitCreateUser = async () => {
  if (!createForm.value.loginId || !createForm.value.username || !createForm.value.password) {
    error.value = '로그인 아이디, 사용자명, 비밀번호를 모두 입력해주세요.'
    return
  }

  if (createForm.value.password !== createForm.value.confirmPassword) {
    error.value = '비밀번호가 일치하지 않습니다.'
    return
  }

  if (createForm.value.password.length < 8) {
    error.value = '비밀번호는 8자 이상이어야 합니다.'
    return
  }

  creatingUser.value = true
  error.value = null

  try {
    await authApi.createUser({
      loginId: createForm.value.loginId,
      username: createForm.value.username,
      password: createForm.value.password,
      confirmPassword: createForm.value.confirmPassword,
      role: createForm.value.role
    })
    approvalMessage.value = '계정이 생성되었습니다.'
    alert('계정이 생성되었습니다.')
    resetCreateForm()
    createUserModalVisible.value = false
    await loadUsers()
    setTimeout(() => {
      approvalMessage.value = null
    }, 3000)
  } catch (err) {
    error.value = err?.response?.data?.message || '계정 생성에 실패했습니다.'
    console.error(err)
  } finally {
    creatingUser.value = false
  }
}

const openEditModal = (user) => {
  if (isAdminAccount(user)) {
    return
  }

  editForm.value = {
    id: user.id,
    loginId: user.loginId || '',
    username: user.username || '',
    role: user.role || 'user',
    status: resolveStatus(user),
    resetPassword: false
  }
  editUserModalVisible.value = true
}

const deleteUser = async (user) => {
  if (!user?.id || isAdminAccount(user)) {
    return
  }

  const name = formatDisplayName(user)
  if (!confirm(`${name} 계정을 삭제하시겠습니까?`)) {
    return
  }

  deletingUserId.value = user.id
  error.value = null

  try {
    await authApi.deleteUser(user.id)
    approvalMessage.value = '사용자가 삭제되었습니다.'
    alert('사용자가 삭제되었습니다.')
    if (editForm.value.id === user.id) {
      closeEditModal()
    }
    await loadUsers()
    setTimeout(() => {
      approvalMessage.value = null
    }, 3000)
  } catch (err) {
    error.value = err?.response?.data?.message || '사용자 삭제에 실패했습니다.'
    console.error(err)
  } finally {
    deletingUserId.value = null
  }
}

const closeEditModal = () => {
  editUserModalVisible.value = false
  editForm.value = {
    id: null,
    loginId: '',
    username: '',
    role: 'user',
    status: 'pending',
    resetPassword: false
  }
}

const submitEditUser = async () => {
  if (!editForm.value.id) return

  if (!editForm.value.loginId || !editForm.value.username || !editForm.value.role) {
    error.value = '로그인 아이디, 사용자명, 권한을 모두 입력해주세요.'
    return
  }

  if (!editForm.value.status) {
    error.value = '사용자 상태를 선택해주세요.'
    return
  }

  if (!confirm('사용자를 수정하시겠습니까?')) {
    return
  }

  savingEditUserId.value = editForm.value.id
  error.value = null

  try {
    await authApi.updateUser(editForm.value.id, {
      loginId: editForm.value.loginId,
      username: editForm.value.username,
      role: editForm.value.role,
      status: editForm.value.status,
      newPassword: editForm.value.resetPassword ? DEFAULT_RESET_PASSWORD : null
    })
    approvalMessage.value = editForm.value.resetPassword
      ? '사용자 정보가 수정되고 비밀번호가 초기화되었습니다.'
      : '사용자 정보가 수정되었습니다.'
    alert('사용자가 수정되었습니다.')
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

.create-user-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.create-user-form label {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 13px;
  color: #495057;
  font-weight: 600;
}

.create-user-form input,
.create-user-form select {
  border: 1px solid #d0d7de;
  border-radius: 8px;
  padding: 10px 12px;
  font-size: 14px;
  background: #fff;
}

.create-user-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.create-user-hint {
  display: block;
  color: #6c757d;
  font-size: 12px;
}

.create-user-modal {
  max-width: 480px;
}

.ghost-btn {
  border: 1px solid #d0d7de;
  border-radius: 8px;
  background: #fff;
  color: #495057;
  padding: 8px 14px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}

.ghost-btn:hover:not(:disabled) {
  background: #f8f9fa;
}

.ghost {
  border: 1px solid #ced4da;
  border-radius: 10px;
  padding: 8px 12px;
  font-weight: 700;
  cursor: pointer;
  background: #ffffff;
  color: #6c757d;
  white-space: nowrap;
  word-break: keep-all;
  flex: 0 0 auto;
}
.ghost:hover {
  background: #f1f3f5;
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
  border: 1px solid #dbe4f0;
  border-radius: 14px;
  background: #fff;
  box-shadow: 0 10px 26px rgba(15, 23, 42, 0.05);
}

table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  font-size: 14px;
}

thead {
  background: linear-gradient(180deg, #f8fbff 0%, #eef4fb 100%);
}

th {
  padding: 14px 12px;
  text-align: center;
  font-weight: 600;
  color: #334155;
  border-bottom: 1px solid #dbe4f0;
  border-right: 1px solid #e5edf6;
  white-space: nowrap;
}

tbody tr {
  background: #fff;
}

tbody tr:hover {
  background: #f8fbff;
}

td {
  padding: 14px 12px;
  text-align: center;
  vertical-align: middle;
  border-bottom: 1px solid #edf2f7;
  border-right: 1px solid #f0f4f8;
}

th:last-child,
td:last-child {
  border-right: none;
}

tbody tr:last-child td {
  border-bottom: none;
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

.status-rejected {
  background: #fde2e1;
  color: #a12622;
}

.status-deleted {
  background: #ebedf0;
  color: #4f5b67;
}

.actions {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 8px;
  min-width: 140px;
}

.action-placeholder {
  color: #98a2ad;
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
  background: #ffffff;
  color: #4c6ef5;
  border: 1px solid #4c6ef5;
}

.btn-edit:hover:not(:disabled) {
  background: #ffffff;
  color: #3b5bdb;
  border-color: #3b5bdb;
}

.btn-delete {
  background: #ffffff;
  color: #c92a2a;
  border: 1px solid #f1aeb5;
}

.btn-delete:hover:not(:disabled) {
  background: #ffffff;
  color: #a61e1e;
  border-color: #e98b95;
}

.btn-create {
  color: #1f5aa8;
  border: 1px solid #a9c6f3;
  border-radius: 10px;
  padding: 8px 12px;
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.1px;
  background: #ffffff;
  box-shadow: none;
  transition: transform 0.14s ease, border-color 0.18s ease, opacity 0.15s;
}

.btn-create:hover:not(:disabled) {
  border-color: #8db4eb;
  background: #ffffff;
  box-shadow: none;
  transform: translateY(-1px);
  opacity: 1;
}

.btn-create:active:not(:disabled) {
  transform: translateY(0);
  box-shadow: none;
}

.btn-create:focus-visible {
  outline: none;
  box-shadow: 0 0 0 3px rgba(13, 110, 253, 0.14);
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

.edit-inline-checkbox {
  display: flex !important;
  flex-direction: row !important;
  align-items: center;
  gap: 8px;
  font-weight: 600;
}

@media (max-width: 720px) {
  .create-user-actions {
    flex-direction: column;
  }
}

.edit-inline-checkbox input {
  width: 16px;
  height: 16px;
}

.reset-password-hint {
  margin-top: -4px;
  color: #6c757d;
  font-size: 12px;
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
    flex-direction: row;
    align-items: center;
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

<script setup>
import { ref, watch } from 'vue'

const props = defineProps({
  customer: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['add-customer', 'update-customer', 'close'])

const isEditing = ref(false)
const form = ref({
  name: '',
  phoneNumber: '',
  email: '',
  position: '',
  notes: '',
  status: 'Active',
  company: ''
})

const resetForm = () => {
  form.value = {
    name: '',
    phoneNumber: '',
    email: '',
    position: '',
    notes: '',
    status: 'Active',
    company: ''
  }
}

watch(
  () => props.customer,
  (next) => {
    if (next) {
      form.value = {
        ...next,
        company: next.company ?? ''
      }
      isEditing.value = true
    } else {
      resetForm()
      isEditing.value = false
    }
  },
  { immediate: true }
)

const submitForm = () => {
  if (!form.value.name.trim()) {
    alert('업체명을 입력해 주세요.')
    return
  }

  if (isEditing.value) {
    emit('update-customer', form.value.id, form.value)
  } else {
    emit('add-customer', { ...form.value })
  }
}
</script>

<template>
  <div class="customer-form">
    <h2>{{ isEditing ? '업체 수정' : '업체 추가' }}</h2>

    <form @submit.prevent="submitForm" class="form">
      <div class="form-group">
        <label>업체명 *</label>
        <input v-model="form.name" type="text" placeholder="업체명을 입력하세요" required />
      </div>

      <div class="form-row">
        <div class="form-group">
          <label>전화번호</label>
          <input v-model="form.phoneNumber" type="tel" placeholder="대표번호 또는 담당자 번호" />
        </div>

        <div class="form-group">
          <label>이메일</label>
          <input v-model="form.email" type="email" placeholder="contact@company.com" />
        </div>
      </div>

      <div class="form-group">
        <label>담당자</label>
        <input v-model="form.position" type="text" placeholder="예: 김대리 / 영업팀" />
      </div>

      <div class="form-group">
        <label>상태</label>
        <select v-model="form.status">
          <option value="Lead">리드</option>
          <option value="Active">진행중</option>
          <option value="Inactive">보류</option>
        </select>
      </div>

      <div class="form-group">
        <label>메모</label>
        <textarea v-model="form.notes" rows="3" placeholder="업체 특이사항, 요청사항 등" />
      </div>

      <div class="form-actions">
        <button type="submit" class="btn btn-submit">{{ isEditing ? '수정 저장' : '업체 등록' }}</button>
        <button type="button" class="btn btn-cancel" @click="$emit('close')">닫기</button>
      </div>
    </form>
  </div>
</template>

<style scoped>
.customer-form h2 {
  margin: 0 0 14px;
}

.form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

label {
  font-weight: 600;
}

input,
select,
textarea {
  border: 1px solid #d8d8d8;
  border-radius: 6px;
  padding: 10px;
}

input:focus,
select:focus,
textarea:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.15);
}

.form-actions {
  display: flex;
  gap: 10px;
}

.btn {
  flex: 1;
  border: none;
  border-radius: 6px;
  padding: 11px;
  font-weight: 700;
  cursor: pointer;
}

.btn-submit {
  background: #667eea;
  color: white;
}

.btn-cancel {
  background: #95a5a6;
  color: white;
}

@media (max-width: 700px) {
  .form-row {
    grid-template-columns: 1fr;
  }
}
</style>

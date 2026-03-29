<script setup>
const props = defineProps({
  customers: {
    type: Array,
    required: true
  },
  selectedCustomer: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['select-customer', 'edit-customer', 'delete-customer'])

const formatDate = (value) => {
  if (!value) return '기록 없음'

  return new Date(value).toLocaleDateString('ko-KR', {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  })
}

const statusText = {
  Lead: '리드',
  Active: '진행중',
  Inactive: '보류'
}
</script>

<template>
  <div class="customer-list">
    <div v-if="customers.length === 0" class="empty">
      등록된 업체가 없습니다. 업체를 먼저 추가해 주세요.
    </div>

    <div v-else class="customers">
      <article
        v-for="company in customers"
        :key="company.id"
        class="customer-card"
        :class="{ selected: selectedCustomer?.id === company.id }"
        @click="emit('select-customer', company)"
      >
        <div class="top-row">
          <h3>{{ company.name }}</h3>
          <span class="status">{{ statusText[company.status] || company.status }}</span>
        </div>

        <p v-if="company.position" class="meta">담당자: {{ company.position }}</p>
        <p v-if="company.phoneNumber" class="meta">전화: {{ company.phoneNumber }}</p>
        <p v-if="company.email" class="meta">이메일: {{ company.email }}</p>
        <p class="meta">최근 상담: {{ formatDate(company.lastContactDate) }}</p>

        <div class="actions">
          <button class="btn-edit" @click.stop="emit('edit-customer', company)">수정</button>
          <button class="btn-delete" @click.stop="emit('delete-customer', company.id)">삭제</button>
        </div>
      </article>
    </div>
  </div>
</template>

<style scoped>
.empty {
  color: #777;
  text-align: center;
  padding: 20px;
}

.customers {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.customer-card {
  border: 2px solid #ececec;
  border-radius: 8px;
  padding: 14px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.customer-card:hover {
  border-color: #667eea;
  background: #f6f8ff;
}

.customer-card.selected {
  border-color: #667eea;
  background: #eef2ff;
}

.top-row {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: center;
  margin-bottom: 8px;
}

.top-row h3 {
  margin: 0;
}

.status {
  font-size: 12px;
  border-radius: 20px;
  padding: 4px 8px;
  background: #667eea;
  color: white;
}

.meta {
  margin: 2px 0;
  color: #555;
  font-size: 14px;
}

.actions {
  display: flex;
  gap: 8px;
  margin-top: 10px;
}

button {
  border: none;
  border-radius: 6px;
  padding: 7px 10px;
  cursor: pointer;
}

.btn-edit {
  background: #dcefff;
  color: #1d6fb8;
}

.btn-delete {
  background: #ffe2e2;
  color: #b73a3a;
}
</style>

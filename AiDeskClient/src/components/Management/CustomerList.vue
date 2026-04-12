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
  color: #6c757d;
  text-align: center;
  padding: 22px;
  border: 1px dashed #ced4da;
  border-radius: 10px;
  background: #f8f9fa;
}

.customers {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.customer-card {
  border: 1px solid #dee2e6;
  border-radius: 10px;
  padding: 14px;
  cursor: pointer;
  background: #fff;
  transition: all 0.2s ease;
}

.customer-card:hover {
  border-color: #86b7fe;
  box-shadow: 0 0.25rem 0.6rem rgba(13, 110, 253, 0.12);
}

.customer-card.selected {
  border-color: #0d6efd;
  background: #eef5ff;
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
  background: #0d6efd;
  color: white;
  font-weight: 700;
}

.meta {
  margin: 2px 0;
  color: #6c757d;
  font-size: 14px;
}

.actions {
  display: flex;
  gap: 8px;
  margin-top: 10px;
}

button {
  border: 1px solid transparent;
  border-radius: 8px;
  padding: 7px 10px;
  cursor: pointer;
  font-weight: 600;
}

.btn-edit {
  background: #ffffff;
  color: #0d6efd;
  border-color: #0d6efd;
}

.btn-edit:hover {
  background: #e7f1ff;
}

.btn-delete {
  background: #ffffff;
  color: #dc3545;
  border-color: #dc3545;
}

.btn-delete:hover {
  background: #fdecef;
}
</style>

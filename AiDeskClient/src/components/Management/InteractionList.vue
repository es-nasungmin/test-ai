<script setup>
const props = defineProps({
  interactions: {
    type: Array,
    required: true
  }
})

const emit = defineEmits(['edit-interaction', 'delete-interaction'])

const typeEmojis = {
  Call: '📞',
  Email: '📧',
  Meeting: '🤝',
  Note: '📝'
}

const formatDate = (dateString) => {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleDateString('ko-KR', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  })
}
</script>

<template>
  <div class="interaction-list">
    <div v-if="interactions.length === 0" class="empty">
      <p>No interactions logged yet</p>
    </div>

    <div v-else class="interactions">
      <div
        v-for="interaction in interactions"
        :key="interaction.id"
        class="interaction-card"
        :class="{ completed: interaction.isCompleted }"
      >
        <div class="interaction-header">
          <div class="type-badge">
            {{ typeEmojis[interaction.type] || '📝' }} {{ interaction.type }}
          </div>
          <div class="header-right">
            <div class="date">{{ formatDate(interaction.createdAt) }}</div>
          </div>
        </div>

        <div class="interaction-content">
          {{ interaction.content }}
        </div>

        <div class="interaction-actions">
          <button
            class="btn-small btn-edit"
            @click="emit('edit-interaction', interaction)"
            title="Edit"
          >
            ✏️ 수정
          </button>
          <button
            class="btn-small btn-delete"
            @click="emit('delete-interaction', interaction.id)"
            title="Delete"
          >
            🗑️ 삭제
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.interaction-list {
  width: 100%;
}

.empty {
  text-align: center;
  padding: 40px 20px;
  color: #6c757d;
}

.interactions {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.interaction-card {
  padding: 15px;
  border: 1px solid #dee2e6;
  border-left: 4px solid #0d6efd;
  border-radius: 8px;
  background-color: #ffffff;
  transition: all 0.3s ease;
}

.interaction-card:hover {
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  transform: translateX(2px);
}

.interaction-card.completed {
  border-left-color: #198754;
  background-color: #f8fcf9;
  opacity: 0.8;
}

.interaction-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.type-badge {
  display: inline-block;
  padding: 4px 12px;
  background-color: #e7f1ff;
  color: #0d6efd;
  border-radius: 20px;
  font-weight: 600;
  font-size: 0.9em;
}

.date {
  font-size: 0.85em;
  color: #6c757d;
}

.interaction-content {
  font-size: 0.95em;
  color: #495057;
  line-height: 1.5;
  margin-bottom: 8px;
  white-space: pre-wrap;
  word-break: break-word;
}

.interaction-actions {
  display: flex;
  gap: 8px;
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid #e0e0e0;
}

.btn-small {
  padding: 6px 12px;
  border: 1px solid transparent;
  border-radius: 8px;
  cursor: pointer;
  font-size: 0.9em;
  transition: all 0.2s ease;
  flex: 1;
}

.btn-edit {
  background-color: #ffffff;
  color: #0d6efd;
  border-color: #0d6efd;
}

.btn-edit:hover {
  background-color: #e7f1ff;
}

.btn-delete {
  background-color: #ffffff;
  color: #dc3545;
  border-color: #dc3545;
}

.btn-delete:hover {
  background-color: #fdecef;
}
</style>

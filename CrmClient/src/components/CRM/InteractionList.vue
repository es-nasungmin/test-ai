<script setup>
const props = defineProps({
  interactions: {
    type: Array,
    required: true
  }
})

const emit = defineEmits(['edit-interaction', 'delete-interaction', 'toggle-external'])

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
            <span
              class="external-badge"
              :class="interaction.isExternalProvided ? 'on' : 'off'"
            >
              {{ interaction.isExternalProvided ? '외부제공 ON' : '외부제공 OFF' }}
            </span>
            <div class="date">{{ formatDate(interaction.createdAt) }}</div>
          </div>
        </div>

        <div class="interaction-content">
          {{ interaction.content }}
        </div>

        <div class="interaction-actions">
          <button
            class="btn-small btn-external"
            @click="emit('toggle-external', interaction)"
          >
            {{ interaction.isExternalProvided ? '🔒 내부만' : '🌐 외부제공' }}
          </button>
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
  color: #999;
}

.interactions {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.interaction-card {
  padding: 15px;
  border-left: 4px solid #667eea;
  border-radius: 4px;
  background-color: #f9f9ff;
  transition: all 0.3s ease;
}

.interaction-card:hover {
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  transform: translateX(2px);
}

.interaction-card.completed {
  border-left-color: #27ae60;
  background-color: #f0f8f0;
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

.external-badge {
  font-size: 0.72em;
  padding: 2px 8px;
  border-radius: 999px;
  font-weight: 700;
}

.external-badge.on {
  background: #d1fae5;
  color: #065f46;
}

.external-badge.off {
  background: #f1f5f9;
  color: #475569;
}

.type-badge {
  display: inline-block;
  padding: 4px 12px;
  background-color: #e8f4f8;
  color: #0084d0;
  border-radius: 20px;
  font-weight: 600;
  font-size: 0.9em;
}

.date {
  font-size: 0.85em;
  color: #999;
}

.interaction-content {
  font-size: 0.95em;
  color: #333;
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
  border: none;
  border-radius: 3px;
  cursor: pointer;
  font-size: 0.9em;
  transition: all 0.2s ease;
  flex: 1;
}

.btn-edit {
  background-color: #d4e9f7;
  color: #1e5ba8;
}

.btn-external {
  background-color: #e0f2fe;
  color: #0369a1;
}

.btn-external:hover {
  background-color: #bae6fd;
}

.btn-edit:hover {
  background-color: #b3d9f2;
}

.btn-delete {
  background-color: #ffe8e8;
  color: #e74c3c;
}

.btn-delete:hover {
  background-color: #ffd0d0;
}
</style>

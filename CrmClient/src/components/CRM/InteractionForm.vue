<script setup>
import { ref, watch } from 'vue'
import axios from 'axios'

const API_URL = 'http://localhost:8080/api'

const props = defineProps({
  customer: {
    type: Object,
    required: true
  },
  consultationTypes: {
    type: Array,
    default: () => ['Call', 'Email', 'Meeting', 'Note']
  },
  provider: {
    type: String,
    default: 'gpt'
  }
})

const emit = defineEmits(['add-interaction', 'open-type-editor'])

const getDefaultType = () => props.consultationTypes[0] || 'Call'

const form = ref({
  type: getDefaultType(),
  content: '',
  isExternalProvided: false
})

const summarizing = ref(false)
const summarizedContent = ref('')

watch(
  () => props.customer?.id,
  () => {
    form.value = {
      type: getDefaultType(),
      content: '',
      isExternalProvided: false
    }
    summarizedContent.value = ''
  }
)

watch(
  () => props.consultationTypes,
  (types) => {
    if (!types.includes(form.value.type)) {
      form.value.type = getDefaultType()
    }
  },
  { deep: true }
)

const summarizeContent = async () => {
  if (!form.value.content.trim()) {
    alert('상담 내용을 입력해 주세요.')
    return
  }

  summarizing.value = true
  try {
    const response = await axios.post(`${API_URL}/interaction/summarize`, {
      content: form.value.content,
      type: form.value.type,
      provider: props.provider
    })
    summarizedContent.value = response.data.summary
  } catch (error) {
    alert('내용 정리 중 오류가 발생했습니다: ' + error.response?.data?.error || error.message)
    console.error(error)
  } finally {
    summarizing.value = false
  }
}

const useSummarized = () => {
  if (summarizedContent.value) {
    form.value.content = summarizedContent.value
    summarizedContent.value = ''
  }
}

const submitForm = () => {
  if (!props.customer?.id) {
    alert('업체를 먼저 선택해 주세요.')
    return
  }

  if (!form.value.content.trim()) {
    alert('상담 내용을 입력해 주세요.')
    return
  }

  emit('add-interaction', {
    customerId: props.customer.id,
    type: form.value.type,
    content: form.value.content,
    outcome: null,
    scheduledDate: null,
    isCompleted: false,
    isExternalProvided: form.value.isExternalProvided
  })

  form.value = {
    type: getDefaultType(),
    content: '',
    isExternalProvided: false
  }
  summarizedContent.value = ''
}
</script>

<template>
  <form class="interaction-form" @submit.prevent="submitForm">
    <div class="form-group">
      <div class="field-header">
        <label>상담 유형</label>
        <button
          class="btn-type-mini"
          type="button"
          title="상담유형 관리"
          @click="emit('open-type-editor')"
        >
          관리
        </button>
      </div>
      <select v-model="form.type">
        <option
          v-for="type in consultationTypes"
          :key="type"
          :value="type"
        >
          {{ type }}
        </option>
      </select>
    </div>

    <div class="form-group">
      <label>상담 내용</label>
      <textarea
        v-model="form.content"
        rows="5"
        placeholder="통화 내용과 결과를 함께 기록하세요."
      />
    </div>

    <div class="form-group checkbox-group">
      <label class="checkbox-label">
        <input type="checkbox" v-model="form.isExternalProvided" />
        외부 제공 가능 (고객 챗봇에도 노출)
      </label>
    </div>

    <div class="form-actions">
      <button class="btn-submit" type="submit">상담 저장</button>
      <button
        class="btn-summarize"
        type="button"
        @click="summarizeContent"
        :disabled="summarizing || !form.content.trim()"
      >
        {{ summarizing ? '정리 중...' : '내용정리' }}
      </button>
    </div>

    <div v-if="summarizedContent" class="summarized-section">
      <div class="summarized-title">정리된 내용</div>
      <div class="summarized-content">
        {{ summarizedContent }}
      </div>
      <div class="summarized-actions">
        <button class="btn-use-summary" type="button" @click="useSummarized">
          적용
        </button>
        <button
          class="btn-clear-summary"
          type="button"
          @click="summarizedContent = ''"
        >
          닫기
        </button>
      </div>
    </div>
  </form>
</template>

<style scoped>
.interaction-form {
  padding: 14px;
  border: 1px solid #e7e7e7;
  border-radius: 8px;
  background: #fafbff;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 10px;
}

.checkbox-group {
  margin-top: -2px;
}

.checkbox-label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  color: #2f2f2f;
}

.checkbox-label input {
  width: 16px;
  height: 16px;
}

.field-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

label {
  font-weight: 600;
  color: #2f2f2f;
}

input,
select,
textarea {
  border: 1px solid #d8d8d8;
  border-radius: 6px;
  padding: 10px;
  font-size: 14px;
}

input:focus,
select:focus,
textarea:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.15);
}

.btn-submit {
  flex: 1;
  border: none;
  border-radius: 6px;
  padding: 10px;
  background: #27ae60;
  color: white;
  font-weight: 700;
  cursor: pointer;
  font-size: 0.9em;
  min-width: auto;
}

.btn-submit:hover:not(:disabled) {
  background: #229954;
}

.btn-submit:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.form-actions {
  display: flex;
  gap: 10px;
  margin-top: 8px;
}

.btn-summarize {
  flex: 1;
  border: none;
  border-radius: 6px;
  padding: 10px;
  background: #3498db;
  color: white;
  font-weight: 700;
  cursor: pointer;
  transition: background 0.2s ease;
  font-size: 0.9em;
  min-width: auto;
  height: auto;
}

.btn-summarize:hover:not(:disabled) {
  background: #2980b9;
}

.btn-summarize:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-type-mini {
  border: 1px solid #d8d8d8;
  border-radius: 6px;
  padding: 4px 8px;
  background: #fff;
  color: #4f5f9f;
  font-size: 0.78em;
  font-weight: 700;
  cursor: pointer;
}

.btn-type-mini:hover {
  background: #f3f5ff;
  border-color: #aeb9f6;
}

.summary-actions-group {
  display: none;
}

.summarized-section {
  margin-top: 12px;
  padding: 12px;
  background: #ecf0f1;
  border: 1px solid #bdc3c7;
  border-radius: 6px;
}

.summarized-title {
  font-weight: 700;
  color: #2c3e50;
  margin-bottom: 8px;
  font-size: 0.95em;
}

.summarized-content {
  background: white;
  padding: 12px;
  border-radius: 4px;
  color: #2f2f2f;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 300px;
  overflow-y: auto;
  margin-bottom: 10px;
  font-size: 0.9em;
}

.summarized-actions {
  display: flex;
  gap: 8px;
}

.btn-use-summary {
  flex: 1;
  border: none;
  border-radius: 4px;
  padding: 8px;
  background: #27ae60;
  color: white;
  font-weight: 600;
  cursor: pointer;
  font-size: 0.9em;
}

.btn-use-summary:hover {
  background: #229954;
}

.btn-clear-summary {
  flex: 1;
  border: none;
  border-radius: 4px;
  padding: 8px;
  background: #95a5a6;
  color: white;
  font-weight: 600;
  cursor: pointer;
  font-size: 0.9em;
}

.btn-clear-summary:hover {
  background: #7f8c8d;
}

</style>

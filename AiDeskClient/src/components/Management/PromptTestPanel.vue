<script setup>
import { onMounted, ref } from 'vue'
import axios from 'axios'
import { API_BASE_URL } from '../../config'

const API_URL = API_BASE_URL

const question = ref('')
const systemPrompt = ref('')
const rulesPrompt = ref('')

const loadingTemplate = ref(false)
const asking = ref(false)
const error = ref('')

const resultAnswer = ref('')

const template = ref({
  adminSystemPrompt: '',
  adminRulesPrompt: ''
})

const applyDefaultPrompts = () => {
  systemPrompt.value = template.value.adminSystemPrompt
  rulesPrompt.value = template.value.adminRulesPrompt
}

const loadTemplate = async () => {
  loadingTemplate.value = true
  error.value = ''
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/chatbot-prompt-template`)
    template.value = {
      adminSystemPrompt: res.data.adminSystemPrompt || '',
      adminRulesPrompt: res.data.adminRulesPrompt || ''
    }
    applyDefaultPrompts()
  } catch (err) {
    error.value = '기본 프롬프트를 불러오지 못했습니다.'
    console.error(err)
  } finally {
    loadingTemplate.value = false
  }
}

const askWithTemporaryPrompt = async () => {
  if (!question.value.trim()) {
    error.value = '질문을 입력해주세요.'
    return
  }
  if (!systemPrompt.value.trim() || !rulesPrompt.value.trim()) {
    error.value = '프롬프트 항목을 모두 입력해주세요.'
    return
  }

  asking.value = true
  error.value = ''
  resultAnswer.value = ''

  try {
    const res = await axios.post(`${API_URL}/knowledgebase/ask`, {
      question: question.value.trim(),
      noSave: true,
      createSession: false,
      promptOverride: {
        promptOnly: true,
        systemPrompt: systemPrompt.value,
        rulesPrompt: rulesPrompt.value
      }
    })

    resultAnswer.value = res.data.answer || ''
  } catch (err) {
    error.value = err.response?.data?.error || err.message || '테스트 요청에 실패했습니다.'
    console.error(err)
  } finally {
    asking.value = false
  }
}

onMounted(async () => {
  await loadTemplate()
})
</script>

<template>
  <section class="prompt-test-wrap">
    <div class="prompt-test-header">
      <h2>프롬프트 실험실</h2>
      <p>입력한 프롬프트와 규칙으로 질문 답변을 바로 확인합니다.</p>
    </div>

    <div v-if="error" class="error-box">
      {{ error }}
    </div>

    <div class="prompt-test-grid">
      <div class="test-card">
        <div class="field">
          <div class="prompt-head">
            <label>시스템 프롬프트</label>
            <button class="btn light" type="button" @click="applyDefaultPrompts" :disabled="loadingTemplate">
              기본값 다시 불러오기
            </button>
          </div>
          <textarea v-model="systemPrompt" rows="6" />
        </div>

        <div class="field">
          <label>답변 규칙 프롬프트</label>
          <textarea v-model="rulesPrompt" rows="6" />
        </div>

        <div class="field">
          <label>질문</label>
          <textarea v-model="question" rows="3" placeholder="예: 계산서 발행이 누락됐다고 문의가 왔어요. 어떻게 안내하지?" />
        </div>

        <div class="actions">
          <button class="btn primary" type="button" @click="askWithTemporaryPrompt" :disabled="asking || loadingTemplate">
            {{ asking ? '응답 생성 중...' : '답변 확인' }}
          </button>
        </div>
      </div>

      <div class="test-card result-card">
        <h3>답변</h3>

        <div v-if="resultAnswer" class="answer-box">
          <pre>{{ resultAnswer }}</pre>
        </div>
        <div v-else class="empty-result">아직 테스트 결과가 없습니다.</div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.prompt-test-wrap {
  max-width: 1200px;
  margin: 0 auto;
}

.prompt-test-header {
  margin-bottom: 12px;
}

.prompt-test-header h2 {
  margin: 0 0 6px;
}

.prompt-test-header p {
  margin: 0;
  color: #6c757d;
}

.error-box {
  margin-bottom: 12px;
  border: 1px solid #f5c6cb;
  background: #f8d7da;
  color: #721c24;
  border-radius: 8px;
  padding: 10px 12px;
}

.prompt-test-grid {
  display: grid;
  grid-template-columns: 1.5fr 1fr;
  gap: 14px;
}

.prompt-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.test-card {
  background: #ffffff;
  border: 1px solid #dee2e6;
  border-radius: 12px;
  padding: 16px;
  box-shadow: 0 0.35rem 0.8rem rgba(0, 0, 0, 0.06);
}

.row-inline {
  display: flex;
  gap: 10px;
  align-items: end;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 12px;
  min-width: 0;
}

.field.grow {
  flex: 1;
}

.threshold-field {
  width: 130px;
}

.field label {
  font-size: 0.9em;
  color: #495057;
  font-weight: 700;
}

.field textarea,
.field select,
.field input {
  border: 1px solid #ced4da;
  border-radius: 8px;
  padding: 10px;
  font-size: 0.92em;
  font-family: inherit;
}

.actions {
  margin-top: 6px;
}

.btn {
  border: 1px solid transparent;
  border-radius: 10px;
  padding: 9px 12px;
  cursor: pointer;
  font-weight: 700;
}

.btn.primary {
  width: 100%;
  background: #0d6efd;
  border-color: #0d6efd;
  color: #ffffff;
}

.btn.primary:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.btn.light {
  background: #ffffff;
  border-color: #ced4da;
  color: #495057;
  white-space: nowrap;
  height: fit-content;
}

.result-card h3 {
  margin: 0 0 10px;
}

.answer-box {
  border: 1px solid #e9ecef;
  border-radius: 8px;
  background: #f8f9fa;
  padding: 12px;
  margin-bottom: 12px;
}

.answer-box pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
}

.empty-result {
  border: 1px dashed #ced4da;
  border-radius: 8px;
  padding: 14px;
  color: #6c757d;
  text-align: center;
}

@media (max-width: 1024px) {
  .prompt-test-grid {
    grid-template-columns: 1fr;
  }

  .row-inline {
    flex-wrap: wrap;
  }
}
</style>

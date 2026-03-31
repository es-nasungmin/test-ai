<script setup>
import { computed, onMounted, ref } from 'vue'
import axios from 'axios'
import CustomerList from '../components/CRM/CustomerList.vue'
import CustomerForm from '../components/CRM/CustomerForm.vue'
import InteractionForm from '../components/CRM/InteractionForm.vue'
import InteractionList from '../components/CRM/InteractionList.vue'
import FloatingChatbot from '../components/FloatingChatbot.vue'
import KBManagement from '../components/CRM/KBManagement.vue'

const API_URL = 'http://localhost:8080/api'

const companies = ref([])
const consultations = ref([])
const selectedCompany = ref(null)
const loading = ref(false)
const error = ref(null)

const showCompanyForm = ref(false)
const editingCompany = ref(null)

const showEditInteractionModal = ref(false)
const editingInteraction = ref(null)

const showAllSummary = ref(false)
const allConsultationsSummary = ref('')
const summarizingAllConsultations = ref(false)
const summaryProvider = ref('gpt')

const defaultConsultationTypes = ['Call', 'Email', 'Meeting', 'Note']
const consultationTypes = ref([...defaultConsultationTypes])
const showTypeEditor = ref(false)
const typeEditorItems = ref([])

const showPromptEditor = ref(false)
const savingPromptTemplate = ref(false)
const promptTemplateForm = ref({
  singleConsultationTemplate: '',
  allConsultationsTemplate: ''
})

const recommendedSinglePrompt = `당신은 CRM 상담 정리 도우미입니다.
아래 상담 내용을 한국어로 간결하고 실무적으로 정리하세요.

[출력 형식]
1) 핵심 이슈 (1~2줄)
2) 고객 요청사항 (불릿)
3) 현재 상태/원인 (불릿)
4) 처리 사항 (불릿)
5) 후속 액션 (담당/기한이 있으면 함께)

[규칙]
- 사실 기반으로만 작성하고 추측은 금지합니다.
- 불필요한 수식어 없이 명확하게 작성합니다.
- 내용이 없는 항목은 출력하지 않습니다.
- 하나의 상담에 문의가 2개 이상이면 문의 항목을 분리해 각각 작성합니다.
- 각 문의 항목마다 원인/안내/처리 결과를 따로 정리합니다.

상담 유형: {type}
상담 내용:
{content}`

const recommendedAllPrompt = `당신은 CRM 이력 분석 도우미입니다.
아래 고객의 전체 상담 이력을 종합해 한국어로 보고서 형태로 요약하세요.

[출력 형식]
1) 고객 요약 (업종/상황/핵심 관심사)
2) 이력 타임라인 (시간순 핵심 사건 3~7개)
3) 반복 이슈 및 패턴
4) 미해결 과제/리스크
5) 다음 액션 제안 (우선순위 High/Medium/Low)

[규칙]
- 상담 기록에 있는 사실만 사용합니다.
- 중복 내용은 합쳐서 간결히 작성합니다.
- 실행 가능한 액션 중심으로 작성합니다.

업체명: {companyName}
상담 이력:
{consultationText}`

promptTemplateForm.value = {
  singleConsultationTemplate: recommendedSinglePrompt,
  allConsultationsTemplate: recommendedAllPrompt
}

// KB 분석 상태 토스트
const kbToasts = ref([]) // { id, step, message, status: 'saving'|'analyzing'|'done'|'error' }

function addKbToast() {
  const id = Date.now()
  kbToasts.value.push({ id, message: '💾 상담 저장 중...', status: 'saving' })

  setTimeout(() => {
    const t = kbToasts.value.find(t => t.id === id)
    if (t) {
      t.message = '🔄 AI가 KB를 분석하고 있습니다...'
      t.status = 'analyzing'
    }
  }, 800)

  setTimeout(() => {
    const t = kbToasts.value.find(t => t.id === id)
    if (t) {
      t.message = '🧠 KB 분석 완료! 챗봇에서 바로 사용 가능합니다.'
      t.status = 'done'
    }
    setTimeout(() => {
      kbToasts.value = kbToasts.value.filter(t => t.id !== id)
    }, 4000)
  }, 5000)

  return id
}

const filteredConsultations = computed(() => {
  if (!selectedCompany.value) {
    return []
  }

  return consultations.value
    .filter(item => item.customerId === selectedCompany.value.id)
    .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))
})

const fetchCompanies = async () => {
  loading.value = true
  error.value = null

  try {
    const response = await axios.get(`${API_URL}/customer`)
    companies.value = response.data

    if (selectedCompany.value) {
      const stillExists = response.data.find(item => item.id === selectedCompany.value.id)
      if (stillExists) {
        selectedCompany.value = stillExists
      } else {
        selectedCompany.value = null
      }
    }
  } catch (err) {
    error.value = '업체 목록을 불러오지 못했습니다.'
    console.error(err)
  } finally {
    loading.value = false
  }
}

const fetchConsultations = async () => {
  try {
    const response = await axios.get(`${API_URL}/interaction`)
    consultations.value = response.data
  } catch (err) {
    error.value = '상담 기록을 불러오지 못했습니다.'
    console.error(err)
  }
}

const addCompany = async (newCompany) => {
  try {
    const response = await axios.post(`${API_URL}/customer`, newCompany)
    companies.value.unshift(response.data)
    selectedCompany.value = response.data
    showCompanyForm.value = false
  } catch (err) {
    error.value = '업체 등록에 실패했습니다.'
    console.error(err)
  }
}

const updateCompany = async (id, updatedCompany) => {
  try {
    await axios.put(`${API_URL}/customer/${id}`, updatedCompany)
    await fetchCompanies()
    showCompanyForm.value = false
    editingCompany.value = null
  } catch (err) {
    error.value = '업체 수정에 실패했습니다.'
    console.error(err)
  }
}

const deleteCompany = async (id) => {
  if (!confirm('해당 업체와 상담 기록이 모두 삭제됩니다. 계속할까요?')) {
    return
  }

  try {
    await axios.delete(`${API_URL}/customer/${id}`)
    await fetchCompanies()
    await fetchConsultations()

    if (selectedCompany.value?.id === id) {
      selectedCompany.value = null
    }
  } catch (err) {
    error.value = '업체 삭제에 실패했습니다.'
    console.error(err)
  }
}

const addConsultation = async (newConsultation) => {
  try {
    addKbToast()
    await axios.post(`${API_URL}/interaction`, newConsultation)
    await fetchConsultations()
    await fetchCompanies()
  } catch (err) {
    error.value = '상담 내용 저장에 실패했습니다.'
    console.error(err)
  }
}

const completeConsultation = async (id) => {
  try {
    await axios.patch(`${API_URL}/interaction/${id}/complete`)
    await fetchConsultations()
  } catch (err) {
    error.value = '상담 상태 변경에 실패했습니다.'
    console.error(err)
  }
}

const deleteConsultation = async (id) => {
  try {
    await axios.delete(`${API_URL}/interaction/${id}`)
    await fetchConsultations()
  } catch (err) {
    error.value = '상담 삭제에 실패했습니다.'
    console.error(err)
  }
}

const editConsultation = (interaction) => {
  if (!consultationTypes.value.includes(interaction.type)) {
    interaction.type = consultationTypes.value[0] || 'Call'
  }
  editingInteraction.value = interaction
  showEditInteractionModal.value = true
}

const updateConsultation = async (updatedInteraction) => {
  try {
    await axios.put(`${API_URL}/interaction/${updatedInteraction.id}`, updatedInteraction)
    await fetchConsultations()
    await fetchCompanies()
    showEditInteractionModal.value = false
    editingInteraction.value = null
  } catch (err) {
    error.value = '상담 수정에 실패했습니다.'
    console.error(err)
  }
}

const toggleExternalProvide = async (interaction) => {
  try {
    const nextValue = !interaction.isExternalProvided
    await axios.patch(`${API_URL}/interaction/${interaction.id}/external-provide`, {
      isExternalProvided: nextValue
    })
    interaction.isExternalProvided = nextValue
    await fetchConsultations()
  } catch (err) {
    error.value = '외부제공 상태 변경에 실패했습니다.'
    console.error(err)
  }
}

const openAddCompanyForm = () => {
  editingCompany.value = null
  showCompanyForm.value = true
}

const loadConsultationTypes = () => {
  const raw = localStorage.getItem('crm-consultation-types')

  if (!raw) {
    consultationTypes.value = [...defaultConsultationTypes]
    return
  }

  try {
    const parsed = JSON.parse(raw)
    if (!Array.isArray(parsed)) {
      consultationTypes.value = [...defaultConsultationTypes]
      return
    }

    const sanitized = parsed
      .map(item => (typeof item === 'string' ? item.trim() : ''))
      .filter(Boolean)

    consultationTypes.value = sanitized.length > 0 ? sanitized : [...defaultConsultationTypes]
  } catch {
    consultationTypes.value = [...defaultConsultationTypes]
  }
}

const openTypeEditor = () => {
  typeEditorItems.value = consultationTypes.value.map((name, index) => ({
    id: `${Date.now()}-${index}`,
    name
  }))
  showTypeEditor.value = true
}

const addTypeItem = () => {
  typeEditorItems.value.push({
    id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    name: ''
  })
}

const removeTypeItem = (id) => {
  typeEditorItems.value = typeEditorItems.value.filter(item => item.id !== id)
}

const saveConsultationTypes = () => {
  const unique = []
  const dedupe = new Set()

  for (const item of typeEditorItems.value) {
    const name = item.name.trim()
    if (!name) continue

    const key = name.toLowerCase()
    if (dedupe.has(key)) continue
    dedupe.add(key)
    unique.push(name)
  }

  if (unique.length === 0) {
    alert('상담유형은 최소 1개 이상 필요합니다.')
    return
  }

  consultationTypes.value = unique
  localStorage.setItem('crm-consultation-types', JSON.stringify(unique))

  if (editingInteraction.value && !consultationTypes.value.includes(editingInteraction.value.type)) {
    editingInteraction.value.type = consultationTypes.value[0]
  }

  showTypeEditor.value = false
}


const generateAllSummary = async () => {
  if (!selectedCompany.value) {
    alert('업체를 먼저 선택해 주세요.')
    return
  }

  if (filteredConsultations.value.length === 0) {
    alert('상담 이력이 없습니다.')
    return
  }

  summarizingAllConsultations.value = true
  try {
    const response = await axios.post(
      `${API_URL}/interaction/customer/${selectedCompany.value.id}/summarize-all?provider=${encodeURIComponent(summaryProvider.value)}`
    )
    allConsultationsSummary.value = response.data.summary
    showAllSummary.value = true
  } catch (error) {
    alert('요약 생성 중 오류가 발생했습니다: ' + error.response?.data?.error || error.message)
    console.error(error)
  } finally {
    summarizingAllConsultations.value = false
  }
}
const editCompany = (company) => {
  editingCompany.value = company
  showCompanyForm.value = true
}

const fetchPromptTemplate = async () => {
  try {
    const response = await axios.get(`${API_URL}/interaction/prompt-template`)
    promptTemplateForm.value = {
      singleConsultationTemplate: response.data.singleConsultationTemplate || recommendedSinglePrompt,
      allConsultationsTemplate: response.data.allConsultationsTemplate || recommendedAllPrompt
    }
  } catch (err) {
    error.value = '프롬프트 설정을 불러오지 못했습니다.'
    console.error(err)
  }
}

const openPromptEditor = async () => {
  await fetchPromptTemplate()
  if (!promptTemplateForm.value.singleConsultationTemplate.trim() || !promptTemplateForm.value.allConsultationsTemplate.trim()) {
    applyRecommendedPrompts()
  }
  showPromptEditor.value = true
}

const applyRecommendedPrompts = () => {
  promptTemplateForm.value.singleConsultationTemplate = recommendedSinglePrompt
  promptTemplateForm.value.allConsultationsTemplate = recommendedAllPrompt
}

const resetSinglePromptTemplate = () => {
  promptTemplateForm.value.singleConsultationTemplate = recommendedSinglePrompt
}

const resetAllPromptTemplate = () => {
  promptTemplateForm.value.allConsultationsTemplate = recommendedAllPrompt
}

const savePromptTemplate = async () => {
  if (!promptTemplateForm.value.singleConsultationTemplate.trim() || !promptTemplateForm.value.allConsultationsTemplate.trim()) {
    alert('프롬프트 문구를 비워둘 수 없습니다.')
    return
  }

  savingPromptTemplate.value = true
  try {
    const response = await axios.put(`${API_URL}/interaction/prompt-template`, {
      singleConsultationTemplate: promptTemplateForm.value.singleConsultationTemplate,
      allConsultationsTemplate: promptTemplateForm.value.allConsultationsTemplate
    })

    promptTemplateForm.value = {
      singleConsultationTemplate: response.data.singleConsultationTemplate,
      allConsultationsTemplate: response.data.allConsultationsTemplate
    }

    alert('프롬프트가 저장되었습니다. 다음 요약부터 바로 반영됩니다.')
    showPromptEditor.value = false
  } catch (err) {
    alert('프롬프트 저장에 실패했습니다: ' + (err.response?.data?.error || err.message))
    console.error(err)
  } finally {
    savingPromptTemplate.value = false
  }
}

// ---- 페이지 탭 ----
const activePage = ref('crm')  // 'crm' | 'kb'

onMounted(() => {
  loadConsultationTypes()
  fetchCompanies()
  fetchConsultations()
  fetchPromptTemplate()
})
</script>

<template>
  <div class="crm-container">
    <!-- KB 분석 상태 토스트 -->
    <div class="kb-toast-container">
      <transition-group name="toast">
        <div
          v-for="toast in kbToasts"
          :key="toast.id"
          class="kb-toast"
          :class="toast.status"
        >
          {{ toast.message }}
        </div>
      </transition-group>
    </div>

    <header class="crm-header">
      <h1>CRM 요약 · 정리 · 챗봇 테스트</h1>
      <p>프롬프트를 수정하고 결과를 비교해 보세요.</p>
      <!-- 페이지 탭 -->
      <div class="page-tabs">
        <button
          class="page-tab"
          :class="{ active: activePage === 'crm' }"
          @click="activePage = 'crm'"
        >🗂️ CRM 상담관리</button>
        <button
          class="page-tab"
          :class="{ active: activePage === 'kb' }"
          @click="activePage = 'kb'"
        >📚 KB 관리</button>
      </div>
    </header>

    <div v-if="error" class="error-message">
      {{ error }}
    </div>

    <!-- KB 관리 페이지 -->
    <div v-if="activePage === 'kb'" class="kb-page">
      <KBManagement />
    </div>

    <div v-show="activePage === 'crm'" class="crm-content">
      <section class="left-section">
        <div class="section-header-right">
          <h2>업체 목록</h2>
          <button class="btn btn-summary" @click="openAddCompanyForm">
            업체 추가
          </button>
        </div>

        <div v-if="loading" class="loading">
          로딩 중...
        </div>

        <CustomerList
          v-else
          :customers="companies"
          :selected-customer="selectedCompany"
          @select-customer="selectedCompany = $event"
          @edit-customer="editCompany"
          @delete-customer="deleteCompany"
        />
      </section>

      <section class="right-section">
        <div v-if="selectedCompany" class="consultation-section">
          <div class="section-header-right">
            <h2>{{ selectedCompany.name }} 상담 작성</h2>
            <select v-model="summaryProvider" class="provider-select" title="요약 모델 선택">
              <option value="gpt">GPT</option>
              <option value="gemini">Gemini</option>
            </select>
            <button
              v-if="filteredConsultations.length > 0"
              class="btn btn-summary"
              @click="generateAllSummary"
              :disabled="summarizingAllConsultations"
            >
              {{ summarizingAllConsultations ? '요약 생성 중...' : '요약 보기' }}
            </button>
            <button
              class="btn btn-prompt-mini"
              type="button"
              title="요약 프롬프트 수정"
              @click="openPromptEditor"
            >
              프롬프트
            </button>
          </div>
          <InteractionForm
            :customer="selectedCompany"
            :provider="summaryProvider"
            :consultation-types="consultationTypes"
            @open-type-editor="openTypeEditor"
            @add-interaction="addConsultation"
          />

          <h3 class="list-title">상담 이력</h3>
          <InteractionList
            :interactions="filteredConsultations"
            @edit-interaction="editConsultation"
            @delete-interaction="deleteConsultation"
            @toggle-external="toggleExternalProvide"
          />
        </div>

        <div v-else class="empty-state">
          <p>왼쪽에서 업체를 선택하면 상담 내용을 작성할 수 있습니다.</p>
        </div>
      </section>
    </div>

    <div v-if="showCompanyForm" class="modal-overlay" @click="showCompanyForm = false">
      <div class="modal" @click.stop>
        <CustomerForm
          :customer="editingCompany"
          @add-customer="addCompany"
          @update-customer="updateCompany"
          @close="showCompanyForm = false"
        />
      </div>
    </div>

    <div v-if="showAllSummary" class="modal-overlay" @click="showAllSummary = false">
      <div class="modal modal-summary" @click.stop>
        <div class="summary-header">
          <h3>{{ selectedCompany?.name }} - 전체 상담 요약</h3>
          <button class="btn-close" @click="showAllSummary = false">✕</button>
        </div>
        <div class="summary-content">
          {{ allConsultationsSummary }}
        </div>
        <button class="btn btn-primary" @click="showAllSummary = false">닫기</button>
      </div>
    </div>

    <div v-if="showEditInteractionModal && editingInteraction" class="modal-overlay" @click="showEditInteractionModal = false">
      <div class="modal modal-edit" @click.stop>
        <div class="modal-header">
          <h3>상담 내용 수정</h3>
          <button class="btn-close" @click="showEditInteractionModal = false">✕</button>
        </div>
        <div class="edit-form">
          <div class="form-group">
            <label>상담 유형</label>
            <select v-model="editingInteraction.type">
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
              v-model="editingInteraction.content"
              rows="6"
            />
          </div>
          <div class="form-group checkbox-group">
            <label class="checkbox-label">
              <input type="checkbox" v-model="editingInteraction.isExternalProvided" />
              외부 제공 가능 (고객 챗봇 노출)
            </label>
          </div>
          <div class="modal-actions">
            <button class="btn btn-primary" @click="updateConsultation(editingInteraction)">
              저장
            </button>
            <button class="btn btn-secondary" @click="showEditInteractionModal = false">
              취소
            </button>
          </div>
        </div>
      </div>
    </div>

    <div v-if="showPromptEditor" class="modal-overlay" @click="showPromptEditor = false">
      <div class="modal modal-prompt" @click.stop>
        <div class="modal-header">
          <h3>요약 프롬프트 수정</h3>
          <button class="btn-close" @click="showPromptEditor = false">✕</button>
        </div>

        <div class="edit-form">
          <div class="form-group">
            <div class="prompt-field-header">
              <label>내용정리(단건) 프롬프트</label>
              <button class="btn btn-reset" type="button" @click="resetSinglePromptTemplate">
                단건 초기화
              </button>
            </div>
            <textarea
              v-model="promptTemplateForm.singleConsultationTemplate"
              rows="10"
            />
          </div>

          <div class="form-group">
            <div class="prompt-field-header">
              <label>요약보기(전체) 프롬프트</label>
              <button class="btn btn-reset" type="button" @click="resetAllPromptTemplate">
                일괄 초기화
              </button>
            </div>
            <textarea
              v-model="promptTemplateForm.allConsultationsTemplate"
              rows="12"
            />
          </div>
        </div>

        <div class="modal-actions">
          <button class="btn btn-primary" @click="savePromptTemplate" :disabled="savingPromptTemplate">
            {{ savingPromptTemplate ? '저장 중...' : '저장' }}
          </button>
          <button class="btn btn-secondary" @click="showPromptEditor = false">취소</button>
        </div>
      </div>
    </div>

    <div v-if="showTypeEditor" class="modal-overlay" @click="showTypeEditor = false">
      <div class="modal modal-type-editor" @click.stop>
        <div class="modal-header">
          <h3>상담유형 관리</h3>
          <button class="btn-close" @click="showTypeEditor = false">✕</button>
        </div>

        <div class="type-editor-list">
          <div v-for="item in typeEditorItems" :key="item.id" class="type-editor-row">
            <input
              v-model="item.name"
              type="text"
              placeholder="예: 방문상담, 카카오톡, CS"
            />
            <button class="btn btn-type-delete" type="button" @click="removeTypeItem(item.id)">삭제</button>
          </div>
        </div>

        <div class="type-editor-actions">
          <button class="btn btn-add-type" type="button" @click="addTypeItem">유형 추가</button>
        </div>

        <div class="modal-actions">
          <button class="btn btn-primary" @click="saveConsultationTypes">저장</button>
          <button class="btn btn-secondary" @click="showTypeEditor = false">취소</button>
        </div>
      </div>
    </div>
  </div>

  <!-- 관리자용 AI 어시스턴트 (내부 KB 포함 전체 접근, 왼쪽 하단) -->
  <FloatingChatbot role="admin" />
</template>

<style scoped>
/* KB 토스트 */
.kb-toast-container {
  position: fixed;
  bottom: 90px;
  right: 24px;
  z-index: 9000;
  display: flex;
  flex-direction: column;
  gap: 8px;
  pointer-events: none;
}

.kb-toast {
  padding: 12px 18px;
  border-radius: 10px;
  font-size: 0.9em;
  font-weight: 600;
  color: white;
  box-shadow: 0 4px 16px rgba(0,0,0,0.2);
  background: #4a5568;
}

.kb-toast.saving   { background: #4a5568; }
.kb-toast.analyzing { background: #667eea; }
.kb-toast.done     { background: #27ae60; }
.kb-toast.error    { background: #e53e3e; }

.toast-enter-active, .toast-leave-active { transition: all 0.4s; }
.toast-enter-from  { opacity: 0; transform: translateY(20px); }
.toast-leave-to    { opacity: 0; transform: translateX(40px); }

.crm-container {
  min-height: 100vh;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  padding: 20px;
}

.crm-header {
  color: white;
  text-align: center;
  margin-bottom: 24px;
}

.crm-header h1 {
  margin: 0 0 8px;
}

.crm-header p {
  margin: 0;
  opacity: 0.95;
  margin-bottom: 16px;
}

/* 페이지 탭 */
.page-tabs {
  display: flex;
  justify-content: center;
  gap: 8px;
  margin-top: 12px;
}

.page-tab {
  padding: 8px 24px;
  border: 2px solid rgba(255,255,255,0.5);
  border-radius: 20px;
  background: rgba(255,255,255,0.15);
  color: white;
  font-size: 0.9em;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}
.page-tab:hover { background: rgba(255,255,255,0.25); }
.page-tab.active {
  background: white;
  color: #667eea;
  border-color: white;
}

/* KB 관리 페이지 */
.kb-page {
  max-width: 1000px;
  margin: 0 auto;
  background: white;
  border-radius: 10px;
  padding: 24px;
}

.error-message {
  background: #f8d7da;
  color: #721c24;
  border: 1px solid #f5c6cb;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 16px;
}

.crm-content {
  max-width: 1400px;
  margin: 0 auto;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

.left-section,
.right-section {
  background: white;
  border-radius: 10px;
  padding: 20px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.section-header h2 {
  margin: 0;
}

.section-header-right {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  gap: 12px;
}

.section-header-right h2 {
  margin: 0;
  flex: 1;
}

.provider-select {
  border: 1px solid #d8d8d8;
  border-radius: 6px;
  padding: 7px 10px;
  font-size: 0.9em;
  background: white;
  color: #2f2f2f;
}

.consultation-section h2 {
  margin-top: 0;
}

.list-title {
  margin: 22px 0 10px;
}

.loading,
.empty-state {
  color: #666;
  text-align: center;
  padding: 30px 16px;
}

.btn {
  border: none;
  border-radius: 6px;
  padding: 8px 12px;
  cursor: pointer;
  font-weight: 600;
  font-size: 0.9em;
}

.btn-primary {
  background: #667eea;
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: #5568d3;
}

.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-summary {
  background: #f39c12;
  color: white;
  white-space: nowrap;
  padding: 8px 12px;
  font-size: 0.9em;
  min-width: auto;
}

.btn-summary:hover:not(:disabled) {
  background: #e67e22;
}

.btn-summary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-prompt-mini {
  background: #ffffff;
  color: #555;
  border: 1px solid #d8d8d8;
  white-space: nowrap;
  padding: 8px 10px;
  font-size: 0.78em;
}

.btn-prompt-mini:hover {
  background: #f3f5ff;
  border-color: #aeb9f6;
  color: #3f51b5;
}

.modal-summary {
  max-width: 800px;
  display: flex;
  flex-direction: column;
}

.modal-prompt {
  max-width: 760px;
}

.prompt-field-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.btn-reset {
  background: #f3f4f6;
  color: #5f6368;
  border: 1px solid #d1d5db;
}

.btn-reset:hover {
  background: #e9ebf0;
}

.modal-type-editor {
  max-width: 620px;
}

.type-editor-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.type-editor-row {
  display: flex;
  gap: 8px;
}

.type-editor-row input {
  flex: 1;
  border: 1px solid #d8d8d8;
  border-radius: 6px;
  padding: 10px;
  font-size: 14px;
}

.type-editor-row input:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.15);
}

.type-editor-actions {
  margin-top: 12px;
}

.btn-add-type {
  background: #eef2ff;
  color: #3f51b5;
  border: 1px solid #d2dcff;
}

.btn-add-type:hover {
  background: #e1e9ff;
}

.btn-type-delete {
  background: #ffe8e8;
  color: #c0392b;
  border: 1px solid #f7c9c9;
}

.btn-type-delete:hover {
  background: #ffd7d7;
}

.summary-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  padding-bottom: 12px;
  border-bottom: 2px solid #e0e0e0;
}

.summary-header h3 {
  margin: 0;
  color: #2c3e50;
}

.btn-close {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  color: #999;
  padding: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
}

.btn-close:hover {
  background: #f0f0f0;
  color: #333;
}

.summary-content {
  flex: 1;
  background: #f9f9f9;
  padding: 16px;
  border-radius: 6px;
  margin-bottom: 16px;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 400px;
  overflow-y: auto;
  line-height: 1.6;
  font-size: 0.95em;
  color: #333;
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  width: 92%;
  max-width: 560px;
  max-height: 90vh;
  overflow: auto;
  background: white;
  border-radius: 10px;
  padding: 24px;
}

.modal-edit {
  max-width: 600px;
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;
  padding-bottom: 12px;
  border-bottom: 2px solid #e0e0e0;
}

.modal-header h3 {
  margin: 0;
  color: #2c3e50;
}

.edit-form {
  flex: 1;
  min-width: 0;
}

.edit-form .form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 14px;
}

.edit-form label {
  font-weight: 600;
  color: #2f2f2f;
  font-size: 0.95em;
}

.edit-form input,
.edit-form select,
.edit-form textarea {
  border: 1px solid #d8d8d8;
  border-radius: 6px;
  padding: 10px;
  font-size: 14px;
  font-family: inherit;
}

.edit-form input:focus,
.edit-form select:focus,
.edit-form textarea:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.15);
}

.edit-form textarea {
  resize: vertical;
  min-height: 120px;
}

.edit-form .checkbox-group {
  margin-top: -6px;
}

.edit-form .checkbox-label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  color: #2f2f2f;
}

.edit-form .checkbox-label input {
  width: 16px;
  height: 16px;
}

.modal-actions {
  display: flex;
  gap: 12px;
  margin-top: 20px;
}

.btn-secondary {
  background: #95a5a6;
  color: white;
  flex: 1;
}

.btn-secondary:hover {
  background: #7f8c8d;
}

.btn-primary {
  flex: 1;
}

@media (max-width: 1024px) {
  .crm-content {
    grid-template-columns: 1fr;
  }
}
</style>

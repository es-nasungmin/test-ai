<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import axios from 'axios'
import CustomerList from '../components/Management/CustomerList.vue'
import CustomerForm from '../components/Management/CustomerForm.vue'
import InteractionForm from '../components/Management/InteractionForm.vue'
import InteractionList from '../components/Management/InteractionList.vue'
import KBManagement from '../components/Management/KBManagement.vue'
import ChatLogManagement from '../components/Management/ChatLogManagement.vue'
import PromptTestPanel from '../components/Management/PromptTestPanel.vue'
import UserApproval from '../components/Management/UserApproval.vue'
import { API_BASE_URL } from '../config'

const API_URL = API_BASE_URL

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

const showChatbotPromptEditor = ref(false)
const savingChatbotPromptTemplate = ref(false)
const chatbotPromptTemplateForm = ref({
  userSystemPrompt: '',
  adminSystemPrompt: '',
  userRulesPrompt: '',
  adminRulesPrompt: '',
  userLowSimilarityMessage: '',
  adminLowSimilarityMessage: '',
  similarityThreshold: 0.42
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

const filteredConsultations = computed(() => {
  if (!selectedCompany.value) {
    return []
  }

  return consultations.value
    .filter(item => item.customerId === selectedCompany.value.id)
    .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))
})

// 기간별 질문 분석 데이터 로드
async function fetchQuestionSummary() {
  loadingSummary.value = true
  try {
    const params = { days: summaryDays.value, top: summaryTop.value }
    if (roleFilter.value !== 'all') params.role = roleFilter.value
    if (platformFilter.value !== 'all') params.platform = platformFilter.value
    
    const response = await axios.get(`${API_URL}/chat/questions-summary`, { params })
    questionSummary.value = response.data || null
  } catch {
    questionSummary.value = null
  } finally {
    loadingSummary.value = false
  }
}

function formatDate(dateStr) {
  if (!dateStr) return '-'
  const date = new Date(dateStr)
  return date.toLocaleDateString('ko-KR')
}

function formatDateTime(dateStr) {
  if (!dateStr) return '-'
  const date = new Date(dateStr)
  return date.toLocaleString('ko-KR', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
}

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

const editSummarizing = ref(false)
const editSummarizedContent = ref('')

const editConsultation = (interaction) => {
  if (!consultationTypes.value.includes(interaction.type)) {
    interaction.type = consultationTypes.value[0] || 'Call'
  }
  editingInteraction.value = { ...interaction }
  editSummarizedContent.value = ''
  showEditInteractionModal.value = true
}

const summarizeEditContent = async () => {
  if (!editingInteraction.value?.content?.trim()) {
    alert('상담 내용을 입력해 주세요.')
    return
  }
  editSummarizing.value = true
  try {
    const response = await axios.post(`${API_URL}/interaction/summarize`, {
      content: editingInteraction.value.content,
      type: editingInteraction.value.type,
      provider: summaryProvider.value
    })
    editSummarizedContent.value = response.data.summary
  } catch (err) {
    alert('내용 정리 중 오류가 발생했습니다: ' + (err.response?.data?.error || err.message))
    console.error(err)
  } finally {
    editSummarizing.value = false
  }
}

const useEditSummarized = () => {
  if (editSummarizedContent.value) {
    editingInteraction.value.content = editSummarizedContent.value
    editSummarizedContent.value = ''
  }
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

const fetchChatbotPromptTemplate = async () => {
  try {
    const response = await axios.get(`${API_URL}/knowledgebase/chatbot-prompt-template`)
    chatbotPromptTemplateForm.value = {
      userSystemPrompt: response.data.userSystemPrompt,
      adminSystemPrompt: response.data.adminSystemPrompt,
      userRulesPrompt: response.data.userRulesPrompt,
      adminRulesPrompt: response.data.adminRulesPrompt,
      userLowSimilarityMessage: response.data.userLowSimilarityMessage,
      adminLowSimilarityMessage: response.data.adminLowSimilarityMessage,
      similarityThreshold: response.data.similarityThreshold
    }
  } catch (err) {
    error.value = '챗봇 프롬프트 설정을 불러오지 못했습니다.'
    console.error(err)
  }
}

const openChatbotPromptEditor = async () => {
  await fetchChatbotPromptTemplate()
  showChatbotPromptEditor.value = true
}

const saveChatbotPromptTemplate = async () => {
  const form = chatbotPromptTemplateForm.value
  if (
    !form.userSystemPrompt.trim() ||
    !form.adminSystemPrompt.trim() ||
    !form.userRulesPrompt.trim() ||
    !form.adminRulesPrompt.trim() ||
    !form.userLowSimilarityMessage.trim() ||
    !form.adminLowSimilarityMessage.trim()
  ) {
    alert('챗봇 프롬프트 항목을 모두 입력해주세요.')
    return
  }

  savingChatbotPromptTemplate.value = true
  try {
    const response = await axios.put(`${API_URL}/knowledgebase/chatbot-prompt-template`, {
      userSystemPrompt: form.userSystemPrompt,
      adminSystemPrompt: form.adminSystemPrompt,
      userRulesPrompt: form.userRulesPrompt,
      adminRulesPrompt: form.adminRulesPrompt,
      userLowSimilarityMessage: form.userLowSimilarityMessage,
      adminLowSimilarityMessage: form.adminLowSimilarityMessage,
      similarityThreshold: Number(form.similarityThreshold)
    })

    chatbotPromptTemplateForm.value = {
      userSystemPrompt: response.data.userSystemPrompt,
      adminSystemPrompt: response.data.adminSystemPrompt,
      userRulesPrompt: response.data.userRulesPrompt,
      adminRulesPrompt: response.data.adminRulesPrompt,
      userLowSimilarityMessage: response.data.userLowSimilarityMessage,
      adminLowSimilarityMessage: response.data.adminLowSimilarityMessage,
      similarityThreshold: response.data.similarityThreshold
    }

    alert('챗봇 프롬프트가 저장되었습니다. 다음 대화부터 반영됩니다.')
    showChatbotPromptEditor.value = false
  } catch (err) {
    alert('챗봇 프롬프트 저장에 실패했습니다: ' + (err.response?.data?.error || err.message))
    console.error(err)
  } finally {
    savingChatbotPromptTemplate.value = false
  }
}

// ---- 페이지 탭 ----
const activePage = ref('kb')  // 'kb' | 'logs' | 'question-analysis' | 'prompt-test' | 'crm' | 'user-management'

// 채팅 필터 및 기간별 질문 분석 관련 상태
const roleFilter = ref('all')
const platformFilter = ref('all')
const loadingSummary = ref(false)
const summaryDays = ref(7)
const summaryTop = ref(10)
const questionSummary = ref(null)

const openChatWidgetHtmlExample = () => {
  window.open('/chat-widget-example.html', '_blank', 'noopener,noreferrer')
}

watch([summaryDays, summaryTop], () => {
  fetchQuestionSummary()
})

onMounted(() => {
  loadConsultationTypes()
  fetchCompanies()
  fetchConsultations()
  fetchPromptTemplate()
  fetchChatbotPromptTemplate()
  fetchQuestionSummary()
})
</script>

<template>
  <div class="crm-container">
    <header class="crm-header">
      <div class="crm-header-shell">
        <div class="crm-header-topline">
          <span class="crm-header-badge">AI 운영 콘솔</span>
        </div>
        <h1>ESN AI 운영센터</h1>
        <p>KB, 채팅 로그, 프롬프트 테스트를 한 곳에서 관리합니다.</p>
        <!-- 페이지 탭 -->
        <div class="page-tabs">
          <button
            class="page-tab"
            :class="{ active: activePage === 'kb' }"
            @click="activePage = 'kb'"
          >KB관리</button>
          <button
            class="page-tab"
            :class="{ active: activePage === 'logs' }"
            @click="activePage = 'logs'"
          >채팅관리</button>
          <button
            class="page-tab"
            :class="{ active: activePage === 'question-analysis' }"
            @click="activePage = 'question-analysis'"
          >질문분석</button>
          <button
            class="page-tab"
            :class="{ active: activePage === 'crm' }"
            @click="activePage = 'crm'"
          >CRM 상담관리(테스트)</button>
          <button
            class="page-tab"
            :class="{ active: activePage === 'prompt-test' }"
            @click="activePage = 'prompt-test'"
          >프롬프트 테스트</button>
          <button
            class="page-tab"
            :class="{ active: activePage === 'user-management' }"
            @click="activePage = 'user-management'"
          >사용자 관리</button>
        </div>
      </div>
    </header>

    <div v-if="error" class="error-message">
      {{ error }}
    </div>

    <!-- KB 관리 페이지 -->
    <div v-if="activePage === 'kb'" class="kb-page">
      <KBManagement />
    </div>

    <div v-if="activePage === 'logs'" class="kb-page">
      <div class="chat-management-actions">
        <button class="btn btn-chat-mgmt" type="button" @click="openChatbotPromptEditor">
          Chat 프롬프트
        </button>
        <button class="btn btn-chat-mgmt" type="button" @click="openChatWidgetHtmlExample">
          HTML Chat 예시
        </button>
      </div>
      <ChatLogManagement />
    </div>


    <div v-if="activePage === 'question-analysis'" class="kb-page">
      <div class="summary-panel-wrapper">
        <div class="summary-controls">
          <div class="control-group">
            <label>조회 기간:</label>
            <select v-model.number="summaryDays" class="control-input">
              <option :value="7">최근 7일</option>
              <option :value="30">최근 30일</option>
              <option :value="90">최근 90일</option>
              <option :value="365">최근 1년</option>
            </select>
          </div>
          <div class="control-group">
            <label>상위:</label>
            <select v-model.number="summaryTop" class="control-input">
              <option :value="5">5개</option>
              <option :value="10">10개</option>
              <option :value="20">20개</option>
            </select>
          </div>
          <div class="control-group">
            <label>역할:</label>
            <select v-model="roleFilter" class="control-input">
              <option value="all">전체</option>
              <option value="user">사용자</option>
              <option value="admin">관리자</option>
            </select>
          </div>
          <div class="control-group">
            <label>플랫폼:</label>
            <select v-model="platformFilter" class="control-input">
              <option value="all">전체</option>
              <option value="공통">공통</option>
            </select>
          </div>
          <button
            class="summary-refresh-btn"
            type="button"
            :disabled="loadingSummary"
            @click="fetchQuestionSummary"
          >
            {{ loadingSummary ? '새로고침 중...' : '새로고침' }}
          </button>
        </div>

        <div v-if="loadingSummary" class="loading">
          질문 분석 데이터 로딩 중...
        </div>

        <div v-else-if="questionSummary" class="question-summary-content">
          <div class="analysis-title-row">
            <h3>질문 분석 리포트</h3>
            <p>조회기간 {{ summaryDays }}일 기준 집계</p>
          </div>

          <div class="kpi-cards">
            <div class="kpi-card">
              <div class="kpi-label">총 질문 수</div>
              <div class="kpi-value">{{ questionSummary.totalQuestions || 0 }}</div>
            </div>
            <div class="kpi-card">
              <div class="kpi-label">고유 질문 수</div>
              <div class="kpi-value">{{ questionSummary.uniqueQuestions || 0 }}</div>
            </div>
            <div class="kpi-card">
              <div class="kpi-label">요청 상위 기준</div>
              <div class="kpi-value">TOP {{ summaryTop }}</div>
            </div>
            <div class="kpi-card">
              <div class="kpi-label">집계 일수</div>
              <div class="kpi-value">{{ questionSummary.dailyCounts?.length || 0 }}일</div>
            </div>
          </div>

          <!-- 상위 질문 섹션 -->
          <div class="top-questions-section">
            <h3>상위 {{ summaryTop }} 질문</h3>
            <div v-if="questionSummary.topQuestions && questionSummary.topQuestions.length > 0" class="questions-list">
              <div v-for="(q, idx) in questionSummary.topQuestions" :key="idx" class="question-item">
                <div class="question-rank">{{ idx + 1 }}</div>
                <div class="question-info">
                  <div class="question-text">{{ q.question || q.Query || '-' }}</div>
                  <div class="question-meta">
                    <span class="meta-item">질문 수: {{ q.count || q.Count || 0 }}</span>
                    <span class="meta-item">최근: {{ formatDateTime(q.lastAskedAt || q.LastAskedAt) }}</span>
                  </div>
                </div>
              </div>
            </div>
            <div v-else class="empty-state">
              데이터가 없습니다.
            </div>
          </div>

          <!-- 키워드 분석 섹션 -->
          <div v-if="questionSummary.topKeywords" class="keywords-section">
            <h3>주요 키워드</h3>
            <div class="keywords-cloud">
              <span v-for="(kw, idx) in questionSummary.topKeywords" :key="idx" class="keyword-tag">
                {{ kw.keyword || kw.Keyword || kw }}
                <strong v-if="kw.count || kw.Count">{{ kw.count || kw.Count }}</strong>
              </span>
            </div>
          </div>

          <!-- 일일 질문 수 섹션 -->
          <div v-if="questionSummary.dailyCounts" class="daily-counts-section">
            <h3>일일 질문 수</h3>
            <div class="daily-counts-list">
              <div v-for="(item, idx) in questionSummary.dailyCounts" :key="idx" class="daily-item">
                <div class="daily-date">{{ formatDate(item.date || item.Date) }}</div>
                <div class="daily-count">{{ item.count || 0 }}건</div>
              </div>
            </div>
          </div>
        </div>

        <div v-else class="empty-state">
          분석할 데이터가 없습니다.
        </div>
      </div>
    </div>
    <div v-if="activePage === 'prompt-test'" class="kb-page">
      <PromptTestPanel />
    </div>

    <div v-if="activePage === 'user-management'" class="kb-page">
      <UserApproval />
    </div>

    <div v-show="activePage === 'crm'" class="crm-content">
      <section class="left-section">
        <div class="section-header-right">
          <h2>업체 목록</h2>
          <button class="btn btn-add-company" @click="openAddCompanyForm">
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
              class="btn btn-summary-action"
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
          <div v-if="editSummarizedContent" class="summarized-section">
            <div class="summarized-title">정리된 내용</div>
            <div class="summarized-content">{{ editSummarizedContent }}</div>
            <div class="summarized-actions">
              <button class="btn-use-summary" type="button" @click="useEditSummarized">적용</button>
              <button class="btn-clear-summary" type="button" @click="editSummarizedContent = ''">닫기</button>
            </div>
          </div>
          <div class="modal-actions">
            <button class="btn btn-primary" @click="updateConsultation(editingInteraction)">
              저장
            </button>
            <button
              class="btn btn-summarize"
              type="button"
              @click="summarizeEditContent"
              :disabled="editSummarizing || !editingInteraction.content?.trim()"
            >
              {{ editSummarizing ? '정리 중...' : '내용정리' }}
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

    <div v-if="showChatbotPromptEditor" class="modal-overlay" @click="showChatbotPromptEditor = false">
      <div class="modal modal-prompt" @click.stop>
        <div class="modal-header">
          <h3>챗봇 프롬프트 수정</h3>
          <button class="btn-close" @click="showChatbotPromptEditor = false">✕</button>
        </div>

        <div class="edit-form">
          <div class="prompt-role-block">
            <h4>사용자 챗봇</h4>
            <div class="form-group">
              <label>시스템 프롬프트</label>
              <textarea v-model="chatbotPromptTemplateForm.userSystemPrompt" rows="3" />
            </div>

            <div class="form-group">
              <label>답변 규칙</label>
              <textarea v-model="chatbotPromptTemplateForm.userRulesPrompt" rows="4" />
            </div>

            <div class="form-group">
              <label>저유사도 안내문</label>
              <textarea v-model="chatbotPromptTemplateForm.userLowSimilarityMessage" rows="2" />
            </div>
          </div>

          <div class="prompt-role-block">
            <h4>관리자 챗봇</h4>
            <div class="form-group">
              <label>시스템 프롬프트</label>
              <textarea v-model="chatbotPromptTemplateForm.adminSystemPrompt" rows="3" />
            </div>

            <div class="form-group">
              <label>답변 규칙</label>
              <textarea v-model="chatbotPromptTemplateForm.adminRulesPrompt" rows="4" />
            </div>

            <div class="form-group">
              <label>저유사도 안내문</label>
              <textarea v-model="chatbotPromptTemplateForm.adminLowSimilarityMessage" rows="2" />
            </div>
          </div>

          <div class="form-group">
            <label>유사도 임계치 (0~1)</label>
            <input v-model.number="chatbotPromptTemplateForm.similarityThreshold" type="number" min="0.1" max="0.95" step="0.01" />
          </div>
        </div>

        <div class="modal-actions">
          <button class="btn btn-primary" @click="saveChatbotPromptTemplate" :disabled="savingChatbotPromptTemplate">
            {{ savingChatbotPromptTemplate ? '저장 중...' : '저장' }}
          </button>
          <button class="btn btn-secondary" @click="showChatbotPromptEditor = false">취소</button>
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

</template>

<style scoped>
.crm-container {
  min-height: 100vh;
  background: #f8f9fa;
  padding: 0 0 20px;
}

.crm-header {
  color: #212529;
  margin-bottom: 14px;
}

.crm-header-shell {
  max-width: none;
  width: 100%;
  margin: 0;
  text-align: center;
  padding: 18px 20px 14px;
  border: 1px solid #d8e4ef;
  border-radius: 16px;
  background:
    linear-gradient(180deg, #f6f9fd 0%, #edf3fa 100%);
  box-shadow: 0 10px 24px rgba(15, 23, 42, 0.08);
}

.crm-header-topline {
  margin-bottom: 8px;
}

.crm-header-badge {
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  border-radius: 999px;
  border: 1px solid #c7dbf8;
  background: #eaf3ff;
  color: #295d9d;
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.08em;
}

.crm-header h1 {
  margin: 0 0 6px;
  font-size: clamp(1.4rem, 2vw, 2.1rem);
  font-weight: 800;
  color: #1d2d45;
}

.crm-header p {
  margin: 0;
  color: #54657c;
  margin-bottom: 12px;
}

/* 페이지 탭 */
.page-tabs {
  display: flex;
  justify-content: center;
  gap: 8px;
  margin-top: 6px;
  flex-wrap: wrap;
}

.page-tab {
  padding: 9px 16px;
  border: 1px solid #cad6e3;
  border-radius: 999px;
  background: #ffffff;
  color: #44556c;
  font-size: 0.9em;
  font-weight: 700;
  cursor: pointer;
  box-shadow: 0 4px 10px rgba(15, 23, 42, 0.05);
  transition: all 0.2s ease;
}
.page-tab:hover {
  border-color: #8fb2de;
  color: #2f5f98;
  background: #f8fbff;
}
.page-tab.active {
  background: linear-gradient(135deg, #0d6efd 0%, #4c9dff 100%);
  color: #ffffff;
  border-color: #0d6efd;
  box-shadow: 0 8px 16px rgba(13, 110, 253, 0.28);
}

/* KB 관리 페이지 */
.kb-page {
  max-width: none;
  width: calc(100% - 32px);
  margin: 0 auto;
  background: #ffffff;
  border-radius: 12px;
  padding: 24px;
  border: 1px solid #dee2e6;
  box-shadow: 0 0.5rem 1rem rgba(0, 0, 0, 0.08);
}

.summary-panel-wrapper {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.summary-controls {
  display: grid;
  grid-template-columns: repeat(5, minmax(120px, 1fr));
  gap: 10px;
  background: #f6f9fd;
  border: 1px solid #d9e4f0;
  border-radius: 12px;
  padding: 12px;
}

.summary-refresh-btn {
  align-self: end;
  justify-self: end;
  width: fit-content;
  min-width: 92px;
  height: 36px;
  border: 1px solid #ced4da;
  border-radius: 10px;
  background: #ffffff;
  color: #6c757d;
  font-weight: 700;
  font-size: 0.86rem;
  padding: 0 12px;
  cursor: pointer;
  white-space: nowrap;
}

.summary-refresh-btn:hover:not(:disabled) {
  background: #f8f9fa;
}

.summary-refresh-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.control-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.control-group label {
  font-size: 0.78rem;
  font-weight: 700;
  color: #4d607a;
}

.control-input {
  border: 1px solid #cbd7e4;
  border-radius: 8px;
  padding: 8px 10px;
  background: #ffffff;
  color: #2f4056;
  font-weight: 600;
}

.question-summary-content {
  background: linear-gradient(180deg, #ffffff 0%, #f9fbfe 100%);
  border: 1px solid #e1e8f0;
  border-radius: 12px;
  padding: 16px;
}

.analysis-title-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
  margin-bottom: 14px;
}

.analysis-title-row h3 {
  margin: 0;
  color: #1f3551;
}

.analysis-title-row p {
  margin: 0;
  color: #5f738f;
  font-size: 0.86rem;
}

.kpi-cards {
  display: grid;
  grid-template-columns: repeat(4, minmax(130px, 1fr));
  gap: 10px;
  margin-bottom: 16px;
}

.kpi-card {
  background: #ffffff;
  border: 1px solid #dbe6f2;
  border-radius: 10px;
  padding: 12px;
}

.kpi-label {
  font-size: 0.78rem;
  font-weight: 700;
  color: #5d738f;
  margin-bottom: 6px;
}

.kpi-value {
  font-size: 1.15rem;
  font-weight: 800;
  color: #17314f;
}

.top-questions-section,
.keywords-section,
.daily-counts-section {
  margin-top: 12px;
  border-top: 1px solid #edf2f8;
  padding-top: 12px;
}

.top-questions-section h3,
.keywords-section h3,
.daily-counts-section h3 {
  margin: 0 0 10px;
  color: #2a415e;
  font-size: 1rem;
}

.questions-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.question-item {
  display: grid;
  grid-template-columns: 30px 1fr;
  gap: 10px;
  align-items: start;
  background: #ffffff;
  border: 1px solid #e3ebf4;
  border-radius: 10px;
  padding: 10px;
}

.question-rank {
  width: 30px;
  height: 30px;
  border-radius: 999px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #e8f1ff;
  color: #235d9d;
  font-weight: 800;
  font-size: 0.82rem;
}

.question-text {
  font-weight: 700;
  color: #243f5d;
}

.question-meta {
  margin-top: 4px;
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.meta-item {
  font-size: 0.8rem;
  color: #5f738f;
}

.keywords-cloud {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.keyword-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border-radius: 999px;
  background: #edf4ff;
  border: 1px solid #d4e3fa;
  color: #244a77;
  font-weight: 700;
  font-size: 0.82rem;
}

.keyword-tag strong {
  font-size: 0.76rem;
  color: #5d738f;
}

.daily-counts-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 8px;
}

.daily-item {
  background: #ffffff;
  border: 1px solid #e3ebf4;
  border-radius: 8px;
  padding: 9px 10px;
}

.daily-date {
  font-size: 0.78rem;
  color: #61758f;
}

.daily-count {
  margin-top: 2px;
  font-weight: 800;
  color: #203b5a;
}

.chat-management-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-bottom: 12px;
}

.btn-chat-mgmt {
  background: #ffffff;
  color: #495057;
  border: 1px solid #ced4da;
  white-space: nowrap;
}

.btn-chat-mgmt:hover {
  background: #f8f9fa;
  border-color: #adb5bd;
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
  max-width: none;
  width: calc(100% - 32px);
  margin: 0 auto;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.left-section,
.right-section {
  background: #ffffff;
  border-radius: 12px;
  border: 1px solid #dee2e6;
  padding: 18px;
  box-shadow: 0 0.35rem 0.8rem rgba(0, 0, 0, 0.06);
}

.left-section {
  background: #ffffff;
}

.right-section {
  background: #ffffff;
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
  margin-bottom: 14px;
  gap: 12px;
}

.section-header-right h2 {
  margin: 0;
  flex: 1;
}

.provider-select {
  border: 1px solid #ced4da;
  border-radius: 8px;
  padding: 8px 11px;
  font-size: 0.9em;
  background: white;
  color: #495057;
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
  border: 1px solid transparent;
  border-radius: 10px;
  padding: 8px 13px;
  cursor: pointer;
  font-weight: 700;
  font-size: 0.9em;
}

.btn-primary {
  background: #0d6efd;
  border-color: #0d6efd;
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: #0b5ed7;
  border-color: #0a58ca;
}

.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-add-company {
  background: #198754;
  color: white;
  border-color: #198754;
  white-space: nowrap;
  padding: 8px 12px;
  font-size: 0.9em;
  min-width: auto;
}

.btn-add-company:hover:not(:disabled) {
  background: #157347;
  border-color: #146c43;
}

.btn-summary-action {
  background: #ffc107;
  color: #212529;
  border-color: #ffc107;
  white-space: nowrap;
  padding: 8px 12px;
  font-size: 0.9em;
  min-width: auto;
}

.btn-summary-action:hover:not(:disabled) {
  background: #ffca2c;
  border-color: #ffc720;
}

.btn-summary-action:disabled,
.btn-add-company:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-prompt-mini {
  background: #ffffff;
  color: #6c757d;
  border: 1px solid #ced4da;
  white-space: nowrap;
  padding: 8px 10px;
  font-size: 0.78em;
}

.btn-prompt-mini:hover {
  background: #f8f9fa;
  border-color: #adb5bd;
  color: #495057;
}

.modal-summary {
  max-width: 800px;
  display: flex;
  flex-direction: column;
}

.modal-prompt {
  max-width: 760px;
}

.prompt-role-block {
  border: 1px solid #dee2e6;
  border-radius: 10px;
  background: #f8f9fa;
  padding: 12px;
  margin-bottom: 12px;
}

.prompt-role-block h4 {
  margin: 0 0 10px;
  color: #212529;
}

.prompt-field-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.btn-reset {
  background: #ffffff;
  color: #6c757d;
  border: 1px solid #ced4da;
}

.btn-reset:hover {
  background: #f8f9fa;
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
  border-color: #86b7fe;
  box-shadow: 0 0 0 0.25rem rgba(13, 110, 253, 0.25);
}

.type-editor-actions {
  margin-top: 12px;
}

.btn-add-type {
  background: #ffffff;
  color: #0d6efd;
  border: 1px solid #0d6efd;
}

.btn-add-type:hover {
  background: #e7f1ff;
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
  color: #212529;
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
  background: #f8f9fa;
  padding: 16px;
  border-radius: 6px;
  margin-bottom: 16px;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 400px;
  overflow-y: auto;
  line-height: 1.6;
  font-size: 0.95em;
  color: #495057;
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
  border-radius: 12px;
  border: 1px solid #dee2e6;
  box-shadow: 0 1rem 2rem rgba(0, 0, 0, 0.2);
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
  color: #212529;
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
  border: 1px solid #ced4da;
  border-radius: 8px;
  padding: 10px;
  font-size: 14px;
  font-family: inherit;
}

.edit-form input:focus,
.edit-form select:focus,
.edit-form textarea:focus {
  outline: none;
  border-color: #86b7fe;
  box-shadow: 0 0 0 0.25rem rgba(13, 110, 253, 0.25);
}

.edit-form textarea {
  resize: vertical;
  min-height: 120px;
}

.modal-actions {
  display: flex;
  gap: 12px;
  margin-top: 20px;
}

.btn-secondary {
  background: #6c757d;
  color: #ffffff;
  border-color: #6c757d;
  flex: 1;
}

.btn-secondary:hover {
  background: #5c636a;
  border-color: #565e64;
}

.btn-primary {
  flex: 1;
}

.btn-summarize {
  flex: 1;
  border: 1px solid #0d6efd;
  border-radius: 8px;
  padding: 10px;
  background: #0d6efd;
  color: white;
  font-weight: 700;
  cursor: pointer;
  transition: background 0.2s ease;
  font-size: 0.9em;
}

.btn-summarize:hover:not(:disabled) {
  background: #0b5ed7;
}

.btn-summarize:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.edit-form .summarized-section {
  margin-bottom: 14px;
  padding: 12px;
  background: #f8f9fa;
  border: 1px solid #dee2e6;
  border-radius: 8px;
}

.edit-form .summarized-title {
  font-weight: 700;
  color: #212529;
  margin-bottom: 8px;
  font-size: 0.9em;
}

.edit-form .summarized-content {
  white-space: pre-wrap;
  font-size: 0.9em;
  color: #495057;
  line-height: 1.6;
  margin-bottom: 10px;
}

.edit-form .summarized-actions {
  display: flex;
  gap: 8px;
}

.btn-use-summary {
  border: 1px solid #198754;
  border-radius: 6px;
  padding: 6px 14px;
  background: #198754;
  color: white;
  font-weight: 700;
  cursor: pointer;
  font-size: 0.85em;
}

.btn-use-summary:hover {
  background: #157347;
}

.btn-clear-summary {
  border: 1px solid #dee2e6;
  border-radius: 6px;
  padding: 6px 14px;
  background: #fff;
  color: #6c757d;
  cursor: pointer;
  font-size: 0.85em;
}

.btn-clear-summary:hover {
  background: #f8f9fa;
}

@media (max-width: 1024px) {
  .crm-container {
    padding-top: 0;
  }

  .crm-header {
    margin-bottom: 14px;
  }

  .crm-header-shell {
    border-radius: 12px;
    padding: 14px 12px 12px;
  }

  .crm-content {
    width: calc(100% - 16px);
    grid-template-columns: 1fr;
  }

  .kb-page {
    width: calc(100% - 16px);
  }

  .summary-controls {
    grid-template-columns: repeat(2, minmax(120px, 1fr));
  }

  .kpi-cards {
    grid-template-columns: repeat(2, minmax(120px, 1fr));
  }
}

@media (max-width: 640px) {
  .summary-controls,
  .kpi-cards {
    grid-template-columns: 1fr;
  }

  .summary-refresh-btn {
    justify-self: start;
  }

  .analysis-title-row {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>

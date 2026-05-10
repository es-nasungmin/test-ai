<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import axios from 'axios'
import KBManagement from '../components/Management/KBManagement.vue'
import ChatLogManagement from '../components/Management/ChatLogManagement.vue'
import UserApproval from '../components/Management/UserApproval.vue'
import LowSimilarityManagement from '../components/Management/LowSimilarityManagement.vue'
import InlineChatbot from '../components/Management/InlineChatbot.vue'
import { API_BASE_URL } from '../config'

const props = defineProps({ user: Object })
const emit = defineEmits(['openMyPage', 'logout'])

const API_URL = API_BASE_URL
const KB_FOCUS_STORAGE_KEY = 'aidesk.management.focusKbId'

const error = ref('')
const ACTIVE_PAGE_STORAGE_KEY = 'aidesk.management.activePage'
const ACTIVE_ANALYSIS_TAB_STORAGE_KEY = 'aidesk.management.activeAnalysisTab'
const ALLOWED_ACTIVE_PAGES = ['kb', 'logs', 'question-analysis', 'chat-test', 'user-management']
const ALLOWED_ANALYSIS_TABS = ['report', 'low-similarity']

const showChatbotPromptEditor = ref(false)
const savingChatbotPromptTemplate = ref(false)
const chatbotPromptTemplateForm = ref({
  userSystemPrompt: '',
  adminSystemPrompt: '',
  userRulesPrompt: '',
  adminRulesPrompt: '',
  userLowSimilarityMessage: '',
  adminLowSimilarityMessage: '',
  similarityThreshold: 0.5
})

const displayUserName = computed(() => {
  const name = props.user?.username
  if (!name) return ''
  return String(name).toLowerCase() === 'admin' ? '관리자' : name
})

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function shouldRetryApiError(err) {
  const status = err?.response?.status
  return !status || status >= 500
}

async function withRetry(requestFn, { retries = 4, initialDelayMs = 300 } = {}) {
  let attempt = 0
  let lastError

  while (attempt <= retries) {
    try {
      return await requestFn()
    } catch (err) {
      lastError = err
      if (!shouldRetryApiError(err) || attempt === retries) {
        throw err
      }

      const delay = initialDelayMs * (attempt + 1)
      await wait(delay)
      attempt += 1
    }
  }

  throw lastError
}


// 기간별 질문 분석 데이터 로드
async function fetchQuestionSummary() {
  loadingSummary.value = true
  try {
    const params = { days: summaryDays.value, top: summaryTop.value }
    if (roleFilter.value !== 'all') params.role = roleFilter.value
    if (platformFilter.value !== 'all') params.platform = platformFilter.value
    
    const response = await withRetry(() => axios.get(`${API_URL}/chat/questions-summary`, { params }))
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

const fetchChatbotPromptTemplate = async () => {
  try {
    const response = await withRetry(() => axios.get(`${API_URL}/knowledgebase/chatbot-prompt-template`))
    chatbotPromptTemplateForm.value = {
      userSystemPrompt: response.data.userSystemPrompt,
      adminSystemPrompt: response.data.adminSystemPrompt,
      userRulesPrompt: response.data.userRulesPrompt,
      adminRulesPrompt: response.data.adminRulesPrompt,
      userLowSimilarityMessage: response.data.userLowSimilarityMessage,
      adminLowSimilarityMessage: response.data.adminLowSimilarityMessage,
      similarityThreshold: response.data.similarityThreshold
    }
    error.value = ''
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

function loadStoredValue(key, fallback, allowedValues) {
  try {
    const raw = localStorage.getItem(key)
    if (!raw) return fallback
    return allowedValues.includes(raw) ? raw : fallback
  } catch {
    return fallback
  }
}

// ---- 페이지 탭 ----
const activePage = ref(loadStoredValue(ACTIVE_PAGE_STORAGE_KEY, 'kb', ALLOWED_ACTIVE_PAGES))
const activeAnalysisTab = ref(loadStoredValue(ACTIVE_ANALYSIS_TAB_STORAGE_KEY, 'report', ALLOWED_ANALYSIS_TABS))
const showUserManagement = computed(() => {
  try {
    const raw = localStorage.getItem('user')
    if (!raw) return false
    const user = JSON.parse(raw)
    const username = typeof user?.username === 'string' ? user.username.trim().toLowerCase() : ''
    const role = typeof user?.role === 'string' ? user.role.trim().toLowerCase() : ''
    return username === 'admin' || role === 'admin'
  } catch {
    return false
  }
})

// 채팅 필터 및 기간별 질문 분석 관련 상태
const roleFilter = ref('all')
const platformFilter = ref('all')
const summaryPlatformOptions = ref(['공통'])
const loadingSummary = ref(false)
const summaryDays = ref(7)
const summaryTop = ref(10)
const questionSummary = ref(null)
const maxDailyQuestionCount = computed(() => {
  const items = Array.isArray(questionSummary.value?.dailyCounts)
    ? questionSummary.value.dailyCounts
    : []

  if (items.length === 0) return 1

  return Math.max(
    1,
    ...items.map((item) => Number(item?.count ?? item?.Count ?? 0))
  )
})

const dailyTrendSeries = computed(() => {
  const items = Array.isArray(questionSummary.value?.dailyCounts)
    ? questionSummary.value.dailyCounts
    : []

  return items
    .map((item) => ({
      date: item?.date ?? item?.Date,
      count: Number(item?.count ?? item?.Count ?? 0)
    }))
    .filter((item) => !!item.date)
})

const dailyTrendPolyline = computed(() => {
  const items = dailyTrendSeries.value
  if (items.length === 0) return ''

  const maxCount = Math.max(1, ...items.map((item) => item.count))
  const w = 100
  const h = 44

  return items
    .map((item, idx) => {
      const x = items.length === 1 ? w / 2 : (idx / (items.length - 1)) * w
      const y = h - (item.count / maxCount) * h
      return `${x.toFixed(2)},${y.toFixed(2)}`
    })
    .join(' ')
})

const dailyTrendPoints = computed(() => {
  const items = dailyTrendSeries.value
  if (items.length === 0) return []

  const maxCount = Math.max(1, ...items.map((item) => item.count))
  const w = 100
  const h = 44

  return items.map((item, idx) => ({
    ...item,
    x: items.length === 1 ? w / 2 : (idx / (items.length - 1)) * w,
    y: h - (item.count / maxCount) * h
  }))
})

async function fetchSummaryPlatforms() {
  try {
    const response = await withRetry(() => axios.get(`${API_URL}/knowledgebase/platforms`))
    const list = Array.isArray(response.data) ? response.data : []
    const normalized = list
      .map((x) => (typeof x === 'string' ? x.trim() : ''))
      .filter(Boolean)
    summaryPlatformOptions.value = Array.from(new Set(['공통', ...normalized]))
    if (platformFilter.value !== 'all' && !summaryPlatformOptions.value.includes(platformFilter.value)) {
      platformFilter.value = 'all'
    }
  } catch {
    summaryPlatformOptions.value = ['공통']
    if (platformFilter.value !== 'all' && platformFilter.value !== '공통') {
      platformFilter.value = 'all'
    }
  }
}

const openChatWidgetHtmlExample = () => {
  window.open('/chat-widget-example.html', '_blank', 'noopener,noreferrer')
}

function openKbFromSummary(kb) {
  const kbId = Number(kb?.kbId ?? kb?.KbId ?? 0)
  if (!kbId || Number.isNaN(kbId)) return

  try {
    localStorage.setItem(KB_FOCUS_STORAGE_KEY, String(kbId))
  } catch {
    // ignore storage errors
  }

  activePage.value = 'kb'
}

watch([summaryDays, summaryTop], () => {
  fetchQuestionSummary()
})

watch([roleFilter, platformFilter], () => {
  fetchQuestionSummary()
})

watch([activePage, activeAnalysisTab], async ([page, tab]) => {
  if (page === 'question-analysis' && tab === 'report') {
    await fetchSummaryPlatforms()
  }
})

watch(showUserManagement, (canManageUsers) => {
  if (!canManageUsers && activePage.value === 'user-management') {
    activePage.value = 'kb'
  }
})

watch(activePage, (page) => {
  try {
    localStorage.setItem(ACTIVE_PAGE_STORAGE_KEY, page)
  } catch {
    // no-op when storage is unavailable
  }
})

watch(activeAnalysisTab, (tab) => {
  try {
    localStorage.setItem(ACTIVE_ANALYSIS_TAB_STORAGE_KEY, tab)
  } catch {
    // no-op when storage is unavailable
  }
})

onMounted(() => {
  fetchChatbotPromptTemplate()
  fetchSummaryPlatforms()
  fetchQuestionSummary()
})
</script>

<template>
  <div class="crm-container">
    <header class="crm-header">
      <div class="crm-header-shell">
        <div class="crm-header-topline">
          <span class="crm-header-badge">AI 챗봇 운영</span>
          <div class="header-user-info">
            <button class="header-username-btn" type="button" @click="emit('openMyPage')">{{ displayUserName }}</button>
            <button class="header-logout-btn" type="button" @click="emit('logout')">로그아웃</button>
          </div>
        </div>
        <h1>챗봇 운영 센터</h1>
        <p>지식베이스 관리, 대화 이력 모니터링, 질문 패턴 분석을 한 화면에서 운영합니다.</p>
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
            class="page-tab hide-on-mobile"
            :class="{ active: activePage === 'question-analysis' }"
            @click="activePage = 'question-analysis'"
          >질문분석</button>

          <button
            class="page-tab show-on-mobile"
            :class="{ active: activePage === 'chat-test' }"
            @click="activePage = 'chat-test'"
          >채팅</button>

          <button
            v-if="showUserManagement"
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
      <KBManagement :key="activePage" />
    </div>

    <div v-if="activePage === 'logs'" class="kb-page">
      <ChatLogManagement :key="activePage">
        <template #detail-actions>
          <button class="btn btn-chat-mgmt" type="button" @click="openChatbotPromptEditor">
            Chat 프롬프트
          </button>
          <button class="btn btn-chat-mgmt" type="button" @click="openChatWidgetHtmlExample">
            HTML Chat 예시
          </button>
        </template>
      </ChatLogManagement>
    </div>


    <div v-if="activePage === 'question-analysis'" class="kb-page">
      <!-- 내부 탭 바 -->
      <div class="analysis-tab-bar">
        <button
          class="analysis-tab-btn"
          :class="{ active: activeAnalysisTab === 'report' }"
          @click="activeAnalysisTab = 'report'"
        >질문분석 리포트</button>
        <button
          class="analysis-tab-btn"
          :class="{ active: activeAnalysisTab === 'low-similarity' }"
          @click="activeAnalysisTab = 'low-similarity'"
        >저유사도 문의 관리</button>
      </div>

      <div v-if="activeAnalysisTab === 'report'" class="summary-panel-wrapper">
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
              <option v-for="p in summaryPlatformOptions" :key="`summary-platform-${p}`" :value="p">{{ p }}</option>
            </select>
          </div>
        </div>

        <div v-if="loadingSummary" class="loading">
          질문 분석 데이터 로딩 중...
        </div>

        <div v-else-if="questionSummary" class="question-summary-content">
          <div class="analysis-title-row">
            <h3>질문 분석 리포트</h3>
            <p>조회기간 {{ summaryDays }}일 기준 집계</p>
            <div class="analysis-meta-chips">
              <span class="analysis-meta-chip">총 질문 {{ questionSummary.totalQuestions || 0 }}건</span>
              <span class="analysis-meta-chip">참조 KB {{ questionSummary.uniqueReferencedKbs || 0 }}건</span>
            </div>
          </div>

          <!-- 답변 품질 대시보드 -->
          <div class="quality-dashboard">
            <h4 class="quality-title">답변 품질 대시보드</h4>
            <div class="quality-kpi-row">
              <div class="quality-kpi-card">
                <div class="quality-kpi-label">총 답변 수</div>
                <div class="quality-kpi-value">{{ questionSummary.totalAnswers ?? 0 }}</div>
              </div>
              <div class="quality-kpi-card" :class="{ 'kpi-warn': (questionSummary.lowSimilarityRate ?? 0) > 0.15 }">
                <div class="quality-kpi-label">미매칭 비율</div>
                <div class="quality-kpi-value">{{ ((questionSummary.lowSimilarityRate ?? 0) * 100).toFixed(1) }}%</div>
                <div class="quality-kpi-sub">{{ questionSummary.lowSimilarityCount ?? 0 }}건</div>
              </div>
              <div class="quality-kpi-card">
                <div class="quality-kpi-label">평균 유사도</div>
                <div class="quality-kpi-value">{{ (questionSummary.avgSimilarity ?? 0).toFixed(3) }}</div>
              </div>
              <div class="quality-kpi-card" :class="{ 'kpi-good': (questionSummary.highConfidenceRate ?? 0) >= 0.6 }">
                <div class="quality-kpi-label">고신뢰 비율</div>
                <div class="quality-kpi-value">{{ ((questionSummary.highConfidenceRate ?? 0) * 100).toFixed(1) }}%</div>
                <div class="quality-kpi-sub">score ≥ 0.82</div>
              </div>
            </div>

            <!-- 유사도 분포 히스토그램 -->
            <div v-if="questionSummary.similarityDistribution && questionSummary.totalAnswers > 0" class="similarity-dist">
              <div class="dist-title">유사도 분포</div>
              <div class="dist-bars">
                <div
                  v-for="item in questionSummary.similarityDistribution"
                  :key="item.range"
                  class="dist-row"
                >
                  <div class="dist-label">{{ item.range }}</div>
                  <div class="dist-bar-wrap">
                    <div
                      class="dist-bar"
                      :style="{ width: questionSummary.totalAnswers > 0 ? (item.count / questionSummary.totalAnswers * 100) + '%' : '0%' }"
                      :class="item.range === '~0.5' ? 'bar-danger' : item.range.startsWith('0.9') ? 'bar-success' : 'bar-normal'"
                    ></div>
                  </div>
                  <div class="dist-count">{{ item.count }}</div>
                </div>
              </div>
            </div>
          </div>

          <div class="kpi-cards">
            <div class="kpi-card">
              <div class="kpi-label">총 질문 수</div>
              <div class="kpi-value">{{ questionSummary.totalQuestions || 0 }}</div>
            </div>
            <div class="kpi-card">
              <div class="kpi-label">참조 KB 수</div>
              <div class="kpi-value">{{ questionSummary.uniqueReferencedKbs || 0 }}</div>
            </div>
            <div class="kpi-card" :class="{ 'kpi-warn': (questionSummary.avgSimilarity ?? 0) < 0.7, 'kpi-good': (questionSummary.avgSimilarity ?? 0) >= 0.82 }">
              <div class="kpi-label">요청 상위 기준</div>
              <div class="kpi-value">{{ (questionSummary.avgSimilarity ?? 0).toFixed(3) }}</div>
              <div class="kpi-sub">평균 유사도</div>
            </div>
            <div class="kpi-card" :class="{ 'kpi-warn': (questionSummary.lowSimilarityRate ?? 0) > 0.15, 'kpi-good': (questionSummary.lowSimilarityRate ?? 0) <= 0.08 }">
              <div class="kpi-label">집계 일수</div>
              <div class="kpi-value">{{ ((questionSummary.lowSimilarityRate ?? 0) * 100).toFixed(1) }}%</div>
              <div class="kpi-sub">미매칭 비율</div>
            </div>
          </div>

          <div class="insight-grid">
            <!-- 상위 참조 KB 섹션 -->
            <div class="top-questions-section">
              <h3>상위 {{ summaryTop }} 참조 KB</h3>
              <div v-if="questionSummary.topReferencedKbs && questionSummary.topReferencedKbs.length > 0" class="questions-list">
                <div v-for="(kb, idx) in questionSummary.topReferencedKbs" :key="`kb-${kb.kbId || kb.KbId || idx}`" class="question-item">
                  <div class="question-rank">{{ idx + 1 }}</div>
                  <div class="question-info">
                    <div class="question-text">{{ kb.title || kb.Title || '제목 없음' }}</div>
                    <div class="question-meta">
                      <span class="meta-item">KB ID: #{{ kb.kbId || kb.KbId || '-' }}</span>
                      <span class="meta-item">참조 수: {{ kb.count || kb.Count || 0 }}</span>
                      <span class="meta-item">최근 참조: {{ formatDateTime(kb.lastReferencedAt || kb.LastReferencedAt) }}</span>
                    </div>
                    <div class="question-actions">
                      <button class="open-kb-btn" type="button" @click="openKbFromSummary(kb)">KB 상세보기</button>
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
          </div>

          <!-- 일일 질문 수 섹션 -->
          <div v-if="questionSummary.dailyCounts" class="daily-counts-section">
            <h3>일일 질문 수</h3>
            <div v-if="dailyTrendSeries.length > 1" class="daily-trend-chart-wrap">
              <svg viewBox="0 0 100 44" preserveAspectRatio="none" class="daily-trend-chart" aria-label="일일 질문 수 추이">
                <polyline :points="dailyTrendPolyline" fill="none" stroke="url(#dailyLineGradient)" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
                <circle
                  v-for="(pt, idx) in dailyTrendPoints"
                  :key="`daily-point-${idx}`"
                  :cx="pt.x"
                  :cy="pt.y"
                  r="1.3"
                  fill="#3b82f6"
                />
                <defs>
                  <linearGradient id="dailyLineGradient" x1="0" y1="0" x2="1" y2="0">
                    <stop offset="0%" stop-color="#8eb8ff" />
                    <stop offset="100%" stop-color="#2563eb" />
                  </linearGradient>
                </defs>
              </svg>
            </div>
            <div class="daily-counts-list">
              <div v-for="(item, idx) in questionSummary.dailyCounts" :key="idx" class="daily-item">
                <div class="daily-date">{{ formatDate(item.date || item.Date) }}</div>
                <div class="daily-bar-wrap">
                  <div class="daily-bar" :style="{ width: (((item.count ?? item.Count ?? 0) / maxDailyQuestionCount) * 100) + '%' }"></div>
                </div>
                <div class="daily-count">{{ item.count || item.Count || 0 }}건</div>
              </div>
            </div>
          </div>
        </div>

        <div v-else class="empty-state">
          분석할 데이터가 없습니다.
        </div>
      </div>

      <div v-if="activeAnalysisTab === 'low-similarity'">
        <LowSimilarityManagement :key="activeAnalysisTab" />
      </div>
    </div>
    <div v-if="activePage === 'chat-test'" class="kb-page chat-test-page">
      <InlineChatbot :key="activePage" role="admin" />
    </div>

    <div v-if="showUserManagement && activePage === 'user-management'" class="kb-page">
      <UserApproval :key="activePage" />
    </div>

    <div v-if="showChatbotPromptEditor" class="modal-overlay" @click="showChatbotPromptEditor = false">
      <div class="modal-box" @click.stop>
        <div class="modal-header">
          <h3>챗봇 프롬프트 수정</h3>
          <button class="modal-close-btn" @click="showChatbotPromptEditor = false">✕</button>
        </div>

        <div class="modal-body chatbot-prompt-body">
          <div class="prompt-section">
            <div class="prompt-section-header">
              <h4>사용자 챗봇</h4>
              <p>일반 사용자에게 보이는 답변 톤, 규칙, 저유사도 안내문을 설정합니다.</p>
            </div>
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

          <div class="prompt-section">
            <div class="prompt-section-header">
              <h4>관리자 챗봇</h4>
              <p>관리자 화면에서 사용하는 답변 규칙과 저유사도 응답을 분리해서 설정합니다.</p>
            </div>
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

          <div class="prompt-section prompt-section-compact">
            <div class="prompt-section-header">
              <h4>검색 임계치</h4>
              <p>이 값보다 낮으면 KB 근거가 부족한 것으로 판단하고 저유사도 안내문을 반환합니다.</p>
            </div>
            <div class="form-group threshold-group">
              <label>유사도 임계치 (0~1)</label>
              <input v-model.number="chatbotPromptTemplateForm.similarityThreshold" type="number" min="0.1" max="0.95" step="0.01" />
            </div>
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

  </div>
</template>

<style scoped>
/* =====================================================
   컨테이너
   ===================================================== */
.crm-container {
  --content-max-width: 2048px;
  width: 100%;
  padding: 0 16px 40px;
}

/* =====================================================
   헤더
   ===================================================== */
.crm-header {
  margin-left: -16px;
  margin-right: -16px;
  margin-bottom: 32px;
}

.crm-header-shell {
  background: linear-gradient(135deg, #0d6efd 0%, #764ba2 100%);
  border-radius: 0;
  padding: 28px 16px 0;
  color: #fff;
  box-shadow: 0 4px 24px rgba(13, 110, 253, 0.18);
}

.crm-header-topline {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.header-user-info {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-username-btn {
  border: none;
  background: transparent;
  color: #fff;
  font-size: 14px;
  font-weight: 700;
  cursor: pointer;
  padding: 0;
  opacity: 0.9;
}

.header-username-btn:hover {
  opacity: 1;
  text-decoration: underline;
}

.header-logout-btn {
  padding: 6px 14px;
  background: rgba(255, 255, 255, 0.2);
  color: #fff;
  border: 1px solid rgba(255, 255, 255, 0.6);
  border-radius: 6px;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.18s;
}

.header-logout-btn:hover {
  background: rgba(255, 255, 255, 0.3);
}

.crm-header-badge {
  display: inline-block;
  background: rgba(255, 255, 255, 0.22);
  border: 1px solid rgba(255, 255, 255, 0.35);
  border-radius: 20px;
  padding: 3px 12px;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
}

.crm-header-shell h1 {
  margin: 0 0 4px;
  font-size: 26px;
  font-weight: 700;
}

.crm-header-shell > p {
  margin: 0 0 20px;
  font-size: 14px;
  opacity: 0.82;
}

/* 페이지 탭 */
.page-tabs {
  display: flex;
  gap: 4px;
  margin-top: 4px;
}

.page-tab {
  padding: 10px 22px;
  border: none;
  border-radius: 10px 10px 0 0;
  background: rgba(255, 255, 255, 0.15);
  color: rgba(255, 255, 255, 0.85);
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.18s, color 0.18s;
}

.page-tab:hover {
  background: rgba(255, 255, 255, 0.25);
  color: #fff;
}

.page-tab.active {
  background: #fff;
  color: #0d6efd;
}

.show-on-mobile {
  display: none;
}

.chat-test-page {
  display: flex;
  justify-content: center;
  padding: 16px 0;
}

/* =====================================================
   본문 공통
   ===================================================== */
.kb-page {
  margin-top: 0;
  max-width: var(--content-max-width);
  margin-left: auto;
  margin-right: auto;
}

.error-message {
  background: #fff0f0;
  border: 1px solid #fca5a5;
  color: #b91c1c;
  padding: 12px 16px;
  border-radius: 8px;
  margin-bottom: 16px;
  font-size: 14px;
}

.btn {
  padding: 8px 16px;
  border-radius: 8px;
  border: none;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.15s;
}

.btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.btn-chat-mgmt {
  color: #1f5aa8;
  border: 1px solid #a9c6f3;
  border-radius: 10px;
  padding: 8px 12px;
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.1px;
  background: #ffffff;
  box-shadow: none;
  transition: transform 0.14s ease, border-color 0.18s ease;
}

.btn-chat-mgmt:hover:not(:disabled) {
  border-color: #8db4eb;
  background: #ffffff;
  box-shadow: none;
  transform: translateY(-1px);
  opacity: 1;
}

.btn-chat-mgmt:active:not(:disabled) {
  transform: translateY(0);
  box-shadow: none;
}

.btn-chat-mgmt:focus-visible {
  outline: none;
  box-shadow: 0 0 0 3px rgba(13, 110, 253, 0.14);
}

.btn-primary {
  background: #0d6efd;
  color: #fff;
}

.btn-primary:hover:not(:disabled) {
  opacity: 0.88;
}

.btn-secondary {
  background: #e2e8f0;
  color: #334155;
}

.btn-secondary:hover:not(:disabled) {
  background: #cbd5e1;
}

/* =====================================================
   분석 탭 바
   ===================================================== */
.analysis-tab-bar {
  display: flex;
  gap: 4px;
  border-bottom: 2px solid #e2e8f0;
  margin-bottom: 20px;
}

.analysis-tab-btn {
  padding: 8px 20px;
  border: none;
  background: transparent;
  color: #64748b;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
  transition: color 0.15s, border-color 0.15s;
}

.analysis-tab-btn:hover {
  color: #0d6efd;
}

.analysis-tab-btn.active {
  color: #0d6efd;
  border-bottom-color: #0d6efd;
}

/* =====================================================
   질문분석 컨트롤
   ===================================================== */
.summary-panel-wrapper {
  background: #fff;
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 1px 6px rgba(0, 0, 0, 0.07);
}

.summary-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-bottom: 20px;
}

.control-group {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 14px;
}

.control-group label {
  font-weight: 500;
  color: #475569;
}

.control-input {
  padding: 6px 10px;
  border: 1px solid #e2e8f0;
  border-radius: 7px;
  font-size: 14px;
  background: #f8fafc;
}

.loading {
  text-align: center;
  color: #64748b;
  padding: 32px 0;
  font-size: 14px;
}

/* =====================================================
   KPI 카드
   ===================================================== */
.question-summary-content {
  margin-top: 4px;
  display: grid;
  gap: 20px;
}

.analysis-title-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 8px;
}

.analysis-title-row h3 {
  margin: 0;
  font-size: 17px;
  font-weight: 700;
  color: #1e293b;
}

.analysis-title-row p {
  margin: 0;
  font-size: 13px;
  color: #64748b;
}

.analysis-meta-chips {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-left: auto;
  flex-wrap: wrap;
}

.analysis-meta-chip {
  display: inline-flex;
  align-items: center;
  border: 1px solid #dbe6f2;
  border-radius: 999px;
  background: #f8fbff;
  color: #1d4ed8;
  font-size: 12px;
  font-weight: 700;
  padding: 5px 10px;
}

/* =====================================================
   답변 품질 대시보드
   ===================================================== */
.quality-dashboard {
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 12px;
  padding: 18px;
}

.quality-title {
  margin: 0 0 14px;
  font-size: 15px;
  font-weight: 700;
  color: #1e293b;
}

.quality-kpi-row {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 18px;
}

.quality-kpi-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 14px 16px;
  text-align: center;
}

.quality-kpi-card.kpi-warn {
  background: #fff7ed;
  border-color: #fed7aa;
}

.quality-kpi-card.kpi-good {
  background: #f0fdf4;
  border-color: #bbf7d0;
}

.quality-kpi-card.kpi-danger {
  background: #fff1f2;
  border-color: #fecdd3;
}

.quality-kpi-label {
  font-size: 12px;
  color: #64748b;
  margin-bottom: 6px;
}

.quality-kpi-value {
  font-size: 24px;
  font-weight: 700;
  color: #1e293b;
}

.quality-kpi-sub {
  font-size: 11px;
  color: #94a3b8;
  margin-top: 3px;
}

.similarity-dist {
  border-top: 1px solid #f1f5f9;
  padding-top: 14px;
}

.dist-title {
  font-size: 13px;
  font-weight: 600;
  color: #475569;
  margin-bottom: 10px;
}

.dist-bars {
  display: flex;
  flex-direction: column;
  gap: 7px;
}

.dist-row {
  display: grid;
  grid-template-columns: 72px 1fr 40px;
  align-items: center;
  gap: 10px;
}

.dist-label {
  font-size: 12px;
  color: #64748b;
  text-align: right;
}

.dist-bar-wrap {
  background: #f1f5f9;
  border-radius: 4px;
  height: 14px;
  overflow: hidden;
}

.dist-bar {
  height: 100%;
  border-radius: 4px;
  min-width: 2px;
  transition: width 0.4s ease;
}

.dist-bar.bar-success { background: #22c55e; }
.dist-bar.bar-normal  { background: #60a5fa; }
.dist-bar.bar-danger  { background: #f87171; }

.dist-count {
  font-size: 12px;
  color: #475569;
  text-align: right;
}

.kpi-cards {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.kpi-card {
  background: linear-gradient(180deg, #fbfdff 0%, #f3f8ff 100%);
  border: 1px solid #dbe6f2;
  border-radius: 10px;
  padding: 16px 18px;
}

.kpi-card.kpi-warn {
  background: #fff7ed;
  border-color: #fed7aa;
}

.kpi-card.kpi-good {
  background: #f0fdf4;
  border-color: #bbf7d0;
}

.insight-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 12px;
}

.top-questions-section,
.keywords-section,
.daily-counts-section {
  border: 1px solid #e2e8f0;
  border-radius: 12px;
  padding: 18px;
  background: #fff;
}

.top-questions-section h3,
.keywords-section h3,
.daily-counts-section h3 {
  margin: 0 0 14px;
  font-size: 15px;
  font-weight: 700;
  color: #1e293b;
}

.questions-list {
  display: grid;
  gap: 10px;
}

.question-item {
  display: grid;
  grid-template-columns: 40px 1fr;
  gap: 12px;
  align-items: start;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 12px 14px;
  background: #f8fafc;
}

.question-rank {
  width: 32px;
  height: 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  background: #e7f1ff;
  color: #0d6efd;
  font-size: 13px;
  font-weight: 800;
}

.question-info {
  min-width: 0;
}

.question-text {
  font-size: 14px;
  font-weight: 700;
  color: #1e293b;
  margin-bottom: 6px;
}

.question-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.question-actions {
  margin-top: 8px;
}

.open-kb-btn {
  border: 1px solid #bcd4fb;
  border-radius: 8px;
  background: #f3f8ff;
  color: #1d4ed8;
  font-size: 12px;
  font-weight: 700;
  padding: 6px 10px;
  cursor: pointer;
}

.open-kb-btn:hover {
  background: #e9f1ff;
  border-color: #93baf5;
}

.meta-item {
  font-size: 12px;
  color: #64748b;
}

.keywords-cloud {
  display: flex;
  flex-wrap: nowrap;
  gap: 10px;
  overflow-x: auto;
  padding-bottom: 2px;
  scrollbar-width: thin;
}

.keyword-tag {
  display: inline-flex;
  align-items: center;
  flex: 0 0 auto;
  gap: 6px;
  border-radius: 999px;
  padding: 8px 12px;
  border: 1px solid #cfe2ff;
  background: #e7f1ff;
  color: #0d6efd;
  font-size: 13px;
  font-weight: 700;
}

.keyword-tag strong {
  color: #1d4ed8;
}

.daily-counts-list {
  display: grid;
  gap: 8px;
}

.daily-trend-chart-wrap {
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  background: #fbfdff;
  padding: 10px 12px 8px;
  margin-bottom: 10px;
}

.daily-trend-chart {
  width: 100%;
  height: 88px;
  display: block;
}

.daily-item {
  display: grid;
  grid-template-columns: 112px 1fr auto;
  align-items: center;
  gap: 10px;
  border: 1px dashed #d0d7de;
  border-radius: 10px;
  padding: 10px 12px;
  background: #f8fafc;
}

.daily-date {
  font-size: 13px;
  color: #334155;
  font-weight: 600;
}

.daily-count {
  font-size: 13px;
  color: #0d6efd;
  font-weight: 700;
  min-width: 54px;
  text-align: right;
}

.daily-bar-wrap {
  height: 8px;
  border-radius: 999px;
  background: #e5edf7;
  overflow: hidden;
}

.daily-bar {
  height: 100%;
  min-width: 2px;
  border-radius: 999px;
  background: linear-gradient(90deg, #9cc3ff 0%, #3b82f6 100%);
}

.empty-state {
  padding: 18px;
  border: 1px dashed #d0d7de;
  border-radius: 10px;
  background: #f8fafc;
  color: #64748b;
  font-size: 14px;
  text-align: center;
}

.kpi-label {
  font-size: 12px;
  color: #64748b;
  margin-bottom: 6px;
  font-weight: 500;
}

.kpi-value {
  font-size: 24px;
  font-weight: 700;
  color: #0f172a;
}

/* =====================================================
   차트/테이블
   ===================================================== */
.charts-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
  gap: 20px;
}

.chart-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 16px 18px;
}

.chart-card h4 {
  margin: 0 0 12px;
  font-size: 14px;
  font-weight: 700;
  color: #1e293b;
}

.chart-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.chart-table th,
.chart-table td {
  padding: 6px 8px;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
}

.chart-table th {
  color: #64748b;
  font-weight: 600;
}

.chart-table td {
  color: #1e293b;
}

/* =====================================================
   챗봇 프롬프트 모달
   ===================================================== */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.35);
  z-index: 3000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}

.modal-box {
  width: 100%;
  max-width: 740px;
  max-height: 90vh;
  overflow-y: auto;
  background: #fff;
  border-radius: 12px;
  box-shadow: 0 14px 44px rgba(15, 23, 42, 0.24);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid #eef2f7;
  position: sticky;
  top: 0;
  background: #fff;
  z-index: 1;
}

.modal-header h3 {
  margin: 0;
  font-size: 17px;
  font-weight: 700;
}

.modal-close-btn {
  border: 1px solid #d0d7de;
  border-radius: 8px;
  padding: 6px 12px;
  background: #fff;
  cursor: pointer;
  font-size: 13px;
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.prompt-section {
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  padding: 16px;
  background: #fff;
}

.prompt-section-header {
  margin: 0 0 14px;
}

.prompt-section h4 {
  margin: 0 0 6px;
  font-size: 14px;
  font-weight: 700;
  color: #1e293b;
}

.prompt-section-header p {
  margin: 0;
  font-size: 12px;
  line-height: 1.5;
  color: #64748b;
}

.chatbot-prompt-body .prompt-section:nth-child(1) .prompt-section-header {
  background: #eef2ff;
  color: #1e3a8a;
  margin: -16px -16px 14px;
  padding: 12px 16px;
  border-bottom: 1px solid #c7d2fe;
}

.chatbot-prompt-body .prompt-section:nth-child(1) .prompt-section-header p {
  color: rgba(30, 58, 138, 0.78);
}

.chatbot-prompt-body .prompt-section:nth-child(2) .prompt-section-header {
  background: #ecfdf5;
  color: #166534;
  margin: -16px -16px 14px;
  padding: 12px 16px;
  border-bottom: 1px solid #bbf7d0;
}

.chatbot-prompt-body .prompt-section:nth-child(2) .prompt-section-header p {
  color: rgba(22, 101, 52, 0.78);
}

.chatbot-prompt-body .prompt-section:nth-child(3) .prompt-section-header {
  background: #fff7ed;
  color: #9a3412;
  margin: -16px -16px 14px;
  padding: 12px 16px;
  border-bottom: 1px solid #fed7aa;
}

.chatbot-prompt-body .prompt-section:nth-child(3) .prompt-section-header p {
  color: rgba(154, 52, 18, 0.78);
}

.form-group {
  margin-bottom: 14px;
}

.prompt-section .form-group:last-child {
  margin-bottom: 0;
}

.prompt-section-compact {
  background: #fbfcfe;
}

.threshold-group {
  max-width: 220px;
}

.form-group label {
  display: block;
  font-size: 13px;
  font-weight: 600;
  color: #475569;
  margin-bottom: 5px;
}

.form-group textarea,
.form-group input[type="number"] {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #e2e8f0;
  border-radius: 7px;
  font-size: 13px;
  resize: vertical;
  font-family: inherit;
  box-sizing: border-box;
}

.form-group textarea:focus,
.form-group input[type="number"]:focus {
  outline: none;
  border-color: #0d6efd;
  box-shadow: 0 0 0 3px rgba(13, 110, 253, 0.12);
}

.modal-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
  padding: 14px 20px;
  border-top: 1px solid #eef2f7;
  position: sticky;
  bottom: 0;
  background: #fff;
}

@media (max-width: 960px) {
  .crm-header-topline {
    flex-direction: column;
    align-items: flex-start;
    gap: 10px;
  }

  .page-tabs {
    flex-wrap: wrap;
  }

  .kpi-cards {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .insight-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .crm-container {
    padding: 0 8px 28px;
  }

  .crm-header {
    margin-left: -8px;
    margin-right: -8px;
  }

  .crm-header-shell {
    padding: 16px 12px 0;
  }

  .crm-header-topline {
    flex-direction: row;
    align-items: center;
    justify-content: space-between;
    gap: 6px;
    margin-bottom: 6px;
  }

  .crm-header-badge {
    font-size: 11px;
    padding: 2px 8px;
  }

  .header-user-info {
    gap: 6px;
    flex-shrink: 0;
  }

  .header-username-btn {
    font-size: 12px;
    max-width: 80px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .header-logout-btn {
    padding: 4px 10px;
    font-size: 12px;
  }

  .crm-header-shell h1 {
    font-size: 18px;
    margin-bottom: 2px;
  }

  .crm-header-shell > p {
    font-size: 12px;
    margin-bottom: 12px;
    opacity: 0.75;
  }

  .page-tabs {
    gap: 2px;
  }

  .page-tab {
    padding: 8px 12px;
    font-size: 13px;
    flex: 1;
    text-align: center;
    min-width: 0;
  }

  .hide-on-mobile {
    display: none;
  }

  .show-on-mobile {
    display: inline-flex;
    justify-content: center;
  }

  .summary-controls {
    align-items: stretch;
  }

  .control-group {
    width: 100%;
    justify-content: space-between;
  }

  .control-input {
    width: 100%;
  }

  .analysis-meta-chips {
    margin-left: 0;
    width: 100%;
  }

  .kpi-cards {
    grid-template-columns: 1fr;
  }

  .question-item {
    grid-template-columns: 1fr;
  }

  .daily-item {
    grid-template-columns: 1fr;
    gap: 6px;
  }

  .daily-count {
    text-align: left;
  }
}
</style>
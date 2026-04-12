<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import axios from 'axios'

const API_URL = 'http://localhost:8080/api'

const loading = ref(false)
const sessions = ref([])
const sessionPage = ref(1)
const sessionPageSize = ref(10)
const sessionTotal = ref(0)
const sessionPageSizeOptions = [10, 20, 50, 100]
const roleFilter = ref('all')
const platformFilter = ref('all')
const platformOptions = ref(['공통'])
const selectedSessionId = ref(null)
const selectedSession = ref(null)
const loadingDetail = ref(false)
const showKbModal = ref(false)
const loadingKbDetail = ref(false)
const selectedKbDetail = ref(null)
const selectedKbSimilarity = ref(null)
const deletingSessionId = ref(null)
const loadingSummary = ref(false)
const summaryDays = ref(7)
const summaryTop = ref(10)
const questionSummary = ref(null)

const sessionTotalPages = computed(() => Math.max(1, Math.ceil(sessionTotal.value / sessionPageSize.value)))

async function fetchSessions() {
  loading.value = true
  try {
    const params = {
      page: sessionPage.value,
      pageSize: sessionPageSize.value
    }
    if (roleFilter.value !== 'all') params.role = roleFilter.value
    if (platformFilter.value !== 'all') params.platform = platformFilter.value
    const res = await axios.get(`${API_URL}/chat/sessions`, { params })
    sessions.value = res.data?.data || []
    sessionTotal.value = Number(res.data?.total || 0)
  } finally {
    loading.value = false
  }
}

function goSessionPage(page) {
  const next = Math.min(Math.max(1, page), sessionTotalPages.value)
  if (next === sessionPage.value) return
  sessionPage.value = next
  fetchSessions()
}

async function fetchPlatforms() {
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/platforms`)
    const list = Array.isArray(res.data) ? res.data : []
    const normalized = list
      .map((x) => (typeof x === 'string' ? x.trim() : ''))
      .filter(Boolean)
    platformOptions.value = Array.from(new Set(['공통', ...normalized]))

    if (platformFilter.value !== 'all' && !platformOptions.value.includes(platformFilter.value)) {
      platformFilter.value = 'all'
    }
  } catch {
    platformOptions.value = ['공통']
    platformFilter.value = 'all'
  }
}

async function fetchQuestionSummary() {
  loadingSummary.value = true
  try {
    const params = {
      days: summaryDays.value,
      top: summaryTop.value
    }
    if (roleFilter.value !== 'all') params.role = roleFilter.value
    if (platformFilter.value !== 'all') params.platform = platformFilter.value

    const res = await axios.get(`${API_URL}/chat/questions-summary`, { params })
    questionSummary.value = res.data || null
  } catch {
    questionSummary.value = null
  } finally {
    loadingSummary.value = false
  }
}

async function loadSessionDetail(id) {
  selectedSessionId.value = id
  loadingDetail.value = true
  try {
    const res = await axios.get(`${API_URL}/chat/sessions/${id}`)
    selectedSession.value = res.data
  } finally {
    loadingDetail.value = false
  }
}

async function deleteSession(id) {
  if (!confirm('이 채팅 세션을 삭제하시겠습니까?')) return

  deletingSessionId.value = id
  try {
    await axios.delete(`${API_URL}/chat/sessions/${id}`)

    if (selectedSessionId.value === id) {
      selectedSessionId.value = null
      selectedSession.value = null
    }

    if (sessions.value.length === 1 && sessionPage.value > 1) {
      sessionPage.value -= 1
    }

    await fetchSessions()
    await fetchQuestionSummary()
  } catch {
    alert('세션 삭제에 실패했습니다.')
  } finally {
    deletingSessionId.value = null
  }
}

function parseRelatedKbIds(value) {
  if (!value) return []
  try {
    const parsed = JSON.parse(value)
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

async function openKbDetail(id, similarity = null) {
  loadingKbDetail.value = true
  showKbModal.value = true
  selectedKbDetail.value = null
  selectedKbSimilarity.value = typeof similarity === 'number' ? similarity : null
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/${id}`)
    selectedKbDetail.value = res.data
  } catch {
    selectedKbDetail.value = { error: 'KB 상세를 불러오지 못했습니다.' }
  } finally {
    loadingKbDetail.value = false
  }
}

function closeKbModal() {
  showKbModal.value = false
  selectedKbDetail.value = null
  selectedKbSimilarity.value = null
}

function formatSimilarity(value) {
  if (typeof value !== 'number' || Number.isNaN(value)) return '-'
  return `${Math.round(value * 100)}%`
}

function formatDateTime(value) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '-'
  return date.toLocaleString('ko-KR')
}

function formatDate(value) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '-'
  return date.toLocaleDateString('ko-KR')
}

function getKbPlatforms(detail) {
  if (!detail) return ['공통']
  if (Array.isArray(detail.platforms) && detail.platforms.length > 0) {
    return detail.platforms
  }
  if (typeof detail.platform === 'string' && detail.platform.trim()) {
    return detail.platform.split(',').map((x) => x.trim()).filter(Boolean)
  }
  return ['공통']
}

function getKbTags(detail) {
  if (!detail || typeof detail.tags !== 'string') return []
  return detail.tags.split(',').map((x) => x.trim()).filter(Boolean)
}

watch([roleFilter, platformFilter, sessionPageSize], () => {
  sessionPage.value = 1
  fetchSessions()
})

watch([roleFilter, platformFilter, summaryDays, summaryTop], () => {
  fetchQuestionSummary()
})

onMounted(async () => {
  await fetchPlatforms()
  await fetchSessions()
  await fetchQuestionSummary()
})
</script>

<template>
  <section class="chat-log-wrap">
    <div class="panel list-panel">
      <div class="panel-head">
        <h3>채팅 세션</h3>
        <div class="toolbar">
          <select v-model="roleFilter">
            <option value="all">전체</option>
            <option value="user">사용자 챗봇</option>
            <option value="admin">관리자 챗봇</option>
          </select>
          <select v-model="platformFilter">
            <option value="all">플랫폼 전체</option>
            <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
          </select>
          <select v-model.number="sessionPageSize">
            <option v-for="size in sessionPageSizeOptions" :key="`size-${size}`" :value="size">{{ size }}개씩</option>
          </select>
          <button class="ghost refresh-fit" :disabled="loading" @click="fetchSessions">새로고침</button>
        </div>
      </div>

      <div v-if="loading" class="empty">불러오는 중...</div>
      <div v-else-if="sessions.length === 0" class="empty">세션이 없습니다.</div>

      <div v-else class="session-list">
        <article
          v-for="session in sessions"
          :key="session.id"
          class="session-item"
          :class="{ selected: selectedSessionId === session.id }"
          @click="loadSessionDetail(session.id)"
        >
          <div class="session-title">{{ session.title || `세션 #${session.id}` }}</div>
          <div class="session-meta">
            <span class="badge" :class="session.userRole">{{ session.userRole === 'admin' ? '관리자' : '사용자' }}</span>
            <span class="badge platform">{{ session.platform || '공통' }}</span>
            <span>메시지 {{ session.messageCount }}</span>
            <span>{{ new Date(session.updatedAt).toLocaleString('ko-KR') }}</span>
          </div>

          <div class="session-actions">
            <button
              class="danger-mini"
              type="button"
              :disabled="deletingSessionId === session.id"
              @click.stop="deleteSession(session.id)"
            >
              {{ deletingSessionId === session.id ? '삭제 중...' : '세션 삭제' }}
            </button>
          </div>
        </article>
      </div>

      <div v-if="!loading && sessionTotal > 0" class="pager">
        <button class="ghost" :disabled="sessionPage <= 1" @click="goSessionPage(1)">처음</button>
        <button class="ghost" :disabled="sessionPage <= 1" @click="goSessionPage(sessionPage - 1)">이전</button>
        <span>{{ sessionPage }} / {{ sessionTotalPages }} (총 {{ sessionTotal }}건)</span>
        <button class="ghost" :disabled="sessionPage >= sessionTotalPages" @click="goSessionPage(sessionPage + 1)">다음</button>
        <button class="ghost" :disabled="sessionPage >= sessionTotalPages" @click="goSessionPage(sessionTotalPages)">마지막</button>
      </div>
    </div>

    <div class="panel summary-panel">
      <div class="panel-head">
        <h3>기간별 질문 분석</h3>
        <div class="toolbar summary-toolbar">
          <select v-model.number="summaryDays">
            <option :value="7">최근 7일</option>
            <option :value="30">최근 30일</option>
            <option :value="90">최근 90일</option>
          </select>
          <select v-model.number="summaryTop">
            <option :value="5">Top 5</option>
            <option :value="10">Top 10</option>
            <option :value="20">Top 20</option>
          </select>
          <button class="ghost refresh-fit" :disabled="loadingSummary" @click="fetchQuestionSummary">새로고침</button>
        </div>
      </div>

      <div v-if="loadingSummary" class="empty">질문 통계를 계산하는 중...</div>
      <div v-else-if="!questionSummary || !questionSummary.totalQuestions" class="empty">선택한 기간에 질문 데이터가 없습니다.</div>

      <div v-else class="summary-grid">
        <div class="summary-kpi-row">
          <div class="kpi-card">
            <div class="kpi-label">총 질문 수</div>
            <div class="kpi-value">{{ questionSummary.totalQuestions }}</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-label">중복 제거 질문 수</div>
            <div class="kpi-value">{{ questionSummary.uniqueQuestions }}</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-label">분석 기간</div>
            <div class="kpi-value small">{{ formatDate(questionSummary.from) }} ~ {{ formatDate(questionSummary.to) }}</div>
          </div>
        </div>

        <div class="summary-block">
          <h4>가장 많이 물어본 질문</h4>
          <div v-if="!questionSummary.topQuestions?.length" class="empty small">집계된 질문이 없습니다.</div>
          <ol v-else class="top-question-list">
            <li v-for="(item, idx) in questionSummary.topQuestions" :key="`q-${idx}`">
              <div class="top-question-text">{{ item.question }}</div>
              <div class="top-question-meta">{{ item.count }}회 · 최근 {{ formatDateTime(item.lastAskedAt) }}</div>
            </li>
          </ol>
        </div>

        <div class="summary-block">
          <h4>질문 키워드 요약</h4>
          <div v-if="!questionSummary.topKeywords?.length" class="empty small">요약할 키워드가 없습니다.</div>
          <div v-else class="keyword-list">
            <span v-for="item in questionSummary.topKeywords" :key="`k-${item.keyword}`" class="keyword-chip">
              #{{ item.keyword }} ({{ item.count }})
            </span>
          </div>
        </div>

        <div class="summary-block">
          <h4>일자별 질문 수</h4>
          <div v-if="!questionSummary.dailyCounts?.length" class="empty small">일자별 데이터가 없습니다.</div>
          <div v-else class="daily-list">
            <div v-for="item in questionSummary.dailyCounts" :key="`d-${item.date}`" class="daily-item">
              <span>{{ item.date }}</span>
              <strong>{{ item.count }}건</strong>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div class="panel detail-panel">
      <div class="panel-head">
        <h3>대화 상세</h3>
      </div>

      <div v-if="!selectedSessionId" class="empty">왼쪽에서 세션을 선택하세요.</div>
      <div v-else-if="loadingDetail" class="empty">세부 내역을 불러오는 중...</div>
      <div v-else-if="!selectedSession" class="empty">세부 내역이 없습니다.</div>

      <div v-else class="message-list">
        <article v-for="msg in selectedSession.messages || []" :key="msg.id" class="message-item" :class="msg.role">
          <div class="message-head">
            <strong>{{ msg.role === 'user' ? '질문' : '답변' }}</strong>
            <span>{{ new Date(msg.createdAt).toLocaleString('ko-KR') }}</span>
          </div>
          <pre class="message-content">{{ msg.content }}</pre>

          <div v-if="msg.role === 'bot'" class="similarity-row">
            <span class="sim-chip">
              유사도: {{ formatSimilarity(msg.topSimilarity) }}
            </span>
            <span v-if="msg.isLowSimilarity" class="sim-chip warning">저유사도 안내 응답</span>
          </div>

          <div v-if="msg.role === 'bot' && parseRelatedKbIds(msg.relatedKbIds).length > 0" class="related-kb">
            <span class="label">참조 KB:</span>
            <button
              v-for="id in parseRelatedKbIds(msg.relatedKbIds)"
              :key="id"
              class="kb-chip"
              type="button"
              @click="openKbDetail(id, msg.topSimilarity)"
            >
              #{{ id }} 보기
            </button>
          </div>
        </article>
      </div>
    </div>

    <div v-if="showKbModal" class="modal-overlay" @click="closeKbModal">
      <div class="modal" @click.stop>
        <div class="modal-head">
          <h4>
            <span class="kb-head-icon">KB</span>
            <span>지식 베이스 상세</span>
          </h4>
          <button class="ghost" type="button" @click="closeKbModal">닫기</button>
        </div>

        <div v-if="loadingKbDetail" class="empty">불러오는 중...</div>
        <div v-else-if="selectedKbDetail?.error" class="empty">{{ selectedKbDetail.error }}</div>
        <div v-else-if="selectedKbDetail" class="kb-detail">
          <div class="kb-header-card">
            <div class="kb-header-top">
              <div class="kb-title">KB 상세 정보</div>
              <div class="kb-header-top-right">
                <span v-if="selectedKbSimilarity !== null" class="sim-chip">
                  연관 유사도 {{ formatSimilarity(selectedKbSimilarity) }}
                </span>
                <span class="badge" :class="selectedKbDetail.visibility === 'admin' ? 'admin' : 'user'">
                  {{ selectedKbDetail.visibility === 'admin' ? '관리자 전용' : '사용자 공개' }}
                </span>
              </div>
            </div>
            <div class="kb-header-badges">
              <span v-for="p in getKbPlatforms(selectedKbDetail)" :key="`header-platform-${p}`" class="badge platform">{{ p }}</span>
              <span v-if="!getKbPlatforms(selectedKbDetail).length" class="badge platform">공통</span>
            </div>
            <div class="kb-header-meta">
              <span>등록일: {{ formatDateTime(selectedKbDetail.createdAt) }}</span>
              <span>수정일: {{ formatDateTime(selectedKbDetail.updatedAt) }}</span>
            </div>
          </div>

          <div class="kb-section">
            <div class="kb-section-label">대표질문</div>
            <div class="kb-q">{{ selectedKbDetail.representativeQuestion }}</div>
          </div>

          <div class="kb-section">
            <div class="kb-section-label">답변</div>
            <pre class="kb-answer">{{ selectedKbDetail.solution }}</pre>
          </div>

          <div class="kb-meta-grid">
            <div class="kb-meta-item">
              <span class="kb-meta-label">공개수준</span>
              <span class="badge" :class="selectedKbDetail.visibility === 'admin' ? 'admin' : 'user'">
                {{ selectedKbDetail.visibility === 'admin' ? '관리자 전용' : '사용자 공개' }}
              </span>
            </div>
            <div class="kb-meta-item">
              <span class="kb-meta-label">플랫폼</span>
              <div class="kb-platforms">
                <span v-for="p in getKbPlatforms(selectedKbDetail)" :key="`detail-${p}`" class="badge platform">{{ p }}</span>
              </div>
            </div>
            <div class="kb-meta-item kb-meta-item-full" v-if="getKbTags(selectedKbDetail).length">
              <span class="kb-meta-label">태그</span>
              <div class="kb-tags">
                <span v-for="tag in getKbTags(selectedKbDetail)" :key="`tag-${tag}`" class="kb-tag-chip">#{{ tag }}</span>
              </div>
            </div>
          </div>

          <div v-if="selectedKbDetail.similarQuestions?.length" class="kb-section">
            <div class="kb-section-label">유사질문</div>
            <ul class="kb-similar-list">
              <li v-for="sq in selectedKbDetail.similarQuestions" :key="sq.id">{{ sq.question }}</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.chat-log-wrap {
  display: grid;
  grid-template-columns: 430px 1fr;
  grid-template-areas:
    "list detail"
    "summary summary";
  gap: 14px;
}

.list-panel {
  grid-area: list;
}

.detail-panel {
  grid-area: detail;
}

.summary-panel {
  grid-area: summary;
}

.panel {
  background: #fff;
  border: 1px solid #dee2e6;
  border-radius: 12px;
  padding: 14px;
  box-shadow: 0 0.45rem 1rem rgba(0, 0, 0, 0.06);
}

.panel-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.panel-head h3 {
  margin: 0;
  color: #212529;
}

.toolbar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) 120px auto;
  gap: 8px;
  align-items: center;
  width: 100%;
  margin-left: auto;
}

.toolbar > * {
  min-width: 0;
}

select {
  border: 1px solid #ced4da;
  border-radius: 8px;
  padding: 6px 8px;
  min-width: 0;
  width: 100%;
}

.ghost {
  border: 1px solid #ced4da;
  background: #fff;
  border-radius: 8px;
  padding: 6px 10px;
  cursor: pointer;
  white-space: nowrap;
  word-break: keep-all;
  box-sizing: border-box;
  transition: background-color 0.15s ease, border-color 0.15s ease;
  position: relative;
  z-index: 1;
}

.ghost:hover:not(:disabled) {
  background: #f8f9fa;
  border-color: #9aa3ad;
  box-shadow: inset 0 0 0 1px #9aa3ad;
}

.ghost:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.refresh-fit {
  width: 100%;
  min-width: 96px;
  margin-left: 0;
}

.pager {
  margin-top: 10px;
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 10px;
  color: #6c757d;
  font-size: 13px;
}

.empty {
  padding: 14px;
  text-align: center;
  color: #6c757d;
  background: #f8f9fa;
  border-radius: 8px;
}

.session-list,
.message-list {
  display: grid;
  gap: 8px;
}

.session-item {
  border: 1px solid #dee2e6;
  border-radius: 10px;
  padding: 10px;
  cursor: pointer;
  background: #fff;
}

.session-item:hover {
  border-color: #86b7fe;
}

.session-item.selected {
  border-color: #0d6efd;
  background: #eef5ff;
}

.session-title {
  font-weight: 700;
  color: #212529;
  margin-bottom: 6px;
}

.session-meta {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 12px;
  color: #6c757d;
}

.session-actions {
  margin-top: 8px;
  display: flex;
  justify-content: flex-end;
}

.danger-mini {
  border: 1px solid #dc3545;
  background: #fff;
  color: #9d2d22;
  border-radius: 999px;
  padding: 4px 10px;
  font-size: 12px;
  font-weight: 700;
  cursor: pointer;
}

.danger-mini:hover:not(:disabled) {
  background: #fdecef;
}

.danger-mini:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.summary-toolbar {
  grid-template-columns: 150px 110px 1fr;
}

.summary-toolbar .refresh-fit {
  width: auto;
  min-width: 88px;
  justify-self: end;
  margin-left: 0;
}

.summary-grid {
  display: grid;
  gap: 12px;
}

.summary-kpi-row {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.kpi-card {
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  background: #f8fafc;
  padding: 10px;
}

.kpi-label {
  font-size: 12px;
  color: #6c757d;
  margin-bottom: 4px;
}

.kpi-value {
  font-size: 20px;
  font-weight: 800;
  color: #1f2a44;
}

.kpi-value.small {
  font-size: 13px;
  font-weight: 700;
}

.summary-block {
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 12px;
  background: #fff;
}

.summary-block h4 {
  margin: 0 0 10px;
  color: #1f2a44;
  font-size: 14px;
}

.top-question-list {
  margin: 0;
  padding-left: 20px;
  display: grid;
  gap: 8px;
}

.top-question-text {
  color: #212529;
  line-height: 1.45;
}

.top-question-meta {
  margin-top: 2px;
  font-size: 12px;
  color: #6c757d;
}

.keyword-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.keyword-chip {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 4px 10px;
  border: 1px solid #cfe2ff;
  background: #e7f1ff;
  color: #0d6efd;
  font-size: 12px;
  font-weight: 700;
}

.daily-list {
  display: grid;
  gap: 6px;
}

.daily-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border: 1px dashed #d0d7de;
  border-radius: 8px;
  padding: 8px 10px;
  color: #495057;
  font-size: 13px;
}

.empty.small {
  padding: 10px;
  font-size: 13px;
}

.badge {
  border-radius: 999px;
  padding: 2px 8px;
  font-weight: 700;
}

.badge.user {
  background: #d1e7dd;
  color: #0f5132;
}

.badge.admin {
  background: #fff3cd;
  color: #664d03;
}

.badge.platform {
  background: #eef2ff;
  color: #3730a3;
}

.message-item {
  border: 1px solid #dee2e6;
  border-radius: 10px;
  padding: 10px;
}

.message-item.user {
  border-left: 4px solid #0d6efd;
}

.message-item.bot {
  border-left: 4px solid #198754;
  background: #fbfefc;
}

.message-head {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 6px;
  font-size: 12px;
  color: #6c757d;
}

.message-content {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  color: #495057;
  font-family: inherit;
  font-size: 14px;
}

.related-kb {
  margin-top: 8px;
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.similarity-row {
  margin-top: 8px;
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.sim-chip {
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 12px;
  font-weight: 700;
  background: #e7f1ff;
  color: #0d6efd;
  border: 1px solid #cfe2ff;
}

.sim-chip.warning {
  background: #fff3cd;
  color: #664d03;
  border-color: #ffe69c;
}

.label {
  font-size: 12px;
  color: #6c757d;
}

.kb-chip {
  background: #e7f1ff;
  color: #0d6efd;
  border: 1px solid #cfe2ff;
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 12px;
  font-weight: 700;
  cursor: pointer;
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1100;
}

.modal {
  width: min(760px, 94vw);
  max-height: 82vh;
  overflow: auto;
  background: #fff;
  border: 1px solid #dee2e6;
  border-radius: 14px;
  padding: 16px;
}

.modal-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
  padding: 12px;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  background: linear-gradient(135deg, #f8fafc 0%, #eef2ff 100%);
}

.modal-head h4 {
  margin: 0;
  color: #1f2a44;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 15px;
}

.kb-head-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  height: 22px;
  border-radius: 999px;
  background: #0d6efd;
  color: #fff;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.2px;
}

.kb-detail {
  display: grid;
  gap: 12px;
}

.kb-detail p {
  margin: 0 0 8px;
  color: #495057;
}

.kb-section {
  margin-bottom: 12px;
}

.kb-header-card {
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  background: #f8fafc;
  padding: 12px;
  display: grid;
  gap: 8px;
}

.kb-header-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.kb-header-top-right {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.kb-title {
  font-size: 14px;
  font-weight: 800;
  color: #1f2a44;
}

.kb-header-badges {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.kb-header-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 12px;
  color: #5f6b7a;
}

.kb-section-label {
  font-size: 12px;
  font-weight: 700;
  color: #6c757d;
  margin-bottom: 6px;
}

.kb-q {
  border: 1px solid #e2e8f0;
  background: #f8fafc;
  color: #1f2a44;
  border-radius: 8px;
  padding: 11px;
  font-weight: 600;
}

.kb-answer {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  color: #495057;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 11px;
}

.kb-meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-bottom: 12px;
}

.kb-meta-item {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 10px;
  display: grid;
  gap: 6px;
  color: #495057;
}

.kb-meta-item-full {
  grid-column: 1 / -1;
}

.kb-meta-label {
  font-size: 12px;
  font-weight: 700;
  color: #6c757d;
}

.kb-platforms {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.kb-tags {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.kb-tag-chip {
  display: inline-flex;
  align-items: center;
  border: 1px solid #cfe2ff;
  border-radius: 999px;
  padding: 3px 9px;
  background: #e7f1ff;
  color: #0d6efd;
  font-size: 12px;
  font-weight: 700;
}

.kb-similar-list {
  margin: 0;
  padding-left: 18px;
  color: #495057;
}

@media (max-width: 1024px) {
  .chat-log-wrap {
    grid-template-columns: 1fr;
    grid-template-areas:
      "list"
      "detail"
      "summary";
  }
  
  .toolbar {
    grid-template-columns: 1fr;
    width: 100%;
  }

  .kb-meta-grid {
    grid-template-columns: 1fr;
  }

  .summary-kpi-row {
    grid-template-columns: 1fr;
  }
}
</style>

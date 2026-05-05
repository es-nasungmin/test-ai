<script setup>
import { computed, onMounted, ref, watch, onBeforeUnmount } from 'vue'
import axios from 'axios'
import { API_BASE_URL } from '../../config'

const API_URL = API_BASE_URL

const loading = ref(false)
const sessions = ref([])
const sessionPage = ref(1)
const sessionPageSize = ref(10)
const sessionTotal = ref(0)
const sessionPageSizeOptions = [10, 20, 50, 100]
const roleFilter = ref('all')
const platformFilter = ref('all')
const keywordFilter = ref('')
const platformOptions = ref(['공통'])
const selectedSessionId = ref(null)
const selectedSession = ref(null)
const mobileTab = ref('list') // 'list' | 'detail'
const loadingDetail = ref(false)
const showKbModal = ref(false)
const loadingKbDetail = ref(false)
const selectedKbDetail = ref(null)
const selectedKbId = ref(null)
const selectedKbSimilarity = ref(null)
const selectedKbEvidence = ref(null)
const showSimilarityExplain = ref(false)
const deletingSessionId = ref(null)
const kbDetailRequestSeq = ref(0)

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
    const keyword = keywordFilter.value.trim()
    if (keyword) params.keyword = keyword
    const res = await axios.get(`${API_URL}/chat/sessions`, { params })
    sessions.value = res.data?.data || []
    sessionTotal.value = Number(res.data?.total || 0)
  } finally {
    loading.value = false
  }
}

function applyKeywordFilter() {
  sessionPage.value = 1
  fetchSessions()
}

function clearKeywordFilter() {
  if (!keywordFilter.value) return
  keywordFilter.value = ''
  sessionPage.value = 1
  fetchSessions()
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

async function loadSessionDetail(id) {
  selectedSessionId.value = id
  mobileTab.value = 'detail'
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
    alert('채팅 세션이 삭제되었습니다.')
  } catch {
    alert('세션 삭제에 실패했습니다.')
  } finally {
    deletingSessionId.value = null
  }
}

function parseRelatedKbs(message) {
  const diagnostics = parseRetrievalDiagnostics(message)
  const candidateById = new Map(
    (diagnostics?.candidates || []).map((c) => [c.id, c])
  )

  const metaRaw = message?.relatedKbMeta || message?.RelatedKbMeta
  if (metaRaw) {
    try {
      const parsedMeta = JSON.parse(metaRaw)
      if (Array.isArray(parsedMeta)) {
        return parsedMeta
          .map((item) => ({
            id: Number(item?.id),
            similarity: Number.isFinite(Number(item?.similarity)) ? Number(item.similarity) : null,
            includedBySemantic: item?.includedBySemantic === true,
            includedByKeyword: item?.includedByKeyword === true,
            matchedKeywords: Array.isArray(item?.matchedKeywords)
              ? item.matchedKeywords.map((x) => String(x).trim()).filter(Boolean)
              : [],
            keywordMatchCount: Number(item?.keywordMatchCount || 0),
            baseSimilarity: Number.isFinite(Number(item?.baseSimilarity)) ? Number(item.baseSimilarity) : null,
            keywordBoost: Number.isFinite(Number(item?.keywordBoost)) ? Number(item.keywordBoost) : null,
            evidence: candidateById.get(Number(item?.id)) || null
          }))
          .filter((item) => Number.isInteger(item.id) && item.id > 0)
      }
    } catch {
      // Fallback to legacy relatedKbIds parsing below.
    }
  }

  const idsRaw = message?.relatedKbIds || message?.RelatedKbIds
  if (!idsRaw) return []
  try {
    const parsedIds = JSON.parse(idsRaw)
    if (!Array.isArray(parsedIds)) return []
    return parsedIds
      .map((id) => Number(id))
      .filter((id) => Number.isInteger(id) && id > 0)
      .map((id) => ({
        id,
        similarity: null,
        includedBySemantic: candidateById.get(id)?.includedBySemantic === true,
        includedByKeyword: candidateById.get(id)?.includedByKeyword === true,
        matchedKeywords: candidateById.get(id)?.matchedKeywords || [],
        keywordMatchCount: candidateById.get(id)?.keywordMatchCount || 0,
        baseSimilarity: Number.isFinite(Number(candidateById.get(id)?.baseSimilarity)) ? Number(candidateById.get(id)?.baseSimilarity) : null,
        keywordBoost: Number.isFinite(Number(candidateById.get(id)?.keywordBoost)) ? Number(candidateById.get(id)?.keywordBoost) : null,
        evidence: candidateById.get(id) || null
      }))
  } catch {
    return []
  }
}

function parseRetrievalDiagnostics(message) {
  const raw = message?.retrievalDebugMeta || message?.RetrievalDebugMeta
  if (!raw) return null
  try {
    const parsed = JSON.parse(raw)
    if (!parsed || typeof parsed !== 'object') return null

    const tokens = Array.isArray(parsed.questionTokens)
      ? parsed.questionTokens
        .map((x) => String(x).trim())
        .filter(Boolean)
      : []

    const candidates = Array.isArray(parsed.candidates)
      ? parsed.candidates
        .map((c) => ({
          id: Number(c?.id),
          title: typeof c?.title === 'string' ? c.title : '',
          matchedQuestion: typeof c?.matchedQuestion === 'string' ? c.matchedQuestion : '',
          baseSimilarity: Number(c?.baseSimilarity),
          keywordBoost: Number(c?.keywordBoost),
          adjustedSimilarity: Number(c?.adjustedSimilarity),
          keywordMatchCount: Number(c?.keywordMatchCount),
          matchedKeywords: Array.isArray(c?.matchedKeywords)
            ? c.matchedKeywords.map((x) => String(x).trim()).filter(Boolean)
            : [],
          includedBySemantic: Boolean(c?.includedBySemantic),
          includedByKeyword: Boolean(c?.includedByKeyword),
          passedThreshold: Boolean(c?.passedThreshold),
          selectedForAnswer: Boolean(c?.selectedForAnswer)
        }))
        .filter((c) => Number.isInteger(c.id) && c.id > 0)
      : []

    return {
      similarityThreshold: Number(parsed.similarityThreshold),
      questionTokens: tokens,
      candidates
    }
  } catch {
    return null
  }
}

function getRelatedKbsSorted(message) {
  return parseRelatedKbs(message)
    .slice()
    .sort((a, b) => {
      const aSimilarity = typeof a?.similarity === 'number' ? a.similarity : -1
      const bSimilarity = typeof b?.similarity === 'number' ? b.similarity : -1
      if (bSimilarity !== aSimilarity) return bSimilarity - aSimilarity
      return (a?.id || 0) - (b?.id || 0)
    })
}

function sourceLabelClass(item) {
  const bySemantic = item?.includedBySemantic === true
  const byKeyword = item?.includedByKeyword === true
  if (bySemantic && byKeyword) return 'both'
  if (bySemantic) return 'semantic'
  if (byKeyword) return 'keyword'
  return ''
}

function sourceLabelText(item) {
  const bySemantic = item?.includedBySemantic === true
  const byKeyword = item?.includedByKeyword === true
  if (bySemantic && byKeyword) return '벡터+키워드'
  if (bySemantic) return '벡터'
  if (byKeyword) return '키워드'
  return '경로없음'
}

function formatPercentPart(value) {
  if (typeof value !== 'number' || Number.isNaN(value)) return '-'
  return `${(value * 100).toFixed(1)}%`
}

function toFiniteNumberOrNull(value) {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function buildSimilarityEvidence(rawEvidence, fallbackSimilarity) {
  const evidence = rawEvidence && typeof rawEvidence === 'object' ? rawEvidence : {}
  const finalSimilarity = toFiniteNumberOrNull(evidence.adjustedSimilarity)
    ?? toFiniteNumberOrNull(evidence.similarity)
    ?? toFiniteNumberOrNull(fallbackSimilarity)

  const matchedKeywords = Array.isArray(evidence.matchedKeywords)
    ? evidence.matchedKeywords.map((x) => String(x).trim()).filter(Boolean)
    : []

  const keywordMatchCountRaw = Number(evidence.keywordMatchCount)
  const keywordMatchCount = Number.isFinite(keywordMatchCountRaw) && keywordMatchCountRaw > 0
    ? Math.floor(keywordMatchCountRaw)
    : matchedKeywords.length

  // 실제 로직: FinalScore = SemanticScore + KeywordBoost
  const semanticSimilarity = toFiniteNumberOrNull(evidence.baseSimilarity)
    ?? finalSimilarity

  const keywordBoost = toFiniteNumberOrNull(evidence.keywordBoost) ?? 0

  const adjustedSimilarity = finalSimilarity ?? semanticSimilarity

  return {
    ...evidence,
    matchedKeywords,
    keywordMatchCount,
    semanticSimilarity,
    keywordBoost,
    adjustedSimilarity,
    includedBySemantic: evidence.includedBySemantic === true,
    includedByKeyword: evidence.includedByKeyword === true
  }
}

function similarityExplainText(evidence, fallbackSimilarity) {
  const normalized = buildSimilarityEvidence(evidence, fallbackSimilarity)

  const semantic = formatPercentPart(normalized.semanticSimilarity)
  const adjusted = formatPercentPart(normalized.adjustedSimilarity)

  // 키워드 가산 여부
  const keywordBoost = typeof normalized.keywordBoost === 'number' ? normalized.keywordBoost : 0
  const hasBoost = keywordBoost > 0
  const boostText = hasBoost
    ? `키워드 점수 가산: +${(keywordBoost * 100).toFixed(1)}%`
    : '키워드 점수 가산: 없음'

  const keywordInfo = normalized.matchedKeywords.length
    ? `매칭 키워드: ${normalized.matchedKeywords.join(', ')}`
    : '매칭 키워드 없음'

  const inclusionReasons = []
  if (normalized.includedBySemantic) inclusionReasons.push('벡터 검색')
  if (normalized.includedByKeyword) inclusionReasons.push('키워드 매칭')
  const inclusionText = inclusionReasons.length > 0
    ? `후보 포함 경로: ${inclusionReasons.join(' + ')}`
    : '후보 포함 경로: 정보 없음'

  // 키워드 매칭됐지만 점수 가산이 없는 경우: 유사도가 너무 낮아 가산 차단됨
  const keywordNote = normalized.includedByKeyword && !hasBoost
    ? '※ 유사도가 낮아 키워드 점수 가산이 차단됨'
    : ''

  return [
    `최종 유사도: ${adjusted}`,
    `벡터 유사도: ${semantic}`,
    boostText,
    keywordInfo,
    inclusionText,
    keywordNote
  ].filter(Boolean).join('\n')
}

async function openKbDetail(id, similarity = null, evidence = null) {
  const kbId = Number(id)
  const requestSeq = ++kbDetailRequestSeq.value
  const normalizedSimilarity = typeof similarity === 'number' ? similarity : null
  loadingKbDetail.value = true
  showKbModal.value = true
  selectedKbDetail.value = null
  selectedKbId.value = kbId
  selectedKbSimilarity.value = normalizedSimilarity
  selectedKbEvidence.value = buildSimilarityEvidence(evidence, normalizedSimilarity)
  showSimilarityExplain.value = false

  if (!Number.isInteger(kbId) || kbId <= 0) {
    selectedKbDetail.value = { error: '잘못된 KB ID입니다.' }
    loadingKbDetail.value = false
    return
  }

  try {
    const res = await axios.get(`${API_URL}/knowledgebase/${kbId}`, { timeout: 10000 })

    if (kbDetailRequestSeq.value !== requestSeq) return

    selectedKbDetail.value = res.data
  } catch (err) {
    if (kbDetailRequestSeq.value !== requestSeq) return
    const status = err?.response?.status
    if (status === 401) {
      selectedKbDetail.value = { error: '로그인이 만료되었습니다. 다시 로그인 후 시도해주세요.' }
    } else {
      selectedKbDetail.value = { error: `KB 상세를 불러오지 못했습니다.${status ? ` (${status})` : ''}` }
    }
  } finally {
    if (kbDetailRequestSeq.value === requestSeq) {
      loadingKbDetail.value = false
    }
  }
}

function closeKbModal() {
  kbDetailRequestSeq.value += 1
  showKbModal.value = false
  loadingKbDetail.value = false
  selectedKbDetail.value = null
  selectedKbId.value = null
  selectedKbSimilarity.value = null
  selectedKbEvidence.value = null
  showSimilarityExplain.value = false
}

async function fetchQuestionSummary() {
  // 질문 분석은 ManagementPage에서 관리됩니다.
  // 이 함수는 호환성을 위해 유지됩니다.
}

function formatSimilarity(value) {
  if (typeof value !== 'number' || Number.isNaN(value)) return '-'
  return `${(value * 100).toFixed(1)}%`
}

function formatDateTime(value) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '-'
  return date.toLocaleString('ko-KR')
}

function getKbPlatforms(detail) {
  if (!detail) return ['공통']
  const platforms = detail.platforms || detail.Platforms
  if (Array.isArray(platforms) && platforms.length > 0) {
    return platforms
  }
  const platform = detail.platform || detail.Platform
  if (typeof platform === 'string' && platform.trim()) {
    return platform.split(',').map((x) => x.trim()).filter(Boolean)
  }
  return ['공통']
}

function getKbTags(detail) {
  const keywords = detail?.keywords || detail?.Keywords || detail?.tags || detail?.Tags
  if (typeof keywords !== 'string') return []
  return keywords.split(',').map((x) => x.trim()).filter(Boolean)
}

watch([roleFilter, platformFilter, sessionPageSize], () => {
  sessionPage.value = 1
  fetchSessions()
})

watch([roleFilter, platformFilter], () => {
  fetchSessions()
})

watch([showKbModal, loadingKbDetail], ([isOpen, isLoading]) => {
  if (!isOpen || !isLoading) return
  const guardSeq = kbDetailRequestSeq.value
  setTimeout(() => {
    if (!showKbModal.value || !loadingKbDetail.value) return
    if (kbDetailRequestSeq.value !== guardSeq) return
    loadingKbDetail.value = false
    if (!selectedKbDetail.value) {
      selectedKbDetail.value = { error: 'KB 상세 로딩이 지연되어 중단되었습니다. 다시 시도해주세요.' }
    }
  }, 12000)
})

function handleOutsideClick(e) {
  if (showSimilarityExplain.value && !e.target.closest('.sim-chip-wrap')) {
    showSimilarityExplain.value = false
  }
}

onMounted(async () => {
  await fetchPlatforms()
  await fetchSessions()
  document.addEventListener('click', handleOutsideClick)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', handleOutsideClick)
})
</script>

<template>
  <section class="chat-log-wrap">
    <div class="panel list-panel" :class="{ 'mobile-hidden': mobileTab !== 'list' }">
      <div class="panel-head">
        <h3>채팅 세션</h3>
        <button class="ghost refresh-top" :disabled="loading" @click="fetchSessions">새로고침</button>
      </div>
      <div class="panel-toolbar-wrap">
        <div class="toolbar">
          <div class="toolbar-row toolbar-row-top">
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
          </div>
          <div class="toolbar-row toolbar-row-bottom">
            <input
              v-model="keywordFilter"
              type="text"
              placeholder="채팅자/질문/답변 검색"
              @keyup.enter="applyKeywordFilter"
            >
            <button class="ghost" :disabled="loading" @click="applyKeywordFilter">검색</button>
            <button class="ghost" :disabled="loading || !keywordFilter" @click="clearKeywordFilter">초기화</button>
          </div>
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
            <span>채팅자 {{ session.actorName || '알 수 없음' }}</span>
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

    <div class="panel detail-panel" :class="{ 'mobile-hidden': mobileTab !== 'detail' }">
      <div class="panel-head">
        <h3>대화 상세</h3>
        <div class="detail-head-actions">
          <div class="desktop-slot-actions">
            <slot name="detail-actions" />
          </div>
          <button class="ghost mobile-back-btn" type="button" @click="mobileTab = 'list'">목록으로</button>
        </div>
      </div>

      <div v-if="!selectedSessionId" class="empty">위 목록에서 세션을 선택하세요.</div>
      <div v-else-if="loadingDetail" class="empty">세부 내역을 불러오는 중...</div>
      <div v-else-if="!selectedSession" class="empty">세부 내역이 없습니다.</div>

      <div v-else class="message-list">
        <div class="detail-meta-row">
          <span>채팅자: {{ selectedSession.actorName || '알 수 없음' }}</span>
          <span>역할: {{ selectedSession.userRole === 'admin' ? '관리자' : '사용자' }}</span>
          <span>플랫폼: {{ selectedSession.platform || '공통' }}</span>
        </div>
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

          <div v-if="msg.role === 'bot' && getRelatedKbsSorted(msg).length > 0" class="related-kb">
            <span class="label">참조 KB:</span>
            <div class="related-kb-list">
              <div
                v-for="(item, idx) in getRelatedKbsSorted(msg)"
                :key="`rel-${msg.id}-${item.id}`"
                class="related-kb-item"
              >
                <button
                  class="kb-chip"
                  type="button"
                  @click="openKbDetail(item.id, item.similarity, item)"
                >
                  {{ idx + 1 }}위 · #{{ item.id }} · {{ formatSimilarity(item.similarity) }}
                </button>
                <span class="source-label" :class="sourceLabelClass(item)">
                  {{ sourceLabelText(item) }}
                </span>
                <span v-if="item.matchedKeywords?.length" class="source-keywords">
                  키워드: {{ item.matchedKeywords.join(', ') }}
                </span>
              </div>
            </div>
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
            <span v-if="selectedKbId" class="kb-modal-id-badge">#{{ selectedKbId }}</span>
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
                <div v-if="selectedKbSimilarity !== null" class="sim-chip-wrap">
                  <span class="sim-chip">
                    <button class="sim-chip-btn" type="button" @click="showSimilarityExplain = !showSimilarityExplain">
                      연관 유사도 {{ formatSimilarity(selectedKbSimilarity) }}
                    </button>
                  </span>
                  <div v-if="showSimilarityExplain" class="sim-float-popup">
                    <p class="sim-mini-explain">{{ similarityExplainText(selectedKbEvidence, selectedKbSimilarity) }}</p>
                  </div>
                </div>
                <span class="badge visibility-chip" :class="(selectedKbDetail.visibility || selectedKbDetail.Visibility) === 'admin' ? 'admin' : 'user'">
                  {{ (selectedKbDetail.visibility || selectedKbDetail.Visibility) === 'admin' ? '관리자 전용' : '사용자 공개' }}
                </span>
              </div>
            </div>
            <div class="kb-header-badges">
              <span v-for="p in getKbPlatforms(selectedKbDetail)" :key="`header-platform-${p}`" class="badge platform">{{ p }}</span>
              <span v-if="!getKbPlatforms(selectedKbDetail).length" class="badge platform">공통</span>
            </div>
            <div class="kb-header-meta">
              <span>등록일: {{ formatDateTime(selectedKbDetail.createdAt || selectedKbDetail.CreatedAt) }}</span>
              <span>수정일: {{ formatDateTime(selectedKbDetail.updatedAt || selectedKbDetail.UpdatedAt) }}</span>
            </div>
          </div>

          <div class="kb-section">
            <div class="kb-section-label">제목</div>
            <div class="kb-q">{{ selectedKbDetail.title || selectedKbDetail.Title || '-' }}</div>
          </div>

          <div class="kb-section">
            <div class="kb-section-label">내용</div>
            <div class="kb-q">{{ selectedKbDetail.content || selectedKbDetail.Content || selectedKbDetail.solution || selectedKbDetail.Solution || '-' }}</div>
          </div>

          <div class="kb-meta-grid">
            <!-- <div class="kb-meta-item">
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
            </div> -->
            <div class="kb-meta-item kb-meta-item-full" v-if="getKbTags(selectedKbDetail).length">
              <span class="kb-meta-label">키워드</span>
              <div class="kb-tags">
                <span v-for="tag in getKbTags(selectedKbDetail)" :key="`tag-${tag}`" class="kb-tag-chip">#{{ tag }}</span>
              </div>
            </div>
          </div>

          <div v-if="(selectedKbDetail.expectedQuestions || selectedKbDetail.ExpectedQuestions || selectedKbDetail.similarQuestions || selectedKbDetail.SimilarQuestions)?.length" class="kb-section">
            <div class="kb-section-label">예상질문</div>
            <ul class="kb-similar-list">
              <li v-for="sq in (selectedKbDetail.expectedQuestions || selectedKbDetail.ExpectedQuestions || selectedKbDetail.similarQuestions || selectedKbDetail.SimilarQuestions)" :key="sq.id || sq.Id">{{ sq.question || sq.Question }}</li>
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
  flex-wrap: nowrap;
  margin-bottom: 10px;
}

.panel-head h3 {
  margin: 0;
  color: #212529;
}

.detail-head-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-left: auto;
  justify-content: flex-end;
}

.detail-head-actions:empty {
  display: none;
}

.panel-toolbar-wrap {
  margin-bottom: 10px;
}

.toolbar {
  display: grid;
  gap: 8px;
  width: 100%;
  margin-left: auto;
}

.toolbar-row {
  display: grid;
  gap: 8px;
  align-items: center;
}

.toolbar-row-top {
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) 120px;
}

.toolbar-row-bottom {
  grid-template-columns: minmax(0, 1fr) auto auto auto;
}

.toolbar-row > * {
  min-width: 0;
}

select {
  border: 1px solid #ced4da;
  border-radius: 8px;
  padding: 6px 8px;
  min-width: 0;
  width: 100%;
}

.toolbar input {
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

.refresh-top {
  min-width: 88px;
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

.detail-meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 12px;
  color: #5b6b7c;
  border: 1px solid #dbe4f2;
  background: #f8fbff;
  border-radius: 8px;
  padding: 8px 10px;
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

.source-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.related-kb-list {
  display: flex;
  flex-direction: row;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.related-kb-item {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.source-label {
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 12px;
  font-weight: 700;
  border: 1px solid #d0d7de;
  color: #334155;
  background: #f8fafc;
}

.source-label.semantic {
  border-color: #cfe2ff;
  color: #0d6efd;
  background: #e7f1ff;
}

.source-label.keyword {
  border-color: #ffe69c;
  color: #8a5700;
  background: #fff3cd;
}

.source-label.both {
  border-color: #c7f9cc;
  color: #166534;
  background: #dcfce7;
}

.source-keywords {
  font-size: 12px;
  color: #64748b;
}

.sim-chip-btn {
  border: none;
  background: transparent;
  color: inherit;
  font: inherit;
  font-weight: 700;
  padding: 0;
  cursor: pointer;
}

.sim-chip-wrap {
  position: relative;
  display: inline-flex;
  align-items: center;
}

.sim-float-popup {
  position: absolute;
  top: calc(100% + 6px);
  right: 0;
  z-index: 200;
  min-width: 220px;
  max-width: 340px;
  filter: drop-shadow(0 4px 12px rgba(0,0,0,.12));
}

.sim-mini-explain {
  margin: 0;
  padding: 8px 10px;
  border-radius: 8px;
  border: 1px solid #dbe4f0;
  background: #f8fbff;
  font-size: 11px;
  line-height: 1.45;
  color: #475569;
  white-space: pre-line;
}

.visibility-chip {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 12px;
  font-weight: 700;
}

.sim-explain {
  margin: 0;
  padding: 10px;
  border: 1px solid #dbe4f0;
  border-radius: 8px;
  background: #f8fbff;
  font-size: 13px;
  color: #334155;
}

.retrieval-debug {
  margin-top: 8px;
  border: 1px solid #dbe4f0;
  border-radius: 8px;
  background: #f8fbff;
}

.retrieval-debug > summary {
  cursor: pointer;
  padding: 8px 10px;
  font-size: 12px;
  font-weight: 700;
  color: #1f4d8f;
}

.retrieval-debug-body {
  border-top: 1px solid #dbe4f0;
  padding: 10px;
  display: grid;
  gap: 8px;
}

.debug-token-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}

.debug-candidates {
  display: grid;
  gap: 8px;
}

.debug-candidate {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 8px;
  background: #fff;
}

.debug-candidate-top {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  align-items: center;
}

.debug-badges {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.debug-line {
  margin: 4px 0;
  font-size: 12px;
  color: #495057;
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

.kb-modal-id-badge {
  display: inline-flex;
  align-items: center;
  padding: 1px 9px;
  border-radius: 999px;
  background: #e7f0ff;
  color: #0d6efd;
  font-size: 12px;
  font-weight: 700;
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
  margin-bottom: 0;
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
  margin-bottom: 0;
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

.mobile-back-btn {
  display: none;
}

.desktop-slot-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

@media (max-width: 1024px) {
  .chat-log-wrap {
    grid-template-columns: 1fr;
    grid-template-areas:
      "list"
      "detail"
      "summary";
  }

  .detail-head-actions {
    justify-content: flex-end;
  }
  
  .toolbar {
    grid-template-columns: 1fr;
    width: 100%;
  }

  .toolbar-row-top,
  .toolbar-row-bottom {
    grid-template-columns: 1fr;
  }

  .kb-meta-grid {
    grid-template-columns: 1fr;
  }

  .summary-kpi-row {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 768px) {
  .mobile-hidden {
    display: none !important;
  }

  .mobile-back-btn {
    display: inline-flex;
  }

  .desktop-slot-actions {
    display: none;
  }

  .chat-log-wrap {
    grid-template-areas:
      "list"
      "detail"
      "summary";
  }
}
</style>

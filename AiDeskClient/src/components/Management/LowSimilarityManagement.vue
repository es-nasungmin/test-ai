<script setup>
import { computed, onMounted, ref } from 'vue'
import axios from 'axios'
import { API_BASE_URL } from '../../config'

const API_URL = API_BASE_URL

const lowSimilarityQuestions = ref([])
const loadingLowSimilarity = ref(false)
const lowSimilarityPage = ref(1)
const lowSimilarityPageSize = 20
const lowSimilarityTotal = ref(0)
const lowSimilarityPlatformFilter = ref('all')
const lowSimilarityStatusFilter = ref('pending')  // 'pending' | 'resolved' | 'all'
const platformOptions = ref(['공통'])
const showSessionModal = ref(false)
const loadingSession = ref(false)
const selectedSession = ref(null)

const lowSimilarityTotalPages = computed(() =>
  Math.max(1, Math.ceil(lowSimilarityTotal.value / lowSimilarityPageSize))
)

async function fetchPlatforms() {
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/platforms`)
    const list = Array.isArray(res.data) ? res.data : []
    const normalized = list
      .map((x) => (typeof x === 'string' ? x.trim() : ''))
      .filter(Boolean)
    platformOptions.value = Array.from(new Set(['공통', ...normalized]))
    if (
      lowSimilarityPlatformFilter.value !== 'all' &&
      !platformOptions.value.includes(lowSimilarityPlatformFilter.value)
    ) {
      lowSimilarityPlatformFilter.value = 'all'
    }
  } catch {
    platformOptions.value = ['공통']
  }
}

async function fetchLowSimilarityQuestions() {
  loadingLowSimilarity.value = true
  try {
    const params = {
      page: lowSimilarityPage.value,
      pageSize: lowSimilarityPageSize,
      platform:
        lowSimilarityPlatformFilter.value === 'all'
          ? undefined
          : lowSimilarityPlatformFilter.value,
      includeResolved: lowSimilarityStatusFilter.value !== 'pending'
    }
    const res = await axios.get(`${API_URL}/knowledgebase/low-similarity-questions`, { params })
    let data = res.data?.data || []
    if (lowSimilarityStatusFilter.value === 'resolved') {
      data = data.filter(x => x.isResolved)
    } else if (lowSimilarityStatusFilter.value === 'pending') {
      data = data.filter(x => !x.isResolved)
    }
    lowSimilarityQuestions.value = data
    lowSimilarityTotal.value = Number(res.data?.total || 0)
  } catch {
    // silent
  } finally {
    loadingLowSimilarity.value = false
  }
}

async function resolveLowSimilarityQuestion(item) {
  if (!confirm('해당 문의를 처리완료로 변경할까요?')) return
  try {
    await axios.put(`${API_URL}/knowledgebase/low-similarity-questions/${item.id}/resolve`)
    if (lowSimilarityQuestions.value.length === 1 && lowSimilarityPage.value > 1) {
      lowSimilarityPage.value -= 1
    }
    await fetchLowSimilarityQuestions()
  } catch {
    alert('처리 상태 변경에 실패했습니다.')
  }
}

function onStatusFilterChange() {
  lowSimilarityPage.value = 1
  fetchLowSimilarityQuestions()
}

function onPlatformFilterChange() {
  lowSimilarityPage.value = 1
  fetchLowSimilarityQuestions()
}

function goLowSimilarityPage(page) {
  const next = Math.min(Math.max(1, page), lowSimilarityTotalPages.value)
  if (next === lowSimilarityPage.value) return
  lowSimilarityPage.value = next
  fetchLowSimilarityQuestions()
}

function formatDateTime(val) {
  if (!val) return '-'
  const d = new Date(val)
  return isNaN(d.getTime()) ? '-' : d.toLocaleString('ko-KR', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit'
  })
}

async function openLinkedSession(item) {
  const sessionId = item?.matchedSessionId || item?.sessionId
  if (!sessionId) {
    alert('연결된 채팅 세션을 찾지 못했습니다.')
    return
  }

  showSessionModal.value = true
  loadingSession.value = true
  selectedSession.value = null

  try {
    const res = await axios.get(`${API_URL}/chat/sessions/${sessionId}`)
    selectedSession.value = res.data
  } catch {
    alert('채팅 세션을 불러오지 못했습니다.')
    showSessionModal.value = false
  } finally {
    loadingSession.value = false
  }
}

onMounted(async () => {
  await fetchPlatforms()
  await fetchLowSimilarityQuestions()
})
</script>

<template>
  <div class="panel">
    <div class="panel-head">
      <h3>저유사도 문의 관리</h3>
      <div class="panel-tools">
        <select v-model="lowSimilarityPlatformFilter" @change="onPlatformFilterChange">
          <option value="all">플랫폼 전체</option>
          <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
        </select>
        <select v-model="lowSimilarityStatusFilter" @change="onStatusFilterChange">
          <option value="pending">미처리</option>
          <option value="resolved">처리완료</option>
          <option value="all">전체</option>
        </select>
        <button
          class="ghost refresh-fit"
          :disabled="loadingLowSimilarity"
          @click="fetchLowSimilarityQuestions"
        >
          새로고침
        </button>
      </div>
    </div>

    <div v-if="loadingLowSimilarity" class="empty">불러오는 중...</div>
    <div v-else-if="lowSimilarityQuestions.length === 0" class="empty">
      {{ lowSimilarityStatusFilter === 'resolved' ? '처리완료된 문의가 없습니다.' : '미처리 문의가 없습니다.' }}
    </div>

    <div v-else class="kb-list">
      <article v-for="item in lowSimilarityQuestions" :key="item.id" class="kb-item">
        <div class="kb-top">
          <button class="question-link" type="button" @click="openLinkedSession(item)">Q. {{ item.question }}</button>
          <div class="badges">
            <span class="scope resolved-badge" v-if="item.isResolved">처리완료</span>
            <span class="scope" :class="item.role === 'admin' ? 'admin' : 'user'">
              {{ item.role === 'admin' ? '관리자 챗봇' : '사용자 챗봇' }}
            </span>
            <span class="scope platform">{{ item.platform || '공통' }}</span>
          </div>
        </div>

        <p class="meta">
          <span>채팅자: {{ item.actorName || '알 수 없음' }}</span>
          <span>최대 유사도: {{ Math.round((item.topSimilarity || 0) * 100) }}%</span>
          <span v-if="item.topMatchedQuestion">매칭 후보: {{ item.topMatchedQuestion }}</span>
          <span v-if="item.isResolved && item.resolvedAt">처리일시: {{ formatDateTime(item.resolvedAt) }}</span>
        </p>

        <div class="item-actions">
          <button v-if="!item.isResolved" class="secondary compact-action-btn" @click="resolveLowSimilarityQuestion(item)">처리완료</button>
        </div>
      </article>
    </div>

    <div v-if="!loadingLowSimilarity && lowSimilarityTotal > 0" class="pager">
      <button
        class="ghost"
        :disabled="lowSimilarityPage <= 1"
        @click="goLowSimilarityPage(lowSimilarityPage - 1)"
      >
        이전
      </button>
      <span>{{ lowSimilarityPage }} / {{ lowSimilarityTotalPages }} (총 {{ lowSimilarityTotal }}건)</span>
      <button
        class="ghost"
        :disabled="lowSimilarityPage >= lowSimilarityTotalPages"
        @click="goLowSimilarityPage(lowSimilarityPage + 1)"
      >
        다음
      </button>
    </div>

    <div v-if="showSessionModal" class="modal-overlay" @click="showSessionModal = false">
      <div class="modal" @click.stop>
        <div class="modal-head">
          <h4>연결된 채팅 세션</h4>
          <button class="ghost compact-action-btn" type="button" @click="showSessionModal = false">닫기</button>
        </div>

        <div v-if="loadingSession" class="empty">세션 불러오는 중...</div>

        <div v-else-if="selectedSession" class="session-body">
          <div class="session-meta">
            <span>세션ID: {{ selectedSession.id }}</span>
            <span>채팅자: {{ selectedSession.actorName || '알 수 없음' }}</span>
            <span>역할: {{ selectedSession.userRole === 'admin' ? '관리자' : '사용자' }}</span>
            <span>플랫폼: {{ selectedSession.platform || '공통' }}</span>
            <span>메시지 수: {{ selectedSession.messageCount || 0 }}</span>
          </div>

          <div class="session-messages">
            <div
              v-for="msg in (selectedSession.messages || [])"
              :key="msg.id"
              class="msg-item"
              :class="msg.role === 'user' ? 'user' : 'bot'"
            >
              <div class="msg-role">{{ msg.role === 'user' ? '사용자' : '챗봇' }}</div>
              <div class="msg-content">{{ msg.content }}</div>
              <div class="msg-time">{{ formatDateTime(msg.createdAt) }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.panel {
  border: 1px solid #dee2e6;
  border-radius: 12px;
  background: #ffffff;
  box-shadow: 0 0.45rem 1rem rgba(0, 0, 0, 0.06);
  padding: 18px;
}

.panel-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.panel-head h3 {
  margin: 0;
  color: #212529;
  font-size: 1.05rem;
}

.panel-tools {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  width: 100%;
}

.panel-tools select {
  width: 160px;
  min-width: 160px;
  flex: 0 0 160px;
}

select {
  width: 100%;
  border: 1px solid #ced4da;
  border-radius: 10px;
  padding: 10px;
  font-size: 14px;
  color: #212529;
  background: #fff;
}

select:focus {
  outline: none;
  border-color: #86b7fe;
  box-shadow: 0 0 0 0.25rem rgba(13, 110, 253, 0.25);
}

.kb-list {
  display: grid;
  gap: 12px;
}

.kb-item {
  border: 1px solid #dfe6ee;
  border-radius: 12px;
  padding: 15px;
  background: #ffffff;
  box-shadow: 0 4px 12px rgba(15, 23, 42, 0.05);
  transition: border-color 0.16s ease, box-shadow 0.16s ease;
}

.kb-item:hover {
  border-color: #c8d5e6;
  box-shadow: 0 8px 18px rgba(15, 23, 42, 0.08);
}

.kb-top {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: flex-start;
  margin-bottom: 10px;
}

.question-link {
  border: none;
  background: transparent;
  padding: 0;
  margin: 0;
  text-align: left;
  font-size: 15px;
  font-weight: 700;
  color: #1f2937;
  cursor: pointer;
}

.question-link:hover {
  text-decoration: underline;
  color: #0d6efd;
}

.badges {
  display: flex;
  align-items: center;
  gap: 6px;
}

.scope {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 88px;
  min-height: 24px;
  padding: 0 8px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 700;
  line-height: 1;
  box-sizing: border-box;
}

.scope.resolved-badge {
  background: #e2e8f0;
  color: #475569;
}

.scope.user {
  background: #d1e7dd;
  color: #0f5132;
}

.scope.admin {
  background: #fff3cd;
  color: #664d03;
}

.scope.platform {
  background: #eef2ff;
  color: #3730a3;
}

.meta {
  display: flex;
  gap: 8px;
  color: #516174;
  font-size: 12px;
  flex-wrap: wrap;
  margin-top: 2px;
}

.item-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 0;
}

.secondary {
  border: 1px solid #0d6efd;
  border-radius: 10px;
  padding: 8px 12px;
  font-weight: 700;
  cursor: pointer;
  background: #ffffff;
  color: #0d6efd;
}

.secondary:hover {
  background: #e7f1ff;
}

.compact-action-btn {
  padding: 6px 12px;
  font-size: 12.5px;
  border-radius: 8px;
  line-height: 1.25;
}

.ghost {
  border: 1px solid #ced4da;
  border-radius: 10px;
  padding: 8px 12px;
  font-weight: 700;
  cursor: pointer;
  background: #ffffff;
  color: #6c757d;
  white-space: nowrap;
  word-break: keep-all;
  flex: 0 0 auto;
}

.ghost:hover {
  background: #f8f9fa;
}

.ghost:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.refresh-fit {
  width: auto;
  margin-left: auto;
}

.pager {
  margin-top: 12px;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  color: #495057;
  font-size: 13px;
}

.empty {
  padding: 16px;
  border-radius: 10px;
  text-align: center;
  background: #f8f9fa;
  color: #6c757d;
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1200;
  padding: 12px;
}

.modal {
  width: min(860px, 100%);
  max-height: 85vh;
  overflow: auto;
  border: 1px solid #dee2e6;
  border-radius: 12px;
  background: #fff;
  padding: 14px;
}

.modal-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}

.modal-head h4 {
  margin: 0;
}

.session-body {
  display: grid;
  gap: 10px;
}

.session-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 12px;
  color: #475569;
}

.session-messages {
  display: grid;
  gap: 8px;
}

.msg-item {
  border: 1px solid #dbe2ea;
  border-radius: 10px;
  padding: 9px 10px;
  background: #fff;
}

.msg-item.user {
  border-color: #c9e4d6;
  background: #f4fbf7;
}

.msg-item.bot {
  border-color: #dbe6ff;
  background: #f7faff;
}

.msg-role {
  font-size: 12px;
  font-weight: 700;
  color: #334155;
  margin-bottom: 4px;
}

.msg-content {
  white-space: pre-wrap;
  color: #1f2937;
  line-height: 1.55;
}

.msg-time {
  margin-top: 6px;
  font-size: 11px;
  color: #64748b;
}

@media (max-width: 900px) {
  .kb-top {
    align-items: stretch;
    flex-direction: column;
  }

  .panel-tools select {
    width: 140px;
    min-width: 140px;
    flex: 0 0 140px;
  }
}
</style>

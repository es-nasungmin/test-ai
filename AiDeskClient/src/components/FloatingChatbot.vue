<script setup>
import { ref, nextTick, computed, onMounted } from 'vue'
import axios from 'axios'
import { API_BASE_URL } from '../config'

const API_URL = API_BASE_URL

// ---- Props ----
const props = defineProps({
  // 'admin' : 내부 상담원, 모든 KB 접근
  // 'user'  : 일반 사용자, 공개 KB만 접근
  role: {
    type: String,
    default: 'user',
    validator: v => ['admin', 'user'].includes(v)
  }
})

const isAdmin = computed(() => props.role === 'admin')

// ---- 스타일 계산 ----
// 두 버튼 모두 오른쪽 세로 배치: admin=78px, user=24px
const fabStyle = computed(() => isAdmin.value
  ? { right: '24px', left: 'auto', bottom: '78px', background: 'linear-gradient(135deg, #f56565 0%, #c05621 100%)' }
  : { right: '24px', left: 'auto', bottom: '24px', background: 'linear-gradient(135deg, #1f7a6d 0%, #155f56 100%)' }
)
const popupStyle = computed(() => isAdmin.value
  ? { right: '24px', left: 'auto', bottom: '146px' }
  : { right: '24px', left: 'auto', bottom: '90px' }
)
const headerGradient = computed(() => isAdmin.value
  ? 'linear-gradient(135deg, #f56565 0%, #c05621 100%)'
  : 'linear-gradient(135deg, #1f7a6d 0%, #155f56 100%)'
)
const userBubbleGradient = computed(() => isAdmin.value
  ? 'linear-gradient(135deg, #f56565 0%, #c05621 100%)'
  : 'linear-gradient(135deg, #1f7a6d 0%, #155f56 100%)'
)

// ---- 상태 ----
const MAX_QUESTION_LENGTH = 300
const isOpen = ref(false)
const question = ref('')
const loading = ref(false)
const messagesEl = ref(null)
const isComposing = ref(false)
const selectedPlatform = ref('전체 플랫폼')
const platformOptions = ref(['전체 플랫폼'])
const BOTTOM_THRESHOLD = 8

// 세션
const sessionId = ref(null)

const messages = ref([])

function defaultWelcome() {
  return {
    role: 'bot',
    text: isAdmin.value
      ? '👋 admin assistant 입니다.\n내부 KB 포함 전체 상담 데이터로 답변합니다.\n\n예시: "인증서 조회가 안 돼요"'
      : '안녕하세요! 😊\n자주 묻는 질문을 기반으로 답변드립니다.\n\n예시: "인증서 조회가 안 돼요"',
    time: now()
  }
}

function linkify(text) {
  if (!text) return ''
  const escapeHtml = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
  const parts = text.split(/(https?:\/\/[^\s]+)/)
  const result = parts.map((part, i) => {
    if (i % 2 === 1) {
      return `<a href="${part}" target="_blank" rel="noopener noreferrer" style="color:#3b82f6;text-decoration:underline;cursor:pointer;word-break:break-all;">${escapeHtml(part)}</a>`
    }
    return escapeHtml(part)
  }).join('')
  console.log('[linkify] input:', text.substring(0, 100))
  console.log('[linkify] output HTML:', result.substring(0, 200))
  return result
}

function now() {
  return new Date().toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' })
}

// ---- 로컬 화면 초기화 ----
function resetLocalChat() {
  sessionId.value = null
  question.value = ''
  loading.value = false
  messages.value = [defaultWelcome()]
}

onMounted(() => {
  resetLocalChat()
  fetchPlatforms()
})

async function fetchPlatforms() {
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/platforms`)
    const list = Array.isArray(res.data) ? res.data : []
    const normalized = list
      .map((x) => (typeof x === 'string' ? x.trim() : ''))
      .filter(Boolean)
    platformOptions.value = ['전체 플랫폼', ...Array.from(new Set(normalized.filter((p) => p !== '공통')))]

    if (!platformOptions.value.includes(selectedPlatform.value)) {
      selectedPlatform.value = '전체 플랫폼'
    }
  } catch {
    platformOptions.value = ['전체 플랫폼']
    selectedPlatform.value = '전체 플랫폼'
  }
}

// ---- 열기/닫기 ----
async function toggle() {
  if (isOpen.value) {
    closeChat()
    return
  }
  await fetchPlatforms()
  isOpen.value = true
  if (messages.value.length === 0) {
    resetLocalChat()
  }
  nextTick(() => scrollToBottom())
}

function closeChat() {
  isOpen.value = false
  // 요구사항: 닫으면 화면은 초기화, 단 DB 저장 이력은 유지
  resetLocalChat()
}

function isNearBottom() {
  if (!messagesEl.value) return true
  const el = messagesEl.value
  return el.scrollHeight - el.scrollTop - el.clientHeight <= BOTTOM_THRESHOLD
}

// ---- 전송 ----
async function send() {
  const q = question.value.trim()
  if (!q || loading.value) return
  if (q.length > MAX_QUESTION_LENGTH) return

  messages.value.push({ role: 'user', text: q, time: now() })
  question.value = ''
  await scrollToBottom(true)
  loading.value = true
  await scrollToBottom(true)

  try {
    const res = await axios.post(`${API_URL}/knowledgebase/ask`, {
      question: q,
      role: props.role,
      platform: selectedPlatform.value,
      sessionId: sessionId.value,
      createSession: sessionId.value === null
    })

    // 새로 생성된 세션 ID는 현재 열린 동안만 유지
    if (res.data.sessionId && !sessionId.value) {
      sessionId.value = res.data.sessionId
    }

    messages.value.push({
      role: 'bot',
      text: res.data.answer,
      relatedKBs: res.data.relatedKBs || [],
      relatedDocuments: res.data.relatedDocuments || [],
      time: now()
    })
  } catch (err) {
    const msg = err.code === 'ERR_NETWORK'
      ? '서버에 연결할 수 없습니다.'
      : err.response?.status === 500
        ? '서버 오류가 발생했습니다.'
        : '오류: ' + (err.response?.data || err.message)
    messages.value.push({ role: 'bot', text: msg, isError: true, time: now() })
  } finally {
    loading.value = false
    await nextTick()
    if (isNearBottom()) scrollToBottom(true)
  }
}

async function scrollToBottom(force = false) {
  await nextTick()
  if (!messagesEl.value) return
  const el = messagesEl.value
  if (force || isNearBottom()) {
    el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' })
  }
}

// ---- 키 이벤트 ----
function onKeydown(e) {
  if (isComposing.value) return
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    send()
  }
}
function onCompositionStart() { isComposing.value = true }
function onCompositionEnd() { isComposing.value = false }
</script>

<template>
  <!-- 플로팅 버튼 -->
  <button
    class="fab"
    :style="fabStyle"
    @click="toggle"
    :class="[{ open: isOpen }, isAdmin ? 'is-admin' : 'is-user']"
    :title="isAdmin ? 'admin' : 'user'"
  >
    <span v-if="!isOpen" class="fab-role-label">{{ isAdmin ? 'admin' : 'user' }}</span>
    <span v-else>✕</span>
  </button>

  <!-- 챗봇 팝업 -->
  <transition name="popup">
    <div v-if="isOpen" class="chatbot-popup" :class="isAdmin ? 'is-admin' : 'is-user'" :style="popupStyle">
      <!-- 헤더 -->
      <div class="popup-header" :style="{ background: headerGradient }">
        <div class="header-left">
          <span class="header-icon" :class="isAdmin ? 'is-admin' : 'is-user'">
            <span class="header-icon-dot"></span>
            <span class="icon-role-label">{{ isAdmin ? 'AD' : 'US' }}</span>
          </span>
          <div>
            <div class="header-title">
              {{ isAdmin ? 'admin' : 'user' }}
            </div>
            <div class="header-sub">
              <span class="dot"></span>
              {{ isAdmin ? 'admin mode' : 'user mode' }}
            </div>
          </div>
        </div>
        <div class="header-actions">
          <select
            v-model="selectedPlatform"
            class="header-platform-select"
            title="질문 플랫폼 선택"
            :disabled="loading"
          >
            <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
          </select>
          <button class="close-btn" @click="closeChat">✕</button>
        </div>
      </div>

      <!-- 메시지 -->
      <div class="messages" ref="messagesEl">
        <div
          v-for="(msg, i) in messages"
          :key="i"
          class="msg-row"
          :class="msg.role"
        >
          <div v-if="msg.role === 'bot'" class="avatar avatar-bot" :class="isAdmin ? 'is-admin' : 'is-user'">
            <span class="avatar-glyph">{{ isAdmin ? 'AD' : 'US' }}</span>
          </div>
          <div class="msg-content">
            <div class="bubble" :class="{ error: msg.isError }">
              <div class="msg-pre" v-html="linkify(msg.text)"></div>
            </div>

            <!-- 관련 KB (관리자에게는 KB 타입/가시성 표시) -->
            <div v-if="isAdmin && msg.relatedKBs && msg.relatedKBs.length" class="related">
              <div class="related-label">📚 참고한 사례</div>
              <div v-for="(kb, j) in msg.relatedKBs" :key="j" class="kb-chip">
                <span class="sim">{{ Math.round(kb.similarity * 100) }}%</span>
                <div class="kb-texts">
                  <div class="kb-problem">{{ kb.title || kb.problem }}</div>
                  <div v-if="kb.matchedQuestion" class="kb-badge">
                    매칭 질문: {{ kb.matchedQuestion }}
                  </div>
                </div>
              </div>
            </div>

            <div v-if="isAdmin && msg.relatedDocuments && msg.relatedDocuments.length" class="related">
              <div class="related-label">📄 참고한 문서</div>
              <div v-for="(doc, j) in msg.relatedDocuments" :key="j" class="kb-chip doc-chip">
                <span class="sim">{{ Math.round(doc.similarity * 100) }}%</span>
                <div class="kb-texts">
                  <div class="kb-problem">{{ doc.displayName }}</div>
                  <div class="kb-badge">p.{{ doc.pageNumber }}</div>
                </div>
              </div>
            </div>

            <div class="msg-time">{{ msg.time }}</div>
          </div>
          <div v-if="msg.role === 'user'" class="avatar avatar-user" :class="isAdmin ? 'is-admin' : 'is-user'">
            <span class="avatar-glyph">{{ isAdmin ? 'AD' : 'US' }}</span>
          </div>
        </div>

        <!-- 로딩 -->
        <div v-if="loading" class="msg-row bot">
          <div class="avatar avatar-bot" :class="isAdmin ? 'is-admin' : 'is-user'">
            <span class="avatar-glyph">{{ isAdmin ? 'AD' : 'US' }}</span>
          </div>
          <div class="bubble loading-bubble">
            <span class="dot-ani" :style="{ background: isAdmin ? '#f56565' : '#1f7a6d' }"></span>
            <span class="dot-ani" :style="{ background: isAdmin ? '#f56565' : '#1f7a6d' }"></span>
            <span class="dot-ani" :style="{ background: isAdmin ? '#f56565' : '#1f7a6d' }"></span>
          </div>
        </div>
      </div>

      <!-- 입력 -->
      <div class="input-row">
        <div class="textarea-wrap">
          <textarea
            v-model="question"
            @keydown="onKeydown"
            @compositionstart="onCompositionStart"
            @compositionend="onCompositionEnd"
            placeholder="질문 입력... (Shift+Enter 줄바꿈)"
            :disabled="loading"
            :maxlength="MAX_QUESTION_LENGTH"
            rows="2"
            :style="{ '--focus-color': isAdmin ? '#f56565' : '#1f7a6d' }"
          ></textarea>
          <span class="char-counter" :class="{ 'near-limit': question.length >= MAX_QUESTION_LENGTH * 0.9, 'at-limit': question.length >= MAX_QUESTION_LENGTH }">
            {{ question.length }}/{{ MAX_QUESTION_LENGTH }}
          </span>
        </div>
        <button
          class="send-btn"
          @click="send"
          :disabled="loading || !question.trim() || question.length > MAX_QUESTION_LENGTH"
          :style="{ background: headerGradient }"
        >
          ➤
        </button>
      </div>
    </div>
  </transition>
</template>

<style scoped>
/* FAB 버튼 */
.fab {
  position: fixed;
  width: 96px;
  height: 40px;
  border-radius: 10px;
  border: 1px solid #dee2e6;
  color: white;
  font-size: 1.1em;
  cursor: pointer;
  box-shadow: 0 0.35rem 0.8rem rgba(0, 0, 0, 0.16);
  z-index: 9999;
  transition: transform 0.2s, box-shadow 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
}

.fab-role-label {
  font-size: 0.86rem;
  font-weight: 800;
  letter-spacing: 0.01em;
  line-height: 1;
  white-space: nowrap;
}

.fab:hover {
  transform: translateY(-1px);
  box-shadow: 0 0.5rem 1rem rgba(0, 0, 0, 0.2);
}

.fab.open {
  background: #6c757d !important;
  box-shadow: 0 0.35rem 0.8rem rgba(0, 0, 0, 0.16);
}

.fab.is-admin {
  border-color: #f5c2c7;
}

.fab.is-user {
  border-color: #badbcc;
}

/* 팝업 */
.chatbot-popup {
  position: fixed;
  width: 348px;
  height: 500px;
  background: #ffffff;
  border-radius: 12px;
  border: 1px solid #dee2e6;
  box-shadow: 0 1rem 2rem rgba(0, 0, 0, 0.18);
  z-index: 9998;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.chatbot-popup.is-admin {
  border-color: #f1c5bf;
}

.chatbot-popup.is-user {
  border-color: #b7ded4;
}

.popup-enter-active, .popup-leave-active {
  transition: all 0.25s ease;
}
.popup-enter-from, .popup-leave-to {
  opacity: 0;
  transform: translateY(16px) scale(0.97);
}

/* 헤더 */
.popup-header {
  color: white;
  padding: 10px 12px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.3);
}

.header-left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.header-icon {
  font-size: 0.74em;
  font-weight: 800;
  letter-spacing: 0.04em;
  border-radius: 50%;
  width: 40px;
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  position: relative;
  border: 1px solid rgba(255, 255, 255, 0.3);
  box-shadow: inset 0 1px 3px rgba(255, 255, 255, 0.25), 0 2px 6px rgba(0, 0, 0, 0.18);
}

.icon-role-label {
  font-weight: 800;
  line-height: 1;
}

.header-icon-dot {
  position: absolute;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #bbf7d0;
  right: -1px;
  top: -1px;
  border: 1px solid rgba(255,255,255,0.65);
}

.header-icon.is-admin {
  background: linear-gradient(145deg, #7f1d1d 0%, #b91c1c 55%, #ef4444 100%);
}

.header-icon.is-user {
  background: linear-gradient(145deg, #065f46 0%, #0f766e 55%, #14b8a6 100%);
}

.header-title { font-weight: 700; font-size: 1em; }
.header-sub   { font-size: 0.8em; opacity: 0.85; display: flex; align-items: center; gap: 5px; }

.header-actions { display: flex; align-items: center; gap: 6px; }

.header-platform-select {
  border: 1px solid rgba(255, 255, 255, 0.5);
  border-radius: 8px;
  padding: 4px 8px;
  background: rgba(255, 255, 255, 0.18);
  color: #ffffff;
  font-size: 0.78em;
  min-width: 96px;
}

.header-platform-select:focus {
  outline: none;
  border-color: rgba(255, 255, 255, 0.85);
}

.header-platform-select option {
  color: #111827;
}

.icon-btn {
  background: rgba(255,255,255,0.15);
  border: none;
  border-radius: 6px;
  color: white;
  font-size: 0.9em;
  padding: 4px 7px;
  cursor: pointer;
}
.icon-btn:hover { background: rgba(255,255,255,0.3); }

.dot {
  width: 7px; height: 7px;
  background: #4ade80;
  border-radius: 50%;
  display: inline-block;
  animation: pulse 2s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.close-btn {
  background: rgba(255,255,255,0.2);
  border: none;
  border-radius: 6px;
  color: white;
  font-size: 0.9em;
  padding: 4px 8px;
  cursor: pointer;
}
.close-btn:hover { background: rgba(255,255,255,0.35); }

/* 메시지 */
.messages {
  flex: 1;
  overflow-y: auto;
  padding: 14px;
  background: #f8f9fa;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.msg-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
}
.msg-row.user { flex-direction: row-reverse; }

.avatar {
  font-size: 1em;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: white;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 2px 10px rgba(15, 23, 42, 0.14);
  flex-shrink: 0;
  border: 1px solid rgba(255, 255, 255, 0.75);
}

.avatar-glyph {
  color: #fff;
  font-size: 0.52em;
  font-weight: 800;
  letter-spacing: 0.03em;
  line-height: 1;
}

.avatar-bot.is-admin {
  background: linear-gradient(145deg, #7f1d1d 0%, #b91c1c 55%, #ef4444 100%);
}

.avatar-bot.is-user {
  background: linear-gradient(145deg, #0b4f4a 0%, #0f766e 55%, #14b8a6 100%);
}

.avatar-user.is-admin {
  background: linear-gradient(145deg, #9f1239 0%, #e11d48 55%, #fb7185 100%);
}

.avatar-user.is-user {
  background: linear-gradient(145deg, #0f766e 0%, #14b8a6 55%, #5eead4 100%);
}

.msg-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-width: 78%;
}

.bubble {
  background: #ffffff;
  padding: 10px 13px;
  border-radius: 10px;
  font-size: 0.92em;
  line-height: 1.5;
  border: 1px solid #e9ecef;
  box-shadow: 0 2px 6px rgba(0,0,0,0.06);
}

.msg-row.user .bubble {
  color: white;
  border-radius: 10px;
  /* 배경은 인라인 스타일로 role에 따라 주입됨 */
  background: v-bind(userBubbleGradient);
}

.bubble pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
}

.bubble .msg-pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
}

.bubble .msg-pre a {
  color: #3b82f6;
  text-decoration: underline;
  word-break: break-all;
  cursor: pointer;
}

.bubble pre :deep(a) {
  color: #3b82f6;
  text-decoration: underline;
  word-break: break-all;
}

.bubble.error {
  background: #fff5f5;
  border: 1px solid #fed7d7;
  color: #c53030;
}

/* 관련 KB */
.related {
  font-size: 0.8em;
  background: #f0f4ff;
  border: 1px solid #c3d3ff;
  border-radius: 8px;
  padding: 8px;
}

.related-label {
  font-weight: 700;
  color: #4a5568;
  margin-bottom: 6px;
}

.kb-chip {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 8px;
  align-items: start;
  padding: 6px 0;
  border-top: 1px solid #e2eaff;
}

.kb-chip:first-of-type {
  border-top: none;
  padding-top: 0;
}

.doc-chip .sim {
  background: #2d9c7f;
}

.sim {
  background: #667eea;
  color: white;
  border-radius: 4px;
  padding: 2px 6px;
  font-size: 0.85em;
  font-weight: 700;
  white-space: nowrap;
  flex-shrink: 0;
}

.kb-texts {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.kb-problem {
  color: #555;
  line-height: 1.4;
  word-break: break-word;
}

.kb-badge {
  font-size: 0.78em;
  background: #fef3c7;
  color: #92400e;
  border-radius: 6px;
  padding: 4px 6px;
  line-height: 1.35;
  word-break: break-word;
}

.msg-time { font-size: 0.72em; color: #a0aec0; padding: 0 2px; }
.msg-row.user .msg-time { text-align: right; }

/* 로딩 */
.loading-bubble {
  display: flex; gap: 5px; align-items: center; padding: 12px 16px;
}

.dot-ani {
  width: 7px; height: 7px;
  border-radius: 50%;
  animation: bounce 1.2s infinite ease-in-out;
}
.dot-ani:nth-child(1) { animation-delay: 0s; }
.dot-ani:nth-child(2) { animation-delay: 0.2s; }
.dot-ani:nth-child(3) { animation-delay: 0.4s; }

@keyframes bounce {
  0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
  40% { transform: scale(1); opacity: 1; }
}

/* 입력 */
.input-row {
  display: flex;
  gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid #dee2e6;
  background: #ffffff;
  flex-shrink: 0;
}

.textarea-wrap {
  flex: 1;
  position: relative;
  display: flex;
  flex-direction: column;
}

.input-row textarea {
  width: 100%;
  padding: 8px 12px;
  padding-bottom: 20px;
  border: 1px solid #ced4da;
  border-radius: 10px;
  font-size: 0.92em;
  resize: none;
  font-family: inherit;
  line-height: 1.4;
  background: #ffffff;
  transition: border-color 0.2s, box-shadow 0.2s;
  box-sizing: border-box;
}

.char-counter {
  position: absolute;
  bottom: 5px;
  right: 10px;
  font-size: 0.72em;
  color: #a0aec0;
  pointer-events: none;
  transition: color 0.2s;
}

.char-counter.near-limit { color: #ed8936; }
.char-counter.at-limit { color: #e53e3e; font-weight: 600; }

.input-row textarea:focus {
  outline: none;
  border-color: var(--focus-color, #667eea);
  box-shadow: 0 0 0 0.2rem rgba(13, 110, 253, 0.2);
}

.input-row textarea:disabled { background: #f7fafc; color: #a0aec0; }

.send-btn {
  min-width: 44px;
  padding: 0 14px;
  color: white;
  border: none;
  border-radius: 8px;
  font-size: 1.1em;
  cursor: pointer;
  box-shadow: none;
  transition: transform 0.18s, opacity 0.2s;
}

.send-btn:hover:not(:disabled) { opacity: 0.9; }
.send-btn:disabled { opacity: 0.4; cursor: not-allowed; }
</style>

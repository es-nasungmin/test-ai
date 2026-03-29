<script setup>
import { ref, nextTick } from 'vue'
import axios from 'axios'

const API_URL = 'http://localhost:8080/api/knowledgebase'

const isOpen = ref(false)
const question = ref('')
const loading = ref(false)
const messagesEl = ref(null)
const isComposing = ref(false)
const BOTTOM_THRESHOLD = 8

const messages = ref([
  {
    role: 'bot',
    text: '안녕하세요! 😊\n상담내역 기반으로 질문에 답변드립니다.\n\n예시: "계산서 조회가 안 돼요"',
    time: now()
  }
])

function now() {
  return new Date().toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' })
}

function toggle() {
  isOpen.value = !isOpen.value
  if (isOpen.value) {
    nextTick(() => scrollToBottom())
  }
}

function isNearBottom() {
  if (!messagesEl.value) return true
  const el = messagesEl.value
  return el.scrollHeight - el.scrollTop - el.clientHeight <= BOTTOM_THRESHOLD
}

async function send() {
  const q = question.value.trim()
  if (!q || loading.value) return

  messages.value.push({ role: 'user', text: q, time: now() })
  question.value = ''
  await scrollToBottom(true)
  loading.value = true
  await scrollToBottom(true)

  try {
    const res = await axios.post(`${API_URL}/ask`, { question: q })
    messages.value.push({
      role: 'bot',
      text: res.data.answer,
      relatedKBs: res.data.relatedKBs || [],
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
    if (isNearBottom()) {
      scrollToBottom(true)
    }
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

function onKeydown(e) {
  if (isComposing.value) return

  // Enter 전송, Shift+Enter는 줄바꿈
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    send()
  }
}

function onCompositionStart() {
  isComposing.value = true
}

function onCompositionEnd() {
  isComposing.value = false
}
</script>

<template>
  <!-- 플로팅 버튼 -->
  <button class="fab" @click="toggle" :class="{ open: isOpen }" title="AI 챗봇">
    <span v-if="!isOpen">🤖</span>
    <span v-else>✕</span>
  </button>

  <!-- 챗봇 팝업 -->
  <transition name="popup">
    <div v-if="isOpen" class="chatbot-popup">
      <!-- 헤더 -->
      <div class="popup-header">
        <div class="header-left">
          <span class="header-icon">🤖</span>
          <div>
            <div class="header-title">AI 상담 어시스턴트</div>
            <div class="header-sub"><span class="dot"></span> 상담내역 기반 RAG</div>
          </div>
        </div>
        <button class="close-btn" @click="isOpen = false">✕</button>
      </div>

      <!-- 메시지 -->
      <div class="messages" ref="messagesEl">
        <div
          v-for="(msg, i) in messages"
          :key="i"
          class="msg-row"
          :class="msg.role"
        >
          <div v-if="msg.role === 'bot'" class="avatar">🤖</div>
          <div class="msg-content">
            <div class="bubble" :class="{ error: msg.isError }">
              <pre>{{ msg.text }}</pre>
            </div>

            <!-- 관련 KB -->
            <div v-if="msg.relatedKBs && msg.relatedKBs.length" class="related">
              <div class="related-label">📚 참고한 사례</div>
              <div v-for="(kb, j) in msg.relatedKBs" :key="j" class="kb-chip">
                <span class="sim">{{ Math.round(kb.similarity * 100) }}%</span>
                <span class="kb-problem">{{ kb.problem }}</span>
              </div>
            </div>

            <div class="msg-time">{{ msg.time }}</div>
          </div>
          <div v-if="msg.role === 'user'" class="avatar user">👤</div>
        </div>

        <!-- 로딩 -->
        <div v-if="loading" class="msg-row bot">
          <div class="avatar">🤖</div>
          <div class="bubble loading-bubble">
            <span class="dot-ani"></span>
            <span class="dot-ani"></span>
            <span class="dot-ani"></span>
          </div>
        </div>
      </div>

      <!-- 입력 -->
      <div class="input-row">
        <textarea
          v-model="question"
          @keydown="onKeydown"
          @compositionstart="onCompositionStart"
          @compositionend="onCompositionEnd"
          placeholder="질문 입력... (Shift+Enter 줄바꿈)"
          :disabled="loading"
          rows="2"
        ></textarea>
        <button class="send-btn" @click="send" :disabled="loading || !question.trim()">
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
  bottom: 24px;
  right: 24px;
  width: 56px;
  height: 56px;
  border-radius: 50%;
  border: none;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  font-size: 1.5em;
  cursor: pointer;
  box-shadow: 0 4px 16px rgba(102,126,234,0.5);
  z-index: 9999;
  transition: transform 0.2s, box-shadow 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
}

.fab:hover {
  transform: scale(1.1);
  box-shadow: 0 6px 24px rgba(102,126,234,0.6);
}

.fab.open {
  background: #4a5568;
  box-shadow: 0 4px 16px rgba(0,0,0,0.3);
}

/* 팝업 */
.chatbot-popup {
  position: fixed;
  bottom: 90px;
  right: 24px;
  width: 360px;
  height: 520px;
  background: white;
  border-radius: 16px;
  box-shadow: 0 8px 40px rgba(0,0,0,0.2);
  z-index: 9998;
  display: flex;
  flex-direction: column;
  overflow: hidden;
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
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  padding: 12px 14px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.header-icon {
  font-size: 1.6em;
  background: rgba(255,255,255,0.2);
  border-radius: 50%;
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.header-title { font-weight: 700; font-size: 0.95em; }
.header-sub   { font-size: 0.75em; opacity: 0.85; display: flex; align-items: center; gap: 5px; }

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
  background: #f8f9fb;
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
  font-size: 1.3em;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: white;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 2px 6px rgba(0,0,0,0.1);
  flex-shrink: 0;
}

.avatar.user { background: #667eea; }

.msg-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-width: 78%;
}

.bubble {
  background: white;
  padding: 10px 13px;
  border-radius: 14px 14px 14px 4px;
  font-size: 0.88em;
  line-height: 1.5;
  box-shadow: 0 2px 6px rgba(0,0,0,0.07);
}

.msg-row.user .bubble {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border-radius: 14px 14px 4px 14px;
}

.bubble pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
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

.related-label { font-weight: 700; color: #4a5568; margin-bottom: 5px; }

.kb-chip {
  display: flex;
  gap: 6px;
  align-items: flex-start;
  padding: 4px 0;
  border-top: 1px solid #e2eaff;
}

.sim {
  background: #667eea;
  color: white;
  border-radius: 4px;
  padding: 1px 5px;
  font-size: 0.85em;
  font-weight: 700;
  white-space: nowrap;
  flex-shrink: 0;
}

.kb-problem { color: #555; line-height: 1.4; }

.msg-time { font-size: 0.72em; color: #a0aec0; padding: 0 2px; }
.msg-row.user .msg-time { text-align: right; }

/* 로딩 */
.loading-bubble {
  display: flex; gap: 5px; align-items: center; padding: 12px 16px;
}

.dot-ani {
  width: 7px; height: 7px;
  background: #667eea;
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
  border-top: 1px solid #e2e8f0;
  background: white;
  flex-shrink: 0;
}

.input-row textarea {
  flex: 1;
  padding: 8px 12px;
  border: 2px solid #e2e8f0;
  border-radius: 10px;
  font-size: 0.88em;
  resize: none;
  font-family: inherit;
  line-height: 1.4;
  transition: border-color 0.2s;
}

.input-row textarea:focus {
  outline: none;
  border-color: #667eea;
}

.input-row textarea:disabled { background: #f7fafc; color: #a0aec0; }

.send-btn {
  padding: 0 14px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border: none;
  border-radius: 10px;
  font-size: 1.1em;
  cursor: pointer;
  transition: opacity 0.2s;
}

.send-btn:hover:not(:disabled) { opacity: 0.85; }
.send-btn:disabled { opacity: 0.4; cursor: not-allowed; }
</style>

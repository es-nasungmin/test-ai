<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { useChatbotSession } from './useChatbotSession'

const props = defineProps({
  apiBaseUrl: { type: String, default: 'http://localhost:8080/api' },
  role: { type: String, default: 'user' },
  title: { type: String, default: 'AI 상담 어시스턴트' },
  defaultPlatform: { type: String, default: '전체 플랫폼' },
  showPlatformSelector: { type: Boolean, default: false },
  fabLabel: { type: String, default: 'CHAT' },
  accent: { type: String, default: '#0d6efd' },
  inline: { type: Boolean, default: false },
  initiallyOpen: { type: Boolean, default: false },
  fabRight: { type: String, default: '20px' },
  fabBottom: { type: String, default: '20px' },
  popupRight: { type: String, default: '20px' },
  popupBottom: { type: String, default: '88px' }
})

const isOpen = ref(props.inline ? true : props.initiallyOpen)
const messagesEl = ref(null)
const isComposing = ref(false)
const BOTTOM_THRESHOLD = 8
const instanceId = `chat-${Math.random().toString(36).slice(2, 10)}`

const bot = useChatbotSession({
  apiBaseUrl: props.apiBaseUrl,
  role: props.role,
  defaultPlatform: props.defaultPlatform
})

const isAdmin = computed(() => bot.isAdmin.value)

const fabStyle = computed(() => {
  if (props.inline) return {}
  if (isAdmin.value) {
    return {
      left: props.fabRight,
      right: 'auto',
      bottom: props.fabBottom,
      background: 'linear-gradient(135deg, #f56565 0%, #c05621 100%)'
    }
  }
  return {
    right: props.fabRight,
    left: 'auto',
    bottom: props.fabBottom,
    background: 'linear-gradient(135deg, #1f7a6d 0%, #155f56 100%)'
  }
})

const popupStyle = computed(() => {
  if (props.inline) return {}
  if (isAdmin.value) {
    return {
      left: props.popupRight,
      right: 'auto',
      bottom: props.popupBottom
    }
  }
  return {
    right: props.popupRight,
    left: 'auto',
    bottom: props.popupBottom
  }
})

const headerGradient = computed(() => (
  isAdmin.value
    ? 'linear-gradient(135deg, #f56565 0%, #c05621 100%)'
    : 'linear-gradient(135deg, #1f7a6d 0%, #155f56 100%)'
))

const userBubbleGradient = computed(() => (
  isAdmin.value
    ? 'linear-gradient(135deg, #f56565 0%, #c05621 100%)'
    : 'linear-gradient(135deg, #1f7a6d 0%, #155f56 100%)'
))

const closedFabLabel = computed(() => {
  const label = String(props.fabLabel || '').trim()
  return label || (isAdmin.value ? 'admin' : 'user')
})

const headerSubText = computed(() => {
  const p = typeof bot.selectedPlatform.value === 'string' ? bot.selectedPlatform.value.trim() : ''
  const hasPlatform = p && p !== '전체 플랫폼'
  if (isAdmin.value) {
    return hasPlatform ? `${p} admin 문의를 도와드려요` : 'admin 문의를 도와드려요'
  }
  return hasPlatform ? `${p} user 문의를 도와드려요` : '무엇이든 편하게 질문해 주세요'
})

function toDisplayText(value) {
  if (typeof value === 'string') return value
  if (value == null) return ''
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  if (typeof value === 'object') {
    if (typeof value.message === 'string' && value.message.trim()) return value.message
    if (typeof value.error === 'string' && value.error.trim()) return value.error
    try {
      const json = JSON.stringify(value, null, 2)
      return json && json !== '{}' ? json : ''
    } catch {
      return String(value)
    }
  }
  return String(value)
}

function applyPlatformLock() {
  if (props.showPlatformSelector) return
  bot.selectedPlatform.value = props.defaultPlatform || '전체 플랫폼'
}

function isNearBottom() {
  if (!messagesEl.value) return true
  const el = messagesEl.value
  return el.scrollHeight - el.scrollTop - el.clientHeight <= BOTTOM_THRESHOLD
}

async function toggle() {
  if (isOpen.value) {
    closeChat()
    return
  }
  if (typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('crm-chat-open', { detail: { instanceId } }))
  }
  await bot.refreshPlatforms()
  applyPlatformLock()
  isOpen.value = true
  await nextTick()
  scrollToBottom(true)
}

function closeChat() {
  isOpen.value = false
  bot.reset()
}

async function submit() {
  if (!bot.question.value.trim() || bot.loading.value) return
  await bot.send()
  await nextTick()
  if (isNearBottom()) {
    scrollToBottom(true)
  }
}

function scrollToBottom(force = false) {
  if (!messagesEl.value) return
  if (force || isNearBottom()) {
    messagesEl.value.scrollTo({ top: messagesEl.value.scrollHeight, behavior: 'smooth' })
  }
}

function onKeydown(e) {
  if (isComposing.value) return
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    submit()
  }
}

onMounted(() => {
  bot.refreshPlatforms()
  applyPlatformLock()

  if (typeof window !== 'undefined') {
    window.addEventListener('crm-chat-open', handleOpenEvent)
  }
})

onBeforeUnmount(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('crm-chat-open', handleOpenEvent)
  }
})

function handleOpenEvent(event) {
  if (props.inline) return
  const openedBy = event?.detail?.instanceId
  if (!openedBy || openedBy === instanceId) return
  if (isOpen.value) {
    closeChat()
  }
}
</script>

<template>
  <button
    v-if="!props.inline"
    class="fab"
    :style="fabStyle"
    :class="[{ open: isOpen }, isAdmin ? 'is-admin' : 'is-user']"
    @click="toggle"
    :title="isAdmin ? 'admin' : 'user'"
  >
    <span v-if="!isOpen" class="fab-role-label">{{ closedFabLabel }}</span>
    <span v-else>✕</span>
  </button>

  <transition name="popup">
    <div
      v-if="isOpen"
      class="chatbot-popup"
      :class="[{ inline: props.inline }, isAdmin ? 'is-admin' : 'is-user']"
      :style="popupStyle"
    >
      <div class="popup-header" :style="{ background: headerGradient }">
        <div class="header-left">
          <span class="header-icon" :class="isAdmin ? 'is-admin' : 'is-user'">
            <span class="header-icon-dot"></span>
            {{ isAdmin ? 'AD' : 'US' }}
          </span>
          <div class="header-meta">
            <div class="header-title">{{ props.title }}</div>
            <div class="header-sub">
              <span class="dot"></span>
              {{ headerSubText }}
            </div>
          </div>
        </div>
        <div class="header-actions">
          <select
            v-if="props.showPlatformSelector"
            v-model="bot.selectedPlatform"
            class="header-platform-select"
            title="질문 플랫폼 선택"
            :disabled="bot.loading"
          >
            <option v-for="p in bot.platformOptions" :key="p" :value="p">{{ p }}</option>
          </select>
          <button class="close-btn" @click="closeChat">✕</button>
        </div>
      </div>

      <div class="messages" ref="messagesEl">
        <div v-for="(msg, i) in bot.messages" :key="i" class="msg-row" :class="msg.role">
          <div v-if="msg.role === 'bot'" class="avatar avatar-bot" :class="isAdmin ? 'is-admin' : 'is-user'">
            <span class="avatar-glyph">{{ isAdmin ? 'AD' : 'US' }}</span>
          </div>
          <div class="msg-content">
            <div class="bubble" :class="{ error: msg.isError }">
              <pre>{{ toDisplayText(msg.text) }}</pre>
            </div>

            <div v-if="isAdmin && msg.relatedKBs && msg.relatedKBs.length" class="related">
              <div class="related-label">📚 참고한 사례</div>
              <div v-for="(kb, j) in msg.relatedKBs" :key="j" class="kb-chip">
                <span class="sim">{{ Math.round(kb.similarity * 100) }}%</span>
                <div class="kb-texts">
                  <div class="kb-problem">{{ kb.problem }}</div>
                  <div v-if="kb.matchedQuestion" class="kb-badge">
                    매칭 질문: {{ kb.matchedQuestion }}
                  </div>
                </div>
              </div>
            </div>

            <div class="msg-time">{{ msg.time }}</div>
          </div>
          <div v-if="msg.role === 'user'" class="avatar avatar-user" :class="isAdmin ? 'is-admin' : 'is-user'">
            <span class="avatar-glyph">{{ isAdmin ? 'AD' : 'US' }}</span>
          </div>
        </div>

        <div v-if="bot.loading" class="msg-row bot">
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

      <div class="input-row">
        <textarea
          v-model="bot.question"
          rows="2"
          placeholder="질문 입력... (Shift+Enter 줄바꿈)"
          @keydown="onKeydown"
          @compositionstart="isComposing = true"
          @compositionend="isComposing = false"
          :disabled="bot.loading"
          :style="{ '--focus-color': isAdmin ? '#f56565' : '#1f7a6d' }"
        />
        <button
          class="send-btn"
          :style="{ background: headerGradient }"
          :disabled="bot.loading || !bot.question.trim()"
          @click="submit"
        >
          ➤
        </button>
      </div>
    </div>
  </transition>
</template>

<style scoped>
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

.fab:hover {
  transform: translateY(-1px);
  box-shadow: 0 0.5rem 1rem rgba(0, 0, 0, 0.2);
}

.fab-role-label {
  font-size: 0.86rem;
  font-weight: 800;
  letter-spacing: 0.01em;
  line-height: 1;
  white-space: nowrap;
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

.chatbot-popup {
  position: fixed;
  width: 356px;
  height: 492px;
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

.chatbot-popup.inline {
  position: relative;
  right: auto;
  bottom: auto;
  width: 100%;
  max-width: 100%;
  box-shadow: 0 0.45rem 1rem rgba(0, 0, 0, 0.06);
}

.popup-enter-active, .popup-leave-active {
  transition: all 0.25s ease;
}

.popup-enter-from, .popup-leave-to {
  opacity: 0;
  transform: translateY(16px) scale(0.97);
}

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
  flex: 1;
  min-width: 0;
}

.header-meta {
  min-width: 0;
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

.header-title {
  font-weight: 700;
  font-size: 1em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.header-sub {
  font-size: 0.8em;
  opacity: 0.85;
  display: flex;
  align-items: center;
  gap: 5px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

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

.dot {
  width: 7px;
  height: 7px;
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

.close-btn:hover {
  background: rgba(255,255,255,0.35);
}

.messages {
  flex: 1;
  min-height: 0;
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

.msg-row.user {
  flex-direction: row-reverse;
}

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
  max-width: calc(100% - 52px);
  min-width: 0;
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
  background: v-bind(userBubbleGradient);
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

.msg-time {
  font-size: 0.72em;
  color: #a0aec0;
  padding: 0 2px;
}

.msg-row.user .msg-time {
  text-align: right;
}

.loading-bubble {
  display: flex;
  gap: 5px;
  align-items: center;
  padding: 12px 16px;
}

.dot-ani {
  width: 7px;
  height: 7px;
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

.input-row {
  display: flex;
  gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid #dee2e6;
  background: #ffffff;
  flex-shrink: 0;
}

.input-row textarea {
  flex: 1;
  padding: 8px 12px;
  border: 1px solid #ced4da;
  border-radius: 10px;
  font-size: 0.92em;
  resize: none;
  font-family: inherit;
  line-height: 1.4;
  background: #ffffff;
  transition: border-color 0.2s, box-shadow 0.2s;
}

.input-row textarea:focus {
  outline: none;
  border-color: var(--focus-color, #667eea);
  box-shadow: 0 0 0 0.2rem rgba(13, 110, 253, 0.2);
}

.input-row textarea:disabled {
  background: #f7fafc;
  color: #a0aec0;
}

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

.send-btn:hover:not(:disabled) {
  opacity: 0.9;
}

.send-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>

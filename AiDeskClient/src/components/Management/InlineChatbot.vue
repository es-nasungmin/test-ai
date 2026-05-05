<script setup>
import { onMounted, onBeforeUnmount, ref } from 'vue'
import { API_BASE_URL } from '../../config'

const props = defineProps({
  role: { type: String, default: 'admin' }
})

const containerRef = ref(null)

function getUserInfo() {
  try {
    const raw = localStorage.getItem('user')
    if (!raw) return {}
    const u = JSON.parse(raw)
    return {
      userId: u?.id ?? u?.userId ?? '',
      username: u?.username ?? '',
      userLoginId: u?.loginId ?? u?.userLoginId ?? ''
    }
  } catch {
    return {}
  }
}

onMounted(() => {
  if (!containerRef.value) return
  if (!window.createCrmChatWidget) {
    // chat-widget.js가 로드 안된 경우 동적 로드
    const script = document.createElement('script')
    script.src = '/chat-widget.js'
    script.onload = mountWidget
    document.head.appendChild(script)
  } else {
    mountWidget()
  }
})

let widgetInstance = null

function mountWidget() {
  if (!containerRef.value) return
  const userInfo = getUserInfo()
  widgetInstance = window.createCrmChatWidget({
    apiBaseUrl: API_BASE_URL,
    role: props.role,
    userId: userInfo.userId,
    username: userInfo.username,
    userLoginId: userInfo.userLoginId,
    title: '채팅',
    showPlatformSelector: true,
    hideButton: true,
    initiallyOpen: true,
    mountTo: containerRef.value
  })
}

onBeforeUnmount(() => {
  if (containerRef.value) containerRef.value.innerHTML = ''
  widgetInstance = null
})
</script>

<template>
  <div class="inline-chatbot-wrap">
    <div ref="containerRef" class="inline-chatbot-mount"></div>
  </div>
</template>

<style scoped>
.inline-chatbot-wrap {
  width: 100%;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 8px 0;
}

.inline-chatbot-mount {
  width: 360px;
  flex-shrink: 0;
  position: relative;
  min-height: 520px;
  display: flex;
  justify-content: center;
}

/* popup을 fixed 대신 block으로 덮어쓰기 */
.inline-chatbot-mount :deep(.crm-chat-popup) {
  position: relative !important;
  right: auto !important;
  bottom: auto !important;
  left: auto !important;
  top: auto !important;
  width: 360px !important;
  height: 520px !important;
  display: flex !important;
  box-shadow: 0 4px 24px rgba(0,0,0,0.12);
}
</style>

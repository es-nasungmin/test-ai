import { computed, ref } from 'vue'
import { createChatbotApi } from './chatbotApi'

export function useChatbotSession(options) {
  const api = createChatbotApi(options.apiBaseUrl)

  const role = ref(options.role || 'user')
  const selectedPlatform = ref(options.defaultPlatform || '전체 플랫폼')
  const platformOptions = ref(['전체 플랫폼'])

  const question = ref('')
  const loading = ref(false)
  const sessionId = ref(null)
  const messages = ref([])

  const isAdmin = computed(() => role.value === 'admin')

  function toSafeText(value, fallback = '요청 처리 중 오류가 발생했습니다.') {
    if (typeof value === 'string') {
      const t = value.trim()
      return t || fallback
    }
    if (value == null) return fallback
    if (typeof value === 'number' || typeof value === 'boolean') return String(value)
    if (Array.isArray(value)) {
      const joined = value.map((v) => toSafeText(v, '')).filter(Boolean).join('\n')
      return joined || fallback
    }
    if (typeof value === 'object') {
      if (typeof value.message === 'string' && value.message.trim()) return value.message.trim()
      if (typeof value.error === 'string' && value.error.trim()) return value.error.trim()
      try {
        const json = JSON.stringify(value)
        return json && json !== '{}' ? json : fallback
      } catch {
        return fallback
      }
    }
    return fallback
  }

  function now() {
    return new Date().toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' })
  }

  function platformLabel(platform) {
    const p = typeof platform === 'string' ? platform.trim() : ''
    if (!p || p === '전체 플랫폼') return ''
    return p
  }

  function welcomeText() {
    const platform = platformLabel(selectedPlatform.value)
      if (isAdmin.value) {
        return platform
          ? `안녕하세요! ${platform} 상담 도우미입니다.\nadmin 문의를 도와드릴게요.\n\n예시: "인증서 조회가 안 돼요"`
          : '안녕하세요! 상담 도우미입니다.\nadmin 문의를 도와드릴게요.\n\n예시: "인증서 조회가 안 돼요"'
    }
    return platform
      ? `안녕하세요! ${platform} 고객센터 챗봇입니다.\n무엇을 도와드릴까요?\n\n예시: "인증서 조회가 안 돼요"`
      : '안녕하세요! 고객센터 챗봇입니다.\n무엇을 도와드릴까요?\n\n예시: "인증서 조회가 안 돼요"'
  }

  function reset() {
    sessionId.value = null
    question.value = ''
    loading.value = false
    messages.value = [{ role: 'bot', text: welcomeText(), time: now() }]
  }

  async function refreshPlatforms() {
    try {
      const list = await api.getPlatforms()
      platformOptions.value = list.length > 0 ? list : ['전체 플랫폼']
      if (!platformOptions.value.includes(selectedPlatform.value)) {
        selectedPlatform.value = '전체 플랫폼'
      }
    } catch {
      platformOptions.value = ['전체 플랫폼']
      selectedPlatform.value = '전체 플랫폼'
    }
  }

  async function send() {
    const q = question.value.trim()
    if (!q || loading.value) return

    messages.value.push({ role: 'user', text: q, time: now() })
    question.value = ''
    loading.value = true

    try {
      const data = await api.ask({
        question: q,
        role: role.value,
        platform: selectedPlatform.value,
        sessionId: sessionId.value,
        createSession: sessionId.value === null
      })

      if (data.sessionId && !sessionId.value) {
        sessionId.value = data.sessionId
      }

      messages.value.push({
        role: 'bot',
        text: toSafeText(data.answer, '답변을 생성하지 못했습니다.'),
        time: now(),
        relatedKBs: data.relatedKBs || [],
        topSimilarity: data.topSimilarity,
        isLowSimilarity: data.isLowSimilarity
      })
    } catch (err) {
      const text = toSafeText(err?.response?.data?.error ?? err?.response?.data ?? err?.message)
      messages.value.push({ role: 'bot', text, time: now(), isError: true })
    } finally {
      loading.value = false
    }
  }

  reset()

  return {
    role,
    isAdmin,
    selectedPlatform,
    platformOptions,
    question,
    loading,
    messages,
    reset,
    send,
    refreshPlatforms
  }
}

import axios from 'axios'

export function createChatbotApi(apiBaseUrl) {
  const base = apiBaseUrl.replace(/\/$/, '')
  const client = axios.create({
    timeout: 20000
  })

  async function getPlatforms() {
    const res = await client.get(`${base}/knowledgebase/platforms`)
    const list = Array.isArray(res.data) ? res.data : []
    const normalized = list
      .map((x) => (typeof x === 'string' ? x.trim() : ''))
      .filter(Boolean)

    return ['전체 플랫폼', ...Array.from(new Set(normalized.filter((p) => p !== '공통')))]
  }

  async function ask({ question, role, platform, sessionId, createSession }) {
    const res = await client.post(`${base}/knowledgebase/ask`, {
      question,
      role,
      platform,
      sessionId,
      createSession
    })

    return res.data
  }

  return {
    getPlatforms,
    ask
  }
}

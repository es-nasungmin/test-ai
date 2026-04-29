#!/usr/bin/env node

const { kbCatalog } = require('./demo-kb-catalog')

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:8080/api'
const RESET_BEFORE_SEED = (process.env.RESET_BEFORE_SEED || 'true').toLowerCase() !== 'false'

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

async function requestJson(url, options = {}) {
  const response = await fetch(url, options)
  const text = await response.text()
  let data = null
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    data = { raw: text }
  }

  if (!response.ok) {
    const error = new Error(`HTTP ${response.status} ${response.statusText}`)
    error.status = response.status
    error.data = data
    throw error
  }

  return data
}

async function listAllKnowledgeBases() {
  const pageSize = 100
  let page = 1
  let all = []

  while (true) {
    const data = await requestJson(`${API_BASE_URL}/knowledgebase/list?page=${page}&pageSize=${pageSize}`)
    const rows = Array.isArray(data?.data) ? data.data : []
    all = all.concat(rows)

    const total = Number(data?.total || 0)
    if (all.length >= total || rows.length === 0) {
      break
    }

    page += 1
  }

  return all
}

async function resetKnowledgeBases() {
  const current = await listAllKnowledgeBases()
  if (current.length === 0) {
    console.log('[seed] reset skipped: no KB found')
    return
  }

  console.log(`[seed] deleting existing KB: ${current.length}`)
  let deleted = 0
  for (const kb of current) {
    try {
      await requestJson(`${API_BASE_URL}/knowledgebase/${kb.id}`, { method: 'DELETE' })
      deleted += 1
    } catch (err) {
      console.error(`[seed] delete failed id=${kb.id}`, err.data || err.message)
    }
    await sleep(30)
  }
  console.log(`[seed] deleted: ${deleted}/${current.length}`)
}

async function createKnowledgeBase(item) {
  const payload = {
    title: item.title,
    content: item.content,
    visibility: 'user',
    platforms: ['공통'],
    keywords: item.keywords.join(', '),
    expectedQuestions: item.expectedQuestions
  }

  return requestJson(`${API_BASE_URL}/knowledgebase`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(payload)
  })
}

async function main() {
  console.log(`[seed] API_BASE_URL=${API_BASE_URL}`)
  console.log(`[seed] catalog size=${kbCatalog.length}`)

  if (RESET_BEFORE_SEED) {
    await resetKnowledgeBases()
  }

  let success = 0
  let failed = 0

  for (const item of kbCatalog) {
    try {
      await createKnowledgeBase(item)
      success += 1
      console.log(`[seed] created: ${item.title}`)
    } catch (err) {
      failed += 1
      console.error(`[seed] create failed: ${item.title}`, err.data || err.message)
    }
    await sleep(40)
  }

  const all = await listAllKnowledgeBases()
  console.log('\n[seed] done')
  console.log(`[seed] success=${success}, failed=${failed}, totalInDb=${all.length}`)

  if (failed > 0) {
    process.exitCode = 1
  }
}

main().catch((err) => {
  console.error('[seed] fatal', err)
  process.exit(1)
})

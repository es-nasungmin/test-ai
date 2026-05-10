#!/usr/bin/env node

const fs = require('fs')
const path = require('path')

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:8080/api'
const OUTPUT_DIR = path.resolve(process.cwd(), 'reports')
const BENCH_AUTH_TOKEN = process.env.BENCH_AUTH_TOKEN || ''
const MAX_SESSIONS = Number(process.env.BENCH_MAX_SESSIONS || 300)
const MAX_CASES = Number(process.env.BENCH_MAX_CASES || 300)
const CONCURRENCY = Math.max(1, Number(process.env.BENCH_CONCURRENCY || 8))
const REPLAY_ROUNDS = Math.max(1, Number(process.env.BENCH_REPLAY_ROUNDS || 20))

function headers() {
  const h = { 'Content-Type': 'application/json' }
  if (BENCH_AUTH_TOKEN) {
    h.Authorization = BENCH_AUTH_TOKEN.startsWith('Bearer ')
      ? BENCH_AUTH_TOKEN
      : `Bearer ${BENCH_AUTH_TOKEN}`
  }
  return h
}

function norm(v) {
  return String(v || '').trim().toLowerCase().replace(/\s+/g, '')
}

function percentile(values, p) {
  if (!values.length) return 0
  const sorted = [...values].sort((a, b) => a - b)
  const idx = Math.ceil((p / 100) * sorted.length) - 1
  return sorted[Math.max(0, idx)]
}

function parseJson(v, fallback) {
  try {
    return JSON.parse(v)
  } catch {
    return fallback
  }
}

async function fetchJson(url, options = {}) {
  const res = await fetch(url, options)
  if (!res.ok) {
    throw new Error(`request failed: ${res.status} ${url}`)
  }
  return res.json()
}

async function listAllSessions() {
  const pageSize = 100
  const rows = []
  let page = 1

  while (true) {
    const data = await fetchJson(`${API_BASE_URL}/chat/sessions?page=${page}&pageSize=${pageSize}`, {
      headers: headers()
    })

    const batch = Array.isArray(data?.data) ? data.data : []
    rows.push(...batch)

    const total = Number(data?.total || 0)
    if (rows.length >= total || batch.length === 0 || rows.length >= MAX_SESSIONS) {
      break
    }

    page += 1
  }

  return rows.slice(0, MAX_SESSIONS)
}

function resolveActualTopTitle(body) {
  const direct = body?.topMatchedKbTitle
  if (typeof direct === 'string' && direct.trim()) return direct.trim()

  const firstRelated = Array.isArray(body?.relatedKBs) ? body.relatedKBs[0] : null
  const byRelated = firstRelated?.title || firstRelated?.Title
  if (typeof byRelated === 'string' && byRelated.trim()) return byRelated.trim()

  return null
}

function extractReplayCases(session) {
  const messages = Array.isArray(session?.messages) ? session.messages : []
  const rows = []

  for (let i = 0; i < messages.length; i += 1) {
    const user = messages[i]
    if (String(user?.role || '').toLowerCase() !== 'user') continue

    const question = String(user?.content || '').trim()
    if (!question) continue

    let bot = null
    for (let j = i + 1; j < messages.length; j += 1) {
      if (String(messages[j]?.role || '').toLowerCase() === 'bot') {
        bot = messages[j]
        break
      }
    }
    if (!bot) continue

    const meta = Array.isArray(bot?.relatedKbMeta)
      ? bot.relatedKbMeta
      : parseJson(bot?.relatedKbMeta || '[]', [])

    const selectedIds = Array.isArray(meta)
      ? meta
          .filter((x) => x && x.isSelected === true)
          .map((x) => Number(x.id || 0))
          .filter((x) => x > 0)
      : []

    rows.push({
      sessionId: session.id,
      question,
      role: session.userRole || 'user',
      platform: session.platform || '공통',
      expectedIsLowSimilarity: bot?.isLowSimilarity === true,
      expectedTopSimilarity: Number.isFinite(Number(bot?.topSimilarity)) ? Number(bot.topSimilarity) : null,
      expectedSelectedKbIds: selectedIds,
      expectedTopKbTitle: null
    })
  }

  return rows
}

async function buildReplayCases() {
  const sessions = await listAllSessions()
  const cases = []

  for (const s of sessions) {
    const detail = await fetchJson(`${API_BASE_URL}/chat/sessions/${s.id}`, {
      headers: headers()
    })

    const perSessionCases = extractReplayCases(detail)
    cases.push(...perSessionCases)
    if (cases.length >= MAX_CASES * 2) break
  }

  // 최신 로그 우선 중복 제거
  const dedup = new Map()
  for (const c of cases) {
    const key = `${norm(c.role)}|${norm(c.platform)}|${norm(c.question)}`
    if (!dedup.has(key)) dedup.set(key, c)
    if (dedup.size >= MAX_CASES) break
  }

  const baseCases = [...dedup.values()]
  const expandedCases = []
  for (let round = 1; round <= REPLAY_ROUNDS; round += 1) {
    baseCases.forEach((c, index) => {
      expandedCases.push({
        ...c,
        replayRound: round,
        replayCaseId: `${index + 1}-${round}`
      })
    })
  }

  return {
    sessionsCount: sessions.length,
    kbCount: null,
    baseCaseCount: baseCases.length,
    replayRounds: REPLAY_ROUNDS,
    cases: expandedCases
  }
}

async function askOnce(c) {
  const startedAt = Date.now()

  const res = await fetch(`${API_BASE_URL}/knowledgebase/ask`, {
    method: 'POST',
    headers: headers(),
    body: JSON.stringify({
      question: c.question,
      role: c.role,
      platform: c.platform,
      noSave: true,
      createSession: false,
      historyMessageCount: 0
    })
  })

  const elapsedMs = Date.now() - startedAt
  const text = await res.text()
  let body

  try {
    body = text ? JSON.parse(text) : {}
  } catch {
    body = { raw: text }
  }

  return {
    status: res.status,
    ok: res.ok,
    elapsedMs,
    body
  }
}

function evaluateReplayQuality(c, result) {
  if (!result.ok) return { success: false, reason: 'http' }

  const answer = String(result.body?.answer || '').trim()
  if (!answer) return { success: false, reason: 'empty_answer' }

  const actualLow = result.body?.isLowSimilarity === true
  if (c.expectedIsLowSimilarity) {
    return actualLow
      ? { success: true, reason: 'low_similarity_match' }
      : { success: false, reason: 'expected_low_but_not' }
  }

  if (actualLow) {
    return { success: false, reason: 'unexpected_low_similarity' }
  }

  const actualTitle = norm(resolveActualTopTitle(result.body))
  const expectedTitle = norm(c.expectedTopKbTitle)

  if (expectedTitle) {
    const titleMatched = actualTitle && (actualTitle.includes(expectedTitle) || expectedTitle.includes(actualTitle))
    if (titleMatched) return { success: true, reason: 'title_match' }

    const related = Array.isArray(result.body?.relatedKBs) ? result.body.relatedKBs : []
    const relatedIds = related
      .map((x) => Number(x?.id || 0))
      .filter((x) => x > 0)

    const idMatched = c.expectedSelectedKbIds.some((id) => relatedIds.includes(id))
    return idMatched
      ? { success: true, reason: 'id_match' }
      : { success: false, reason: 'kb_mismatch' }
  }

  return { success: true, reason: 'non_low_similarity' }
}

async function runSequential(cases) {
  const rows = []

  for (let i = 0; i < cases.length; i += 1) {
    const c = cases[i]
    let result

    try {
      result = await askOnce(c)
    } catch (err) {
      result = {
        status: 0,
        ok: false,
        elapsedMs: 0,
        body: { error: err.message }
      }
    }

    const quality = evaluateReplayQuality(c, result)
    const actualTopSimilarity = Number.isFinite(Number(result.body?.topSimilarity))
      ? Number(result.body.topSimilarity)
      : null

    const similarityDelta = (
      c.expectedTopSimilarity !== null &&
      actualTopSimilarity !== null
    )
      ? Math.abs(actualTopSimilarity - c.expectedTopSimilarity)
      : null

    rows.push({
      ...c,
      status: result.status,
      ok: result.ok,
      elapsedMs: result.elapsedMs,
      answerLength: String(result.body?.answer || '').trim().length,
      actualIsLowSimilarity: result.body?.isLowSimilarity === true,
      actualTopKbTitle: resolveActualTopTitle(result.body),
      actualTopSimilarity,
      similarityDelta,
      qualitySuccess: quality.success,
      qualityReason: quality.reason
    })

    console.log(`[live-bench][seq] ${i + 1}/${cases.length} status=${result.status} latency=${result.elapsedMs}ms quality=${quality.success} reason=${quality.reason}`)
  }

  return rows
}

async function runConcurrent(cases, concurrency) {
  const startedAt = Date.now()
  const rows = []
  let cursor = 0

  async function worker(workerId) {
    while (true) {
      const index = cursor
      cursor += 1
      if (index >= cases.length) return

      const c = cases[index]
      let result

      try {
        result = await askOnce(c)
      } catch (err) {
        result = {
          status: 0,
          ok: false,
          elapsedMs: 0,
          body: { error: err.message }
        }
      }

      rows.push({
        idx: index,
        workerId,
        status: result.status,
        ok: result.ok,
        elapsedMs: result.elapsedMs,
        answerLength: String(result.body?.answer || '').trim().length
      })
    }
  }

  await Promise.all(Array.from({ length: concurrency }, (_, i) => worker(i + 1)))

  const elapsedSec = (Date.now() - startedAt) / 1000
  return {
    rows,
    elapsedSec,
    throughputRps: elapsedSec > 0 ? rows.length / elapsedSec : 0
  }
}

function summarizeSequential(rows) {
  const total = rows.length
  const systemSuccessCount = rows.filter((r) => r.ok && r.answerLength > 0).length
  const qualitySuccessCount = rows.filter((r) => r.qualitySuccess).length
  const latencies = rows.filter((r) => r.ok).map((r) => r.elapsedMs)

  const deltas = rows
    .map((r) => r.similarityDelta)
    .filter((v) => Number.isFinite(v))

  const reasonMap = {}
  rows.forEach((r) => {
    reasonMap[r.qualityReason] = (reasonMap[r.qualityReason] || 0) + 1
  })

  return {
    total,
    systemSuccessRate: total ? systemSuccessCount / total : 0,
    qualityConsistencyRate: total ? qualitySuccessCount / total : 0,
    avgLatencyMs: latencies.length ? latencies.reduce((a, b) => a + b, 0) / latencies.length : 0,
    p50LatencyMs: percentile(latencies, 50),
    p95LatencyMs: percentile(latencies, 95),
    p99LatencyMs: percentile(latencies, 99),
    similarityDeltaAvg: deltas.length ? deltas.reduce((a, b) => a + b, 0) / deltas.length : 0,
    similarityDeltaP95: percentile(deltas, 95),
    qualityReasonBreakdown: reasonMap
  }
}

function summarizeConcurrent(run) {
  const total = run.rows.length
  const systemSuccessCount = run.rows.filter((r) => r.ok && r.answerLength > 0).length
  const latencies = run.rows.filter((r) => r.ok).map((r) => r.elapsedMs)

  return {
    total,
    concurrency: CONCURRENCY,
    elapsedSec: run.elapsedSec,
    throughputRps: run.throughputRps,
    systemSuccessRate: total ? systemSuccessCount / total : 0,
    avgLatencyMs: latencies.length ? latencies.reduce((a, b) => a + b, 0) / latencies.length : 0,
    p50LatencyMs: percentile(latencies, 50),
    p95LatencyMs: percentile(latencies, 95),
    p99LatencyMs: percentile(latencies, 99)
  }
}

function toMarkdown(meta, seqSummary, concurrentSummary, seqRows) {
  const failRows = seqRows
    .filter((r) => !r.qualitySuccess)
    .slice(0, 30)

  const lines = []
  lines.push('# 저장 질문 리플레이 심화 벤치 리포트')
  lines.push('')
  lines.push(`- 실행시각(UTC): ${new Date().toISOString()}`)
  lines.push(`- API_BASE_URL: ${API_BASE_URL}`)
  lines.push(`- 세션 수집 수: ${meta.sessionsCount}`)
  lines.push(`- KB 수: ${meta.kbCount == null ? 'N/A (권한 없는 모드)' : meta.kbCount}`)
  lines.push(`- 베이스 질문 수: ${meta.baseCaseCount}`)
  lines.push(`- 반복 라운드: ${meta.replayRounds}`)
  lines.push(`- 리플레이 총 케이스 수: ${meta.caseCount}`)
  lines.push(`- 동시성: ${CONCURRENCY}`)
  lines.push('')

  lines.push('## 1) 저장 질문 리플레이(순차)')
  lines.push('')
  lines.push('| 지표 | 값 |')
  lines.push('|---|---:|')
  lines.push(`| 시스템 성공률 | ${(seqSummary.systemSuccessRate * 100).toFixed(2)}% |`)
  lines.push(`| 품질 일관성률 | ${(seqSummary.qualityConsistencyRate * 100).toFixed(2)}% |`)
  lines.push(`| 평균 지연 | ${seqSummary.avgLatencyMs.toFixed(1)} ms |`)
  lines.push(`| P50 지연 | ${seqSummary.p50LatencyMs.toFixed(1)} ms |`)
  lines.push(`| P95 지연 | ${seqSummary.p95LatencyMs.toFixed(1)} ms |`)
  lines.push(`| P99 지연 | ${seqSummary.p99LatencyMs.toFixed(1)} ms |`)
  lines.push(`| TopSimilarity Δ 평균 | ${seqSummary.similarityDeltaAvg.toFixed(4)} |`)
  lines.push(`| TopSimilarity Δ P95 | ${seqSummary.similarityDeltaP95.toFixed(4)} |`)
  lines.push('')

  lines.push('### 실패 원인 분포')
  lines.push('')
  lines.push('| 원인 | 건수 |')
  lines.push('|---|---:|')
  Object.entries(seqSummary.qualityReasonBreakdown)
    .sort((a, b) => b[1] - a[1])
    .forEach(([reason, count]) => {
      lines.push(`| ${reason} | ${count} |`)
    })
  lines.push('')

  lines.push('## 2) 저장 질문 리플레이(동시 부하)')
  lines.push('')
  lines.push('| 지표 | 값 |')
  lines.push('|---|---:|')
  lines.push(`| 시스템 성공률 | ${(concurrentSummary.systemSuccessRate * 100).toFixed(2)}% |`)
  lines.push(`| 평균 지연 | ${concurrentSummary.avgLatencyMs.toFixed(1)} ms |`)
  lines.push(`| P50 지연 | ${concurrentSummary.p50LatencyMs.toFixed(1)} ms |`)
  lines.push(`| P95 지연 | ${concurrentSummary.p95LatencyMs.toFixed(1)} ms |`)
  lines.push(`| P99 지연 | ${concurrentSummary.p99LatencyMs.toFixed(1)} ms |`)
  lines.push(`| Throughput | ${concurrentSummary.throughputRps.toFixed(2)} req/s |`)
  lines.push(`| 총 소요 | ${concurrentSummary.elapsedSec.toFixed(2)} s |`)
  lines.push('')

  lines.push('## 3) 품질 불일치 상위 30건')
  lines.push('')
  lines.push('| sessionId | role | platform | 질문 | 기대 low | 실제 low | 기대 KB | 실제 KB | 사유 |')
  lines.push('|---:|---|---|---|---|---|---|---|---|')
  failRows.forEach((r) => {
    lines.push(`| ${r.sessionId} | ${r.role} | ${r.platform} | ${String(r.question).replace(/\|/g, ' ')} | ${r.expectedIsLowSimilarity} | ${r.actualIsLowSimilarity} | ${String(r.expectedTopKbTitle || '-').replace(/\|/g, ' ')} | ${String(r.actualTopKbTitle || '-').replace(/\|/g, ' ')} | ${r.qualityReason} |`)
  })

  lines.push('')
  return lines.join('\n')
}

async function main() {
  console.log(`[live-bench] API_BASE_URL=${API_BASE_URL}`)
  console.log(`[live-bench] collecting sessions (max=${MAX_SESSIONS}) and replay cases (max=${MAX_CASES})`)

  const built = await buildReplayCases()
  const cases = built.cases

  if (!cases.length) {
    throw new Error('replay cases are empty; stored chat questions not found')
  }

  console.log(`[live-bench] sessions=${built.sessionsCount}, baseCases=${built.baseCaseCount}, rounds=${built.replayRounds}, replayCases=${cases.length}`)

  const sequentialRows = await runSequential(cases)
  const seqSummary = summarizeSequential(sequentialRows)

  console.log('[live-bench] running concurrent load pass')
  const concurrentRun = await runConcurrent(cases, CONCURRENCY)
  const concurrentSummary = summarizeConcurrent(concurrentRun)

  const meta = {
    sessionsCount: built.sessionsCount,
    kbCount: built.kbCount,
    baseCaseCount: built.baseCaseCount,
    replayRounds: built.replayRounds,
    caseCount: cases.length
  }

  const output = {
    meta,
    sequentialSummary: seqSummary,
    concurrentSummary,
    sequentialRows,
    concurrentRows: concurrentRun.rows
  }

  fs.mkdirSync(OUTPUT_DIR, { recursive: true })
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
  const jsonPath = path.join(OUTPUT_DIR, `benchmark-live-questions-${timestamp}.json`)
  const mdPath = path.join(OUTPUT_DIR, `benchmark-live-questions-${timestamp}.md`)
  const latestJsonPath = path.join(OUTPUT_DIR, 'benchmark-live-questions-latest.json')
  const latestMdPath = path.join(OUTPUT_DIR, 'benchmark-live-questions-latest.md')

  fs.writeFileSync(jsonPath, JSON.stringify(output, null, 2), 'utf8')
  fs.writeFileSync(latestJsonPath, JSON.stringify(output, null, 2), 'utf8')

  const markdown = toMarkdown(meta, seqSummary, concurrentSummary, sequentialRows)
  fs.writeFileSync(mdPath, markdown, 'utf8')
  fs.writeFileSync(latestMdPath, markdown, 'utf8')

  console.log('\n[live-bench] done')
  console.log(`- sequential system success: ${(seqSummary.systemSuccessRate * 100).toFixed(2)}%`)
  console.log(`- sequential consistency: ${(seqSummary.qualityConsistencyRate * 100).toFixed(2)}%`)
  console.log(`- sequential latency avg/p95/p99: ${seqSummary.avgLatencyMs.toFixed(1)} / ${seqSummary.p95LatencyMs.toFixed(1)} / ${seqSummary.p99LatencyMs.toFixed(1)} ms`)
  console.log(`- concurrent throughput: ${concurrentSummary.throughputRps.toFixed(2)} req/s`)
  console.log(`- report: ${latestMdPath}`)
}

main().catch((err) => {
  console.error('[live-bench] fatal', err)
  process.exit(1)
})

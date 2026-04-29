#!/usr/bin/env node

const fs = require('fs')
const path = require('path')
const { kbCatalog, negativeQuestions } = require('./demo-kb-catalog')

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:8080/api'
const OUTPUT_DIR = path.resolve(process.cwd(), 'reports')

function normalizeText(value) {
  return String(value || '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, '')
}

function percentile(values, p) {
  if (values.length === 0) return 0
  const sorted = [...values].sort((a, b) => a - b)
  const idx = Math.ceil((p / 100) * sorted.length) - 1
  return sorted[Math.max(0, idx)]
}

function buildCases() {
  const positive = []

  kbCatalog.forEach((kb, index) => {
    const q = kb.expectedQuestions
    positive.push(
      {
        id: `P-${index + 1}-1`,
        type: 'positive',
        question: q[0] || `${kb.title} 문의`,
        expectedTitle: kb.title,
        expectLowSimilarity: false
      },
      {
        id: `P-${index + 1}-2`,
        type: 'positive',
        question: q[1] || `${kb.title} 해결 방법`,
        expectedTitle: kb.title,
        expectLowSimilarity: false
      },
      {
        id: `P-${index + 1}-3`,
        type: 'positive',
        question: `${kb.title} ${q[2] || '처리 절차 알려줘'}`,
        expectedTitle: kb.title,
        expectLowSimilarity: false
      }
    )
  })

  const negatives = negativeQuestions.map((question, idx) => ({
    id: `N-${idx + 1}`,
    type: 'negative',
    question,
    expectedTitle: null,
    expectLowSimilarity: true
  }))

  const all = positive.concat(negatives).slice(0, 100)
  if (all.length !== 100) {
    throw new Error(`benchmark case count must be 100, got ${all.length}`)
  }

  return all
}

async function ask(question) {
  const startedAt = Date.now()

  const response = await fetch(`${API_BASE_URL}/knowledgebase/ask`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      question,
      role: 'user',
      platform: '공통',
      noSave: true,
      createSession: false
    })
  })

  const elapsedMs = Date.now() - startedAt
  const text = await response.text()
  let body = null

  try {
    body = text ? JSON.parse(text) : {}
  } catch {
    body = { raw: text }
  }

  return {
    status: response.status,
    ok: response.ok,
    elapsedMs,
    body
  }
}

function evaluateQuality(testCase, result) {
  if (!result.ok) return false

  const answer = String(result.body?.answer || '').trim()
  if (!answer) return false

  const isLowSimilarity = result.body?.isLowSimilarity === true

  if (testCase.expectLowSimilarity) {
    return isLowSimilarity
  }

  if (isLowSimilarity) {
    return false
  }

  const topTitle = normalizeText(result.body?.topMatchedKbTitle)
  const expected = normalizeText(testCase.expectedTitle)
  return topTitle.includes(expected) || expected.includes(topTitle)
}

function buildMarkdownReport(summary, rows) {
  const lines = []
  lines.push('# 100문항 자동 벤치 리포트')
  lines.push('')
  lines.push(`- 실행시각(UTC): ${new Date().toISOString()}`)
  lines.push(`- API_BASE_URL: ${API_BASE_URL}`)
  lines.push(`- 총 문항: ${summary.total}`)
  lines.push('')
  lines.push('## 요약')
  lines.push('')
  lines.push('| 지표 | 값 |')
  lines.push('|---|---:|')
  lines.push(`| 시스템 성공률 | ${(summary.systemSuccessRate * 100).toFixed(2)}% |`)
  lines.push(`| 품질 성공률 | ${(summary.qualitySuccessRate * 100).toFixed(2)}% |`)
  lines.push(`| Positive 품질 성공률 | ${(summary.positiveQualityRate * 100).toFixed(2)}% |`)
  lines.push(`| Negative 품질 성공률 | ${(summary.negativeQualityRate * 100).toFixed(2)}% |`)
  lines.push(`| 평균 응답시간 | ${summary.avgLatencyMs.toFixed(1)} ms |`)
  lines.push(`| P50 응답시간 | ${summary.p50LatencyMs.toFixed(1)} ms |`)
  lines.push(`| P95 응답시간 | ${summary.p95LatencyMs.toFixed(1)} ms |`)
  lines.push('')
  lines.push('## 실패 케이스 상위 20개')
  lines.push('')
  lines.push('| ID | 유형 | 질문 | 상태 | 저유사도 | 매칭제목 | 품질통과 |')
  lines.push('|---|---|---|---:|---|---|---|')

  rows
    .filter((r) => !r.qualitySuccess)
    .slice(0, 20)
    .forEach((r) => {
      lines.push(`| ${r.id} | ${r.type} | ${r.question.replace(/\|/g, ' ')} | ${r.status} | ${r.isLowSimilarity} | ${String(r.topMatchedKbTitle || '-').replace(/\|/g, ' ')} | ${r.qualitySuccess} |`)
    })

  lines.push('')
  return lines.join('\n')
}

async function main() {
  const cases = buildCases()
  console.log(`[bench] API_BASE_URL=${API_BASE_URL}`)
  console.log(`[bench] total cases=${cases.length}`)

  const rows = []

  for (const c of cases) {
    let result
    try {
      result = await ask(c.question)
    } catch (err) {
      result = {
        status: 0,
        ok: false,
        elapsedMs: 0,
        body: { error: err.message }
      }
    }

    const answer = String(result.body?.answer || '').trim()
    const isLowSimilarity = result.body?.isLowSimilarity === true
    const systemSuccess = result.ok && answer.length > 0
    const qualitySuccess = evaluateQuality(c, result)

    rows.push({
      id: c.id,
      type: c.type,
      question: c.question,
      expectedTitle: c.expectedTitle,
      status: result.status,
      systemSuccess,
      qualitySuccess,
      elapsedMs: result.elapsedMs,
      isLowSimilarity,
      topMatchedKbTitle: result.body?.topMatchedKbTitle || null,
      topSimilarity: result.body?.topSimilarity ?? null
    })

    console.log(`[bench] ${c.id} status=${result.status} latency=${result.elapsedMs}ms system=${systemSuccess} quality=${qualitySuccess}`)
  }

  const total = rows.length
  const systemSuccessCount = rows.filter((r) => r.systemSuccess).length
  const qualitySuccessCount = rows.filter((r) => r.qualitySuccess).length

  const positive = rows.filter((r) => r.type === 'positive')
  const negative = rows.filter((r) => r.type === 'negative')

  const latencies = rows.filter((r) => r.systemSuccess).map((r) => r.elapsedMs)
  const avgLatencyMs = latencies.length ? latencies.reduce((a, b) => a + b, 0) / latencies.length : 0

  const summary = {
    total,
    systemSuccessCount,
    qualitySuccessCount,
    systemSuccessRate: systemSuccessCount / total,
    qualitySuccessRate: qualitySuccessCount / total,
    positiveQualityRate: positive.filter((r) => r.qualitySuccess).length / positive.length,
    negativeQualityRate: negative.filter((r) => r.qualitySuccess).length / negative.length,
    avgLatencyMs,
    p50LatencyMs: percentile(latencies, 50),
    p95LatencyMs: percentile(latencies, 95)
  }

  fs.mkdirSync(OUTPUT_DIR, { recursive: true })
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
  const jsonPath = path.join(OUTPUT_DIR, `benchmark-100-${timestamp}.json`)
  const mdPath = path.join(OUTPUT_DIR, `benchmark-100-${timestamp}.md`)
  const latestJsonPath = path.join(OUTPUT_DIR, 'benchmark-100-latest.json')
  const latestMdPath = path.join(OUTPUT_DIR, 'benchmark-100-latest.md')

  fs.writeFileSync(jsonPath, JSON.stringify({ summary, rows }, null, 2), 'utf8')
  fs.writeFileSync(latestJsonPath, JSON.stringify({ summary, rows }, null, 2), 'utf8')

  const report = buildMarkdownReport(summary, rows)
  fs.writeFileSync(mdPath, report, 'utf8')
  fs.writeFileSync(latestMdPath, report, 'utf8')

  console.log('\n[bench] summary')
  console.log(`- system success: ${(summary.systemSuccessRate * 100).toFixed(2)}% (${summary.systemSuccessCount}/${summary.total})`)
  console.log(`- quality success: ${(summary.qualitySuccessRate * 100).toFixed(2)}% (${summary.qualitySuccessCount}/${summary.total})`)
  console.log(`- positive quality: ${(summary.positiveQualityRate * 100).toFixed(2)}%`)
  console.log(`- negative quality: ${(summary.negativeQualityRate * 100).toFixed(2)}%`)
  console.log(`- latency avg/p50/p95: ${summary.avgLatencyMs.toFixed(1)} / ${summary.p50LatencyMs.toFixed(1)} / ${summary.p95LatencyMs.toFixed(1)} ms`)
  console.log(`- report: ${latestMdPath}`)
  console.log(`- raw: ${latestJsonPath}`)

  if (summary.systemSuccessRate < 0.995 || summary.qualitySuccessRate < 0.90) {
    process.exitCode = 2
  }
}

main().catch((err) => {
  console.error('[bench] fatal', err)
  process.exit(1)
})

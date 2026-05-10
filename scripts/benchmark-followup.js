#!/usr/bin/env node
/**
 * 대화이력기반 질문정제 효과 측정 벤치마크
 *
 * 목적:
 *  - 후속 질문을 대화 이력 없이 (노정제) vs 대화 이력 있이 (LLM 정제) 보내서
 *    정확도와 응답 품질 차이를 측정
 *
 * 케이스 구조:
 *  각 시나리오는 [첫 질문, 봇 응답, 후속 질문] 형태의 3턴 대화
 *  후속 질문을 a) 이력 없이, b) 이력 포함으로 각각 요청해 비교
 */

const fs = require('fs')
const path = require('path')

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:8080/api'
const BENCH_AUTH_TOKEN = process.env.BENCH_AUTH_TOKEN || ''
const OUTPUT_DIR = path.resolve(process.cwd(), 'reports')

function buildHeaders() {
  const headers = { 'Content-Type': 'application/json' }
  if (BENCH_AUTH_TOKEN) {
    headers.Authorization = BENCH_AUTH_TOKEN.startsWith('Bearer ')
      ? BENCH_AUTH_TOKEN
      : `Bearer ${BENCH_AUTH_TOKEN}`
  }
  return headers
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

// ---------------------------------------------------------
// 후속질문 시나리오 정의
// 형식: { id, expectedKbTitle, firstTurn: { q, a }, followUpQuestion }
// ---------------------------------------------------------
const SCENARIOS = [
  // 1. 인증서 관련 후속질문
  {
    id: 'FU-01',
    expectedKbTitle: '인증서 선택 후 화면 멈춤',
    firstTurn: {
      q: '인증서가 뭔가요?',
      a: '인증서는 전자서명에 사용되는 디지털 보안 증명서입니다.'
    },
    followUpQuestion: '그럼 선택하고 나서 화면이 멈추면 어떻게 해요?'
  },
  // 2. 계정 잠금 후속
  {
    id: 'FU-02',
    expectedKbTitle: '로그인 실패 시 계정 잠금 해제',
    firstTurn: {
      q: '로그인이 안 돼요',
      a: '로그인 실패 원인은 여러 가지가 있습니다. 비밀번호 오류, 계정 잠금 등이 있습니다.'
    },
    followUpQuestion: '계속 틀렸더니 잠겼는데'
  },
  // 3. 비밀번호 재설정 후속
  {
    id: 'FU-03',
    expectedKbTitle: '비밀번호 재설정 메일 미수신',
    firstTurn: {
      q: '비밀번호를 잊어버렸어요',
      a: '비밀번호 재설정을 위해 등록된 이메일로 재설정 링크를 발송할 수 있습니다.'
    },
    followUpQuestion: '근데 메일이 안 와요'
  },
  // 4. OTP 후속
  {
    id: 'FU-04',
    expectedKbTitle: '2차 인증 코드 불일치',
    firstTurn: {
      q: '2차 인증이 뭔가요?',
      a: '2차 인증은 보안을 강화하기 위해 비밀번호 외에 OTP 코드를 추가로 입력하는 방식입니다.'
    },
    followUpQuestion: '그 코드가 자꾸 틀린대요'
  },
  // 5. 결제 후속
  {
    id: 'FU-05',
    expectedKbTitle: '결제 승인 지연',
    firstTurn: {
      q: '결제가 이상해요',
      a: '결제 이상 증상은 카드 오류, 잔액 부족, 서버 지연 등 다양한 원인이 있을 수 있습니다.'
    },
    followUpQuestion: '승인이 안 되고 계속 기다리는 상태에요'
  },
  // 6. 환불 후속
  {
    id: 'FU-06',
    expectedKbTitle: '중복 결제 환불 처리',
    firstTurn: {
      q: '결제가 두 번 됐어요',
      a: '결제가 중복으로 처리된 경우 환불 절차를 진행할 수 있습니다.'
    },
    followUpQuestion: '그럼 환불은 어떻게 받아요?'
  },
  // 7. 파일 업로드 후속
  {
    id: 'FU-07',
    expectedKbTitle: '파일 업로드 용량 초과',
    firstTurn: {
      q: '파일을 첨부하려는데 안 돼요',
      a: '파일 첨부가 안 되는 경우 용량 초과, 확장자 제한 등이 원인일 수 있습니다.'
    },
    followUpQuestion: '용량 때문인 것 같은데 어떻게 해요?'
  },
  // 8. 권한 후속
  {
    id: 'FU-08',
    expectedKbTitle: '권한 없음 오류 해결',
    firstTurn: {
      q: '메뉴에 접근이 안 돼요',
      a: '메뉴 접근이 제한되는 경우 권한 설정 또는 세션 만료가 원인일 수 있습니다.'
    },
    followUpQuestion: '권한이 없다고 나오는데요'
  },
  // 9. 세션 만료 후속
  {
    id: 'FU-09',
    expectedKbTitle: '세션 만료로 자동 로그아웃',
    firstTurn: {
      q: '갑자기 로그아웃됐어요',
      a: '갑자기 로그아웃되는 경우 세션 만료 또는 중복 로그인 차단이 원인일 수 있습니다.'
    },
    followUpQuestion: '세션이 만료됐다고 나오는 건가요?'
  },
  // 10. 검색 후속
  {
    id: 'FU-10',
    expectedKbTitle: '검색 결과 0건 원인 점검',
    firstTurn: {
      q: '검색해도 아무것도 안 나와요',
      a: '검색 결과가 없는 경우 검색어, 필터, 데이터 범위 등을 확인해볼 수 있습니다.'
    },
    followUpQuestion: '결과가 0건인데 왜 그런 거예요?'
  }
]

async function askWithoutHistory(question) {
  const startedAt = Date.now()
  const response = await fetch(`${API_BASE_URL}/knowledgebase/ask`, {
    method: 'POST',
    headers: buildHeaders(),
    body: JSON.stringify({
      question,
      role: 'user',
      platform: '공통',
      noSave: true,
      createSession: false
    })
  })
  const elapsedMs = Date.now() - startedAt
  const body = await response.json().catch(() => ({}))
  return { status: response.status, ok: response.ok, elapsedMs, body }
}

async function askWithHistory(question, history) {
  const startedAt = Date.now()
  const response = await fetch(`${API_BASE_URL}/knowledgebase/ask`, {
    method: 'POST',
    headers: buildHeaders(),
    body: JSON.stringify({
      question,
      role: 'user',
      platform: '공통',
      noSave: true,
      createSession: false,
      history
    })
  })
  const elapsedMs = Date.now() - startedAt
  const body = await response.json().catch(() => ({}))
  return { status: response.status, ok: response.ok, elapsedMs, body }
}

function resolveTopTitle(body) {
  const direct = body?.topMatchedKbTitle
  if (typeof direct === 'string' && direct.trim()) return direct.trim()
  const first = Array.isArray(body?.relatedKBs) ? body.relatedKBs[0] : null
  return first?.title || first?.Title || null
}

function isMatch(topTitle, expectedTitle) {
  if (!topTitle || !expectedTitle) return false
  const a = norm(topTitle)
  const b = norm(expectedTitle)
  return a.includes(b) || b.includes(a)
}

async function runScenario(scenario) {
  const { id, expectedKbTitle, firstTurn, followUpQuestion } = scenario

  // A. 이력 없이 후속질문 (기존 방식)
  const resultNoHistory = await askWithoutHistory(followUpQuestion)

  // B. 이력 포함 후속질문 (LLM 정제 방식)
  const history = [
    { role: 'user', content: firstTurn.q },
    { role: 'bot', content: firstTurn.a }
  ]
  const resultWithHistory = await askWithHistory(followUpQuestion, history)

  const noHistTitle = resolveTopTitle(resultNoHistory.body)
  const withHistTitle = resolveTopTitle(resultWithHistory.body)

  const noHistMatch = isMatch(noHistTitle, expectedKbTitle)
  const withHistMatch = isMatch(withHistTitle, expectedKbTitle)

  return {
    id,
    followUpQuestion,
    expectedKbTitle,
    noHistory: {
      ok: resultNoHistory.ok,
      topTitle: noHistTitle,
      topSimilarity: resultNoHistory.body?.topSimilarity ?? null,
      isLowSimilarity: resultNoHistory.body?.isLowSimilarity === true,
      latencyMs: resultNoHistory.elapsedMs,
      matched: noHistMatch
    },
    withHistory: {
      ok: resultWithHistory.ok,
      topTitle: withHistTitle,
      topSimilarity: resultWithHistory.body?.topSimilarity ?? null,
      isLowSimilarity: resultWithHistory.body?.isLowSimilarity === true,
      latencyMs: resultWithHistory.elapsedMs,
      matched: withHistMatch
    }
  }
}

function buildReport(results) {
  const lines = []
  lines.push('# 대화이력기반 질문정제 효과 측정 벤치마크')
  lines.push('')
  lines.push(`- 실행시각(UTC): ${new Date().toISOString()}`)
  lines.push(`- API_BASE_URL: ${API_BASE_URL}`)
  lines.push(`- 총 시나리오: ${results.length}`)
  lines.push('')

  const noHistMatched = results.filter(r => r.noHistory.matched).length
  const withHistMatched = results.filter(r => r.withHistory.matched).length
  const improved = results.filter(r => !r.noHistory.matched && r.withHistory.matched).length
  const degraded = results.filter(r => r.noHistory.matched && !r.withHistory.matched).length
  const same = results.filter(r => r.noHistory.matched === r.withHistory.matched).length

  const noHistRate = (noHistMatched / results.length * 100).toFixed(1)
  const withHistRate = (withHistMatched / results.length * 100).toFixed(1)
  const diffPct = ((withHistMatched - noHistMatched) / results.length * 100).toFixed(1)

  const noHistAvgSim = results.map(r => r.noHistory.topSimilarity || 0).reduce((a, b) => a + b, 0) / results.length
  const withHistAvgSim = results.map(r => r.withHistory.topSimilarity || 0).reduce((a, b) => a + b, 0) / results.length

  const noHistAvgLatency = results.map(r => r.noHistory.latencyMs).reduce((a, b) => a + b, 0) / results.length
  const withHistAvgLatency = results.map(r => r.withHistory.latencyMs).reduce((a, b) => a + b, 0) / results.length

  lines.push('## 핵심 요약')
  lines.push('')
  lines.push('| 지표 | 이력 없음 (기존) | 이력 포함 (정제) | 변화 |')
  lines.push('|------|----:|----:|----:|')
  lines.push(`| 정확도 (매칭 성공) | ${noHistRate}% (${noHistMatched}/${results.length}) | ${withHistRate}% (${withHistMatched}/${results.length}) | **${diffPct >= 0 ? '+' : ''}${diffPct}%** |`)
  lines.push(`| 평균 유사도 | ${noHistAvgSim.toFixed(3)} | ${withHistAvgSim.toFixed(3)} | ${(withHistAvgSim - noHistAvgSim).toFixed(3)} |`)
  lines.push(`| 평균 응답시간 | ${noHistAvgLatency.toFixed(0)}ms | ${withHistAvgLatency.toFixed(0)}ms | ${(withHistAvgLatency - noHistAvgLatency).toFixed(0)}ms |`)
  lines.push(`| 개선된 케이스 | - | - | +${improved}개 |`)
  lines.push(`| 저하된 케이스 | - | - | -${degraded}개 |`)
  lines.push(`| 동일한 케이스 | - | - | ${same}개 |`)
  lines.push('')

  lines.push('## 케이스별 결과')
  lines.push('')
  lines.push('| ID | 후속 질문 | 기대 KB | 이력없음 매칭 | 유사도 | 이력포함 매칭 | 유사도 | 결과 |')
  lines.push('|---|---|---|:---:|---:|:---:|---:|---|')

  for (const r of results) {
    const noHistIcon = r.noHistory.matched ? '✅' : '❌'
    const withHistIcon = r.withHistory.matched ? '✅' : '❌'
    const change = !r.noHistory.matched && r.withHistory.matched ? '⬆️ 개선'
      : r.noHistory.matched && !r.withHistory.matched ? '⬇️ 저하'
      : '➡️ 동일'
    const noSimStr = r.noHistory.topSimilarity !== null ? r.noHistory.topSimilarity.toFixed(3) : '-'
    const withSimStr = r.withHistory.topSimilarity !== null ? r.withHistory.topSimilarity.toFixed(3) : '-'

    lines.push(`| ${r.id} | ${r.followUpQuestion.replace(/\|/g, ' ')} | ${r.expectedKbTitle.replace(/\|/g, ' ')} | ${noHistIcon} | ${noSimStr} | ${withHistIcon} | ${withSimStr} | ${change} |`)
  }

  lines.push('')
  lines.push('## 개선된 케이스 상세')
  lines.push('')

  const improvedCases = results.filter(r => !r.noHistory.matched && r.withHistory.matched)
  if (improvedCases.length === 0) {
    lines.push('없음')
  } else {
    for (const r of improvedCases) {
      lines.push(`### ${r.id}: ${r.followUpQuestion}`)
      lines.push(`- 기대 KB: ${r.expectedKbTitle}`)
      lines.push(`- 이력 없음 → 매칭된 KB: ${r.noHistory.topTitle || '(없음)'} (유사도: ${r.noHistory.topSimilarity?.toFixed(3) || '-'})`)
      lines.push(`- 이력 포함 → 매칭된 KB: ${r.withHistory.topTitle || '(없음)'} (유사도: ${r.withHistory.topSimilarity?.toFixed(3) || '-'})`)
      lines.push('')
    }
  }

  lines.push('## 저하된 케이스 상세')
  lines.push('')

  const degradedCases = results.filter(r => r.noHistory.matched && !r.withHistory.matched)
  if (degradedCases.length === 0) {
    lines.push('없음')
  } else {
    for (const r of degradedCases) {
      lines.push(`### ${r.id}: ${r.followUpQuestion}`)
      lines.push(`- 기대 KB: ${r.expectedKbTitle}`)
      lines.push(`- 이력 없음 → 매칭된 KB: ${r.noHistory.topTitle || '(없음)'} (유사도: ${r.noHistory.topSimilarity?.toFixed(3) || '-'})`)
      lines.push(`- 이력 포함 → 매칭된 KB: ${r.withHistory.topTitle || '(없음)'} (유사도: ${r.withHistory.topSimilarity?.toFixed(3) || '-'})`)
      lines.push('')
    }
  }

  return lines.join('\n')
}

async function main() {
  console.log(`[followup-bench] API_BASE_URL=${API_BASE_URL}`)
  console.log(`[followup-bench] 총 시나리오: ${SCENARIOS.length}`)
  console.log(`[followup-bench] 방식: A(이력없음) vs B(이력포함 LLM정제)`)
  console.log('')

  const results = []

  for (const scenario of SCENARIOS) {
    process.stdout.write(`[followup-bench] ${scenario.id} 실행 중... `)
    try {
      const result = await runScenario(scenario)
      results.push(result)
      const noH = result.noHistory.matched ? '✅' : '❌'
      const wiH = result.withHistory.matched ? '✅' : '❌'
      const change = !result.noHistory.matched && result.withHistory.matched ? '⬆️'
        : result.noHistory.matched && !result.withHistory.matched ? '⬇️'
        : '➡️'
      console.log(`이력없음:${noH}(${result.noHistory.topSimilarity?.toFixed(3) || '-'}) → 이력포함:${wiH}(${result.withHistory.topSimilarity?.toFixed(3) || '-'}) ${change}`)
    } catch (err) {
      console.log(`오류: ${err.message}`)
      results.push({
        id: scenario.id,
        followUpQuestion: scenario.followUpQuestion,
        expectedKbTitle: scenario.expectedKbTitle,
        noHistory: { ok: false, topTitle: null, topSimilarity: null, isLowSimilarity: false, latencyMs: 0, matched: false },
        withHistory: { ok: false, topTitle: null, topSimilarity: null, isLowSimilarity: false, latencyMs: 0, matched: false }
      })
    }
  }

  const noHistMatched = results.filter(r => r.noHistory.matched).length
  const withHistMatched = results.filter(r => r.withHistory.matched).length
  const improved = results.filter(r => !r.noHistory.matched && r.withHistory.matched).length
  const degraded = results.filter(r => r.noHistory.matched && !r.withHistory.matched).length

  console.log('')
  console.log('[followup-bench] ========== 결과 요약 ==========')
  console.log(`[followup-bench] 이력 없음 (기존): ${(noHistMatched / results.length * 100).toFixed(1)}% (${noHistMatched}/${results.length})`)
  console.log(`[followup-bench] 이력 포함 (정제): ${(withHistMatched / results.length * 100).toFixed(1)}% (${withHistMatched}/${results.length})`)
  console.log(`[followup-bench] 개선된 케이스: +${improved}개`)
  console.log(`[followup-bench] 저하된 케이스: -${degraded}개`)
  console.log('[followup-bench] ====================================')

  fs.mkdirSync(OUTPUT_DIR, { recursive: true })
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
  const jsonPath = path.join(OUTPUT_DIR, `benchmark-followup-${timestamp}.json`)
  const mdPath = path.join(OUTPUT_DIR, `benchmark-followup-${timestamp}.md`)
  const latestMdPath = path.join(OUTPUT_DIR, 'benchmark-followup-latest.md')
  const latestJsonPath = path.join(OUTPUT_DIR, 'benchmark-followup-latest.json')

  fs.writeFileSync(jsonPath, JSON.stringify(results, null, 2), 'utf8')
  fs.writeFileSync(latestJsonPath, JSON.stringify(results, null, 2), 'utf8')

  const report = buildReport(results)
  fs.writeFileSync(mdPath, report, 'utf8')
  fs.writeFileSync(latestMdPath, report, 'utf8')

  console.log(`[followup-bench] report: ${latestMdPath}`)
}

main().catch(err => {
  console.error('[followup-bench] fatal', err)
  process.exit(1)
})

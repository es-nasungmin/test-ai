# 벤치마크 실행 가이드

이 문서는 현재 저장소의 벤치마크 스크립트 2종을 기준으로 작성되었습니다.

1. `scripts/benchmark-100.js`
- 카탈로그/실시간 KB를 사용한 100문항 품질 벤치

2. `scripts/benchmark-live-questions.js`
- 저장된 실제 질문을 재실행하는 리플레이 벤치(품질 일관성 + 지연/부하)

## 1. 100문항 벤치 (`benchmark-100.js`)

### 케이스 구성
- 기본: Positive 93 + Negative 7 = 총 100
- `BENCH_CASESET=demo` (기본): `scripts/demo-kb-catalog.js` 기준
- `BENCH_CASESET=live`: 현재 DB의 `visibility=user` KB 기준

### 실행

```bash
# 데모 카탈로그 기준
node scripts/benchmark-100.js

# 실시간 KB 기준
BENCH_CASESET=live node scripts/benchmark-100.js
```

### 합격 기준(스크립트 내부)
- 시스템 성공률 >= 99.5%
- 품질 성공률 >= 90.0%
- 미달 시 종료 코드 2

## 2. 저장 질문 리플레이 벤치 (`benchmark-live-questions.js`)

저장된 채팅 질문을 모아 재질의하고, 기존 결과와의 일관성을 측정합니다.

### 핵심 지표
- 순차 리플레이
- 시스템 성공률
- 품질 일관성률
- 평균/P50/P95/P99 지연
- TopSimilarity 차이(Delta)

- 동시 부하 리플레이
- 시스템 성공률
- 평균/P50/P95/P99 지연
- Throughput(req/s)

### 실행

```bash
# 기본 실행
node scripts/benchmark-live-questions.js

# 심화 실행(반복 샘플 확대)
BENCH_REPLAY_ROUNDS=20 BENCH_CONCURRENCY=8 node scripts/benchmark-live-questions.js
```

환경 변수:
- `BENCH_MAX_SESSIONS` (기본 300)
- `BENCH_MAX_CASES` (기본 300)
- `BENCH_REPLAY_ROUNDS` (기본 20)
- `BENCH_CONCURRENCY` (기본 8)
- `API_BASE_URL` (기본 `http://localhost:8080/api`)
- `BENCH_AUTH_TOKEN` (관리자 API 필요 시)

## 3. 산출물

`reports/`에 아래 파일이 생성됩니다.

- 100문항 벤치
- `benchmark-100-latest.json`
- `benchmark-100-latest.md`

- 저장 질문 리플레이
- `benchmark-live-questions-latest.json`
- `benchmark-live-questions-latest.md`

각 실행 시 타임스탬프 버전도 함께 생성됩니다.

## 4. 운영 게이트 권장

배포 직전 최소 2개 벤치를 모두 통과시키는 것을 권장합니다.

1. `BENCH_CASESET=live node scripts/benchmark-100.js`
2. `BENCH_REPLAY_ROUNDS=20 BENCH_CONCURRENCY=8 node scripts/benchmark-live-questions.js`
3. 두 리포트의 실패 케이스를 확인한 후 릴리스 판단

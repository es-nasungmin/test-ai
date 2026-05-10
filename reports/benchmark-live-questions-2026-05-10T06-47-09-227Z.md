# 저장 질문 리플레이 심화 벤치 리포트

- 실행시각(UTC): 2026-05-10T06:47:09.230Z
- API_BASE_URL: http://localhost:8080/api
- 세션 수집 수: 3
- KB 수: N/A (권한 없는 모드)
- 리플레이 케이스 수: 4
- 동시성: 8

## 1) 저장 질문 리플레이(순차)

| 지표 | 값 |
|---|---:|
| 시스템 성공률 | 100.00% |
| 품질 일관성률 | 75.00% |
| 평균 지연 | 2023.0 ms |
| P50 지연 | 926.0 ms |
| P95 지연 | 4329.0 ms |
| P99 지연 | 4329.0 ms |
| TopSimilarity Δ 평균 | 0.2881 |
| TopSimilarity Δ P95 | 0.7895 |

### 실패 원인 분포

| 원인 | 건수 |
|---|---:|
| low_similarity_match | 2 |
| non_low_similarity | 1 |
| unexpected_low_similarity | 1 |

## 2) 저장 질문 리플레이(동시 부하)

| 지표 | 값 |
|---|---:|
| 시스템 성공률 | 100.00% |
| 평균 지연 | 1698.5 ms |
| P50 지연 | 1160.0 ms |
| P95 지연 | 4013.0 ms |
| P99 지연 | 4013.0 ms |
| Throughput | 1.00 req/s |
| 총 소요 | 4.01 s |

## 3) 품질 불일치 상위 30건

| sessionId | role | platform | 질문 | 기대 low | 실제 low | 기대 KB | 실제 KB | 사유 |
|---:|---|---|---|---|---|---|---|---|
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |

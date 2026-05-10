# 저장 질문 리플레이 심화 벤치 리포트

- 실행시각(UTC): 2026-05-10T06:49:51.513Z
- API_BASE_URL: http://localhost:8080/api
- 세션 수집 수: 3
- KB 수: N/A (권한 없는 모드)
- 베이스 질문 수: 4
- 반복 라운드: 20
- 리플레이 총 케이스 수: 80
- 동시성: 8

## 1) 저장 질문 리플레이(순차)

| 지표 | 값 |
|---|---:|
| 시스템 성공률 | 100.00% |
| 품질 일관성률 | 75.00% |
| 평균 지연 | 1633.3 ms |
| P50 지연 | 936.0 ms |
| P95 지연 | 4429.0 ms |
| P99 지연 | 7920.0 ms |
| TopSimilarity Δ 평균 | 0.2881 |
| TopSimilarity Δ P95 | 0.7895 |

### 실패 원인 분포

| 원인 | 건수 |
|---|---:|
| low_similarity_match | 40 |
| non_low_similarity | 20 |
| unexpected_low_similarity | 20 |

## 2) 저장 질문 리플레이(동시 부하)

| 지표 | 값 |
|---|---:|
| 시스템 성공률 | 100.00% |
| 평균 지연 | 1506.1 ms |
| P50 지연 | 943.0 ms |
| P95 지연 | 4211.0 ms |
| P99 지연 | 4764.0 ms |
| Throughput | 4.73 req/s |
| 총 소요 | 16.92 s |

## 3) 품질 불일치 상위 30건

| sessionId | role | platform | 질문 | 기대 low | 실제 low | 기대 KB | 실제 KB | 사유 |
|---:|---|---|---|---|---|---|---|---|
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |
| 1 | user | 트러스트온 | 인증서 조회가 되지 않는 경우에 어떻게 해야해? | false | true | - | - | unexpected_low_similarity |

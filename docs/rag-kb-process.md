# RAG + KB 현재 프로세스 (코드 기준)

이 문서는 현재 구현 기준으로 KB 등록부터 사용자 질문/답변까지의 실제 흐름을 설명합니다.
순서는 운영 시나리오 기준으로 KB 운영 -> 사용자 질문 -> 답변 생성 -> 로그/진단입니다.

대상 소스

- AiDeskApi/Controllers/KnowledgeBaseController.cs
- AiDeskApi/Services/KnowledgeBaseVectorSyncQueue.cs
- AiDeskApi/Services/OpenAiRagService.cs
- AiDeskApi/Services/QdrantVectorSearchService.cs
- AiDeskApi/Controllers/ChatController.cs

---

## 0. 현재 버전 핵심 변경

1. KB 벡터 동기화 상태를 KB 엔티티로 관리합니다.
2. 대량 KB 등록 시 벡터 동기화는 백그라운드 큐에서 순차 처리합니다.
3. 관리자 UI에서 벡터 상태 칩 클릭으로 단건 재동기화가 가능합니다.

상태값

- pending: 동기화 대기
- synced: 동기화 완료
- failed: 동기화 실패

---

## 1. KB 운영 파이프라인

## 1.1 단건 KB 등록

```text
POST /api/knowledgebase
  -> 입력 검증
  -> KB/예상질문 RDB 저장
  -> 벡터 업서트 시도
  -> 성공: synced + VectorSyncedAt 저장
  -> 실패: failed 저장
```

특징

1. 단건은 요청 내 즉시 동기화를 시도합니다.
2. 실패해도 KB 데이터 저장은 유지합니다.

## 1.2 단건 KB 수정

```text
PUT /api/knowledgebase/{id}
  -> 메타/예상질문 변경 반영
  -> 벡터 업서트
  -> stale 예상질문 포인트 정리
  -> 상태값 갱신(synced/failed)
```

## 1.3 대량 KB 등록 (CSV)

```text
POST /api/knowledgebase/bulk-import
  -> CSV 파싱/검증
  -> 각 KB를 RDB에 저장
  -> VectorSyncStatus=pending으로 저장
  -> 백그라운드 큐에 KB ID enqueue
  -> API는 동기화 완료 대기 없이 응답
```

의도

1. 대량 등록 요청과 임베딩 처리 시간을 분리합니다.
2. HTTP 타임아웃과 운영 중단 위험을 낮춥니다.

## 1.4 백그라운드 벡터 동기화 워커

```text
KnowledgeBaseVectorSyncWorker
  -> 앱 시작 시 pending KB 재큐잉
  -> 큐에서 KB ID를 1건씩 순차 소비
  -> Qdrant Upsert
  -> 성공: synced + VectorSyncedAt
  -> 실패: failed
```

현재 처리 방식

- 단일 리더 기반 순차 처리
- 동일 KB 중복 enqueue 방지
- 실패 시 상태만 failed로 남기고 다음 건 처리

## 1.5 수동 동기화

단건 재동기화

- POST /api/knowledgebase/{id}/sync-vector
- 현재 구현은 해당 KB를 Upsert 방식으로 다시 반영합니다.

전체 재구축

- POST /api/knowledgebase/rebuild-vector-index
- 컬렉션 초기화 후 전체 KB 동기화

주의

- 단건 sync-vector는 기본적으로 업서트 방식입니다.
- 강한 의미의 초기화 후 재생성이 필요하면 delete 후 upsert 정책을 별도 정의해야 합니다.

---

## 2. 사용자 질문 -> 챗봇 답변 파이프라인

```text
사용자 질문 입력
  -> 질문 정규화
  -> 필요 시 질문 정제(대화 이력 기반)
  -> 질문 임베딩 생성
  -> Qdrant raw search (document/expected)
  -> KB 후보 병합
  -> keyword boost 반영 정렬(adjusted)
  -> 조건부 LLM rerank
  -> semantic threshold로 통과 판정
  -> selectedResults 컨텍스트 구성
  -> GPT 답변 생성 또는 저유사도 fallback
  -> 대화/진단 로그 저장
```

핵심 정책

1. 후보 정렬에는 semantic + keyword를 사용합니다.
2. 최종 통과/실패 판정은 semantic 기준입니다.
3. low similarity는 별도 운영 로그로 축적합니다.

---

## 3. 데이터 저장/진단 규칙

1. ChatMessage에 질문/답변/메타를 저장합니다.
2. 저유사도 질문은 LowSimilarityQuestions에 저장합니다.
3. KB 조회수는 실제 selected 결과 기반으로 반영합니다.
4. retrieval diagnostics로 후보 점수 추적이 가능합니다.

운영 활용

1. 실패 질문군 재학습/KB 보강
2. 오탐/누락 케이스 분석
3. threshold/rerank 정책 튜닝 근거 확보

---

## 4. 코드리뷰 체크리스트 (현재 구조 기준)

### 4.1 정합성

1. RDB 상태와 Qdrant 상태가 장기적으로 일치하는가
2. pending/failed 건의 재처리 경로가 명확한가
3. 단건 동기화와 백그라운드 동기화의 충돌 가능성이 통제되는가

### 4.2 장애 복구

1. 서버 재시작 후 pending 재큐잉이 보장되는가
2. failed 건이 누락되지 않고 운영자에게 노출되는가
3. 전체 재구축 API 실패 시 롤백/재시도 전략이 있는가

### 4.3 성능

1. 대량 등록 API 지연이 임베딩 처리와 분리되는가
2. 워커 처리량과 질문 응답 지연이 상호 간섭하지 않는가
3. OpenAI/Qdrant 호출 실패 편차가 tail latency에 미치는 영향이 관리되는가

### 4.4 보안/권한

1. 관리자 전용 API 권한 경계가 명확한가
2. 시크릿/API 키가 환경변수로 분리되는가
3. 운영 로그에 민감정보 노출이 없는가

---

## 5. 운영 API 빠른 참조

| 목적 | API |
|---|---|
| 단건 등록 | POST /api/knowledgebase |
| 단건 수정 | PUT /api/knowledgebase/{id} |
| 목록 조회 | GET /api/knowledgebase/list |
| 단건 조회 | GET /api/knowledgebase/{id} |
| CSV 업로드 | POST /api/knowledgebase/bulk-import |
| 단건 재동기화 | POST /api/knowledgebase/{id}/sync-vector |
| 전체 재임베딩 | POST /api/knowledgebase/rebuild-vector-index |

---

## 6. 발표 시 결론 문구

- 현재 구조는 KB 운영과 질의응답 파이프라인을 분리해 안정성과 운영 가시성을 확보했습니다.
- 대량 등록은 비동기 순차 동기화로 전환되어 서비스 안정성이 개선됐고, 질문-답변 품질은 semantic 중심 판정으로 일관성을 유지합니다.
- 다음 개선축은 동시 처리 성능과 동기화 정책(업서트/초기화)의 운영 표준화입니다.
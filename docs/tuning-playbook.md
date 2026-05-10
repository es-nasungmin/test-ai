# 목표치 달성 튜닝 플레이북

목표:
1. 시스템 성공률 99.5% 이상
2. 100문항 품질 성공률 90% 이상
3. 저장 질문 품질 일관성률 90% 이상
4. P95 3000ms 이하

## 1단계: 측정 기준 고정

1. `BENCH_CASESET=live node scripts/benchmark-100.js`
2. `BENCH_REPLAY_ROUNDS=20 BENCH_CONCURRENCY=8 node scripts/benchmark-live-questions.js`
3. `reports/benchmark-100-latest.md`, `reports/benchmark-live-questions-latest.md`를 기준 리포트로 저장

## 2단계: 품질 일관성 미달 우선 해결

현재 주요 실패 패턴 예시:
- `unexpected_low_similarity`
- 과거에는 답변되던 질문이 재실행 시 저유사도로 분류됨

개선 순서:
1. 실패 질문별 `relatedKbMeta`와 `retrievalDebugMeta` 비교
2. 해당 KB의 예상질문 표현 확대(동의어/오타/구어체)
3. 질문 정규화 사전(치환 규칙) 보강
4. `similarityThreshold`와 프롬프트 규칙 점검

## 3단계: 지연 최적화

1. 느린 케이스의 RAG 단계별 시간 분해(임베딩/벡터검색/rerank/answer)
2. rerank 스킵 조건(고신뢰/점수 gap) 재검토
3. 후보 수(`RawVectorTopK`, `MergeTopK`, `ReRankTopK`) 축소 실험
4. 동시성 8에서 P95와 처리량 재측정

## 4단계: 릴리스 게이트

아래를 모두 만족할 때만 운영 반영:

1. 100문항 품질 성공률 >= 90%
2. 저장 질문 품질 일관성률 >= 90%
3. P95 <= 3000ms
4. 동시성 8 처리량 >= 6 req/s

미달 시 2~3단계 반복

# RAG KB v2 프로세스 정리

이 문서는 문서형 KB 구조(제목/내용/키워드/예상질문) 기반의 저장 및 검색 파이프라인을 정리합니다.

## 1. KB 작성/저장 프로세스

### 1.1 작성 입력 순서
1. 공개수준 선택 (`user` / `admin`)
2. 제목 작성
3. 내용 작성
4. 키워드 생성 (AI)
5. 플랫폼 선택
6. 예상질문 생성 (AI, 최대 5개)

### 1.2 AI 생성 규칙
- 키워드 생성 입력: `제목 + 내용 (+ 이미 입력된 예상질문)`
- 키워드 개수: 최대 5개 권장
- 예상질문 생성 입력: `제목 + 내용`
- 예상질문 개수 제한: 최대 5개
- 사용자는 AI 결과에 수동 추가/수정 가능

### 1.3 저장 시 임베딩
저장 직전 아래 문자열을 구성하여 단일 임베딩을 생성합니다.

- `제목: ...`
- `내용: ...`
- `예상질문: q1 | q2 | q3 ...`

이 합본 텍스트를 임베딩하여 KB 검색 벡터로 사용합니다.

## 2. 사용자 질문 처리 프로세스

### 2.1 벡터 검색
1. 사용자 질문 임베딩
2. 유사도 상위 `top 15` 벡터 후보 조회
3. 임계치(threshold) 이상 후보만 통과

### 2.2 문서형 KB 보조 검색 (조건부)
- 벡터 임계치 통과 KB가 하나도 없을 때만 문서형 KB(PDF chunk) 검색 수행
- 문서 검색 결과도 동일 임계치로 필터링

### 2.3 키워드 기반 후보 검색
1. 사용자 질문 키워드 추출
2. KB 점수 계산
   - 제목/키워드에 토큰 일치: `+2`
   - 내용에 토큰 일치: `+1`
3. 점수 순으로 `top 10` 후보 추출

### 2.4 후보 통합 및 재정렬
1. 벡터 통과 후보 + 키워드 후보를 합집합으로 통합
2. 중복 제거
3. AI reranking으로 관련도 재정렬
4. 최종 `top 5` 선택

### 2.5 답변 생성
- 최종 top 5를 근거 컨텍스트로 답변 생성
- 근거 부족/부적합 시 낮은 유사도 안내 문구로 응답
- 근거 외 추측 답변 금지

## 3. 운영 포인트

- 예상질문은 양보다 다양성이 중요 (표현 분산)
- 키워드는 사용자 실제 질의 표현과 동기화
- 저유사도 문의를 주기적으로 확인해 예상질문/키워드에 반영
- 플랫폼 필터를 강제해 잘못된 채널 혼입 방지

## 4. 현재 구현 반영 파일

- Backend
  - `AiDeskApi/Controllers/KnowledgeBaseController.cs`
  - `AiDeskApi/Services/OpenAiRagService.cs`
  - `AiDeskApi/Services/QdrantVectorSearchService.cs`
  - `AiDeskApi/Services/KnowledgeExtractorService.cs`

- Frontend
  - `AiDeskClient/src/components/Management/KBManagement.vue`
  - `AiDeskClient/src/components/Management/ChatLogManagement.vue`

## 5. 검증

- `dotnet build AiDesk.sln` 성공
- `npm run build` (AiDeskClient) 성공

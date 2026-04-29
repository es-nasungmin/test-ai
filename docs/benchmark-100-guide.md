# 100문항 자동 벤치 가이드

## 1. 구성 파일

1. scripts/demo-kb-catalog.js
- 데모 KB 카탈로그(시드 데이터 원본)

2. scripts/seed-demo-kb.js
- 카탈로그 기반 테스트 KB 자동 주입 스크립트

3. scripts/benchmark-100.js
- 100문항(Positive 90 + Negative 10) 자동 평가 스크립트

## 2. 실행 순서

1. 테스트 KB 주입

```bash
node scripts/seed-demo-kb.js
```

옵션:

```bash
# 기존 KB 삭제 없이 추가만
RESET_BEFORE_SEED=false node scripts/seed-demo-kb.js
```

2. 100문항 벤치 실행

```bash
node scripts/benchmark-100.js
```

## 3. 산출물

스크립트 실행 후 reports 폴더에 아래 파일이 생성됩니다.

1. reports/benchmark-100-latest.json
- 원시 결과

2. reports/benchmark-100-latest.md
- 요약 리포트

3. 타임스탬프 버전 파일
- reports/benchmark-100-YYYY-MM-DDTHH-MM-SS-msZ.json
- reports/benchmark-100-YYYY-MM-DDTHH-MM-SS-msZ.md

## 4. 리포트 포맷

요약 표 필수 항목:

1. 시스템 성공률
2. 품질 성공률
3. Positive 품질 성공률
4. Negative 품질 성공률
5. 평균/P50/P95 응답시간

실패 케이스 표 필수 항목:

1. 문항 ID
2. 문항 유형(positive/negative)
3. 질문 원문
4. HTTP 상태코드
5. 저유사도 여부
6. 매칭된 KB 제목
7. 품질 통과 여부

## 5. 합격/불합격 기준

스크립트 기준:

1. 시스템 성공률 99.5% 미만 또는
2. 품질 성공률 90.0% 미만

이면 프로세스 종료 코드 2를 반환합니다.

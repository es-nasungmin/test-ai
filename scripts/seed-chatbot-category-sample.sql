-- ChatbotCategory 샘플 데이터 (MSSQL/SQLite 공용)
-- 사용 방법:
-- 1) 기존 테스트 데이터를 지우고 다시 넣으려면 아래 DELETE를 실행
-- 2) 정책자금 추천 흐름 + 일반 메뉴 흐름을 테스트 가능

DELETE FROM ChatbotCategory;

INSERT INTO ChatbotCategory (CategoryID, ParentCategoryID, Type, Title, Content, UseYN, SortOrder, CreatedAt, CreatedBy) VALUES
(1000, NULL, 'MENU', '정책자금 추천', '안녕하세요. 맞춤형 정책자금 추천을 위해 몇 가지 질문을 드릴게요.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(2000, NULL, 'MENU', '정책자금 안내', '정책자금 제도, 신청 자격, 제출 서류를 안내해드릴게요.', 'Y', 2, CURRENT_TIMESTAMP, 1),
(3000, NULL, 'MENU', '자금 신청', '자금 신청 절차를 단계별로 안내해드릴게요.', 'Y', 3, CURRENT_TIMESTAMP, 1),
(4000, NULL, 'MENU', '대출 조회', '대출 진행 상태 및 실행 내역 조회 방법을 안내해드릴게요.', 'Y', 4, CURRENT_TIMESTAMP, 1),
(5000, NULL, 'MENU', '사후 관리', '사후관리 점검 및 보고 관련 내용을 안내해드릴게요.', 'Y', 5, CURRENT_TIMESTAMP, 1),
(6000, NULL, 'MENU', '전자 약정', '전자약정 진행 방법과 자주 발생하는 오류를 안내해드릴게요.', 'Y', 6, CURRENT_TIMESTAMP, 1);

INSERT INTO ChatbotCategory (CategoryID, ParentCategoryID, Type, Title, Content, UseYN, SortOrder, CreatedAt, CreatedBy) VALUES
(1100, 1000, 'QUESTION', '시설자금', '영위하시는 기업의 업력은 몇 년인가요?', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1200, 1000, 'QUESTION', '운전자금', '영위하시는 기업의 업력은 몇 년인가요?', 'Y', 2, CURRENT_TIMESTAMP, 1);

INSERT INTO ChatbotCategory (CategoryID, ParentCategoryID, Type, Title, Content, UseYN, SortOrder, CreatedAt, CreatedBy) VALUES
(1110, 1100, 'QUESTION', '3년 미만', '연 매출 규모는 어느 구간인가요?', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1120, 1100, 'QUESTION', '3~7년 미만', '연 매출 규모는 어느 구간인가요?', 'Y', 2, CURRENT_TIMESTAMP, 1),
(1130, 1100, 'QUESTION', '7년 이상', '연 매출 규모는 어느 구간인가요?', 'Y', 3, CURRENT_TIMESTAMP, 1),
(1210, 1200, 'QUESTION', '3년 미만', '연 매출 규모는 어느 구간인가요?', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1220, 1200, 'QUESTION', '3~7년 미만', '연 매출 규모는 어느 구간인가요?', 'Y', 2, CURRENT_TIMESTAMP, 1),
(1230, 1200, 'QUESTION', '7년 이상', '연 매출 규모는 어느 구간인가요?', 'Y', 3, CURRENT_TIMESTAMP, 1);

INSERT INTO ChatbotCategory (CategoryID, ParentCategoryID, Type, Title, Content, UseYN, SortOrder, CreatedAt, CreatedBy) VALUES
(1111, 1110, 'ANSWER', '5억 미만', '추천 결과: 창업기반자금(시설) 검토 대상입니다. 신청 전 최근 재무제표와 사업계획서 준비를 권장드립니다.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1112, 1110, 'ANSWER', '5억 이상', '추천 결과: 성장공유형 자금(시설) 검토 대상입니다. 투자/고용 계획을 포함한 확장 계획을 준비해주세요.', 'Y', 2, CURRENT_TIMESTAMP, 1),
(1121, 1120, 'ANSWER', '10억 미만', '추천 결과: 일반경영안정자금(시설) 검토 대상입니다. 업종별 지원 제한 여부를 함께 확인해주세요.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1122, 1120, 'ANSWER', '10억 이상', '추천 결과: 신성장기반자금(시설) 검토 대상입니다. 기술성 평가 자료를 준비해주세요.', 'Y', 2, CURRENT_TIMESTAMP, 1),
(1131, 1130, 'ANSWER', '20억 미만', '추천 결과: 재도약지원자금(시설) 검토 대상입니다. 사업전환 계획서가 있으면 유리합니다.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1132, 1130, 'ANSWER', '20억 이상', '추천 결과: 고도화자금(시설) 검토 대상입니다. 스마트공장/자동화 투자 항목을 정리해주세요.', 'Y', 2, CURRENT_TIMESTAMP, 1),
(1211, 1210, 'ANSWER', '5억 미만', '추천 결과: 창업기업 운전자금 검토 대상입니다. 최근 6개월 자금흐름 자료를 준비해주세요.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1212, 1210, 'ANSWER', '5억 이상', '추천 결과: 초기성장 운전자금 검토 대상입니다. 매출채권 회전 현황 확인이 필요합니다.', 'Y', 2, CURRENT_TIMESTAMP, 1),
(1221, 1220, 'ANSWER', '10억 미만', '추천 결과: 일반경영안정자금(운전) 검토 대상입니다. 인건비/원재료 비중을 포함해 자금 소요를 정리해주세요.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1222, 1220, 'ANSWER', '10억 이상', '추천 결과: 수출/성장형 운전자금 검토 대상입니다. 수출 실적 또는 계약 예정 자료를 준비해주세요.', 'Y', 2, CURRENT_TIMESTAMP, 1),
(1231, 1230, 'ANSWER', '20억 미만', '추천 결과: 재도약지원자금(운전) 검토 대상입니다. 기존 대출 현황과 상환 계획을 함께 준비해주세요.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(1232, 1230, 'ANSWER', '20억 이상', '추천 결과: 고도화 운전자금 검토 대상입니다. 대규모 운영계획과 매출 전망 근거가 필요합니다.', 'Y', 2, CURRENT_TIMESTAMP, 1);

INSERT INTO ChatbotCategory (CategoryID, ParentCategoryID, Type, Title, Content, UseYN, SortOrder, CreatedAt, CreatedBy) VALUES
(2100, 2000, 'ANSWER', '신청 자격', '정책자금 신청 자격은 기업 업력, 업종, 매출/고용 요건 등에 따라 달라집니다. 공고문 기준으로 최종 확인이 필요합니다.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(2200, 2000, 'ANSWER', '제출 서류', '기본적으로 사업자등록증, 재무제표, 국세/지방세 납세증명, 사업계획서가 필요할 수 있습니다.', 'Y', 2, CURRENT_TIMESTAMP, 1),
(3100, 3000, 'ANSWER', '온라인 신청 절차', '자금 신청은 회원가입/인증 > 신청서 작성 > 서류 업로드 > 심사 > 약정 순서로 진행됩니다.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(4100, 4000, 'ANSWER', '대출 진행 상태 조회', '대출 조회 메뉴에서 접수번호 또는 기업정보 기준으로 심사 상태를 확인할 수 있습니다.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(5100, 5000, 'ANSWER', '사후관리 보고', '사후관리 항목은 자금 사용내역, 고용/매출 실적, 시설 도입 현황 등이 포함될 수 있습니다.', 'Y', 1, CURRENT_TIMESTAMP, 1),
(6100, 6000, 'ANSWER', '전자약정 오류 해결', '브라우저 인증서 설정, 팝업 허용, 보안 프로그램 설치 여부를 확인한 뒤 다시 시도해주세요.', 'Y', 1, CURRENT_TIMESTAMP, 1);

namespace CrmApi.Services
{
    public interface ISummaryPromptTemplateService
    {
        string SingleConsultationTemplate { get; }
        string AllConsultationsTemplate { get; }
        void Update(string singleConsultationTemplate, string allConsultationsTemplate);
    }

    public class SummaryPromptTemplateService : ISummaryPromptTemplateService
    {
        // Placeholders:
        // - 단건 템플릿: {type}, {content}
        // - 전체 템플릿: {companyName}, {consultationText}
        private const string DefaultSingleConsultationTemplate = @"당신은 CRM 상담 정리 도우미입니다.
    아래 상담 내용을 한국어로 간결하고 실무적으로 정리하세요.

    [출력 형식]
    1) 핵심 이슈 (1~2줄)
    2) 고객 요청사항 (불릿)
    3) 현재 상태/원인 (불릿)
    4) 처리 사항 (불릿)
    5) 후속 액션 (담당/기한이 있으면 함께)

    [규칙]
    - 사실 기반으로만 작성하고 추측은 금지합니다.
    - 불필요한 수식어 없이 명확하게 작성합니다.
    - 내용이 없는 항목은 출력하지 않습니다.
    - 하나의 상담에 문의가 2개 이상이면 문의 항목을 분리해 각각 작성합니다.
    - 각 문의 항목마다 원인/안내/처리 결과를 따로 정리합니다.

    상담 유형: {type}
    상담 내용:
    {content}";

        private const string DefaultAllConsultationsTemplate = @"당신은 CRM 이력 분석 도우미입니다.
    아래 고객의 전체 상담 이력을 종합해 한국어로 보고서 형태로 요약하세요.

    [출력 형식]
    1) 고객 요약 (업종/상황/핵심 관심사)
    2) 이력 타임라인 (시간순 핵심 사건 3~7개)
    3) 반복 이슈 및 패턴
    4) 미해결 과제/리스크
    5) 다음 액션 제안 (우선순위 High/Medium/Low)

    [규칙]
    - 상담 기록에 있는 사실만 사용합니다.
    - 중복 내용은 합쳐서 간결히 작성합니다.
    - 실행 가능한 액션 중심으로 작성합니다.

    업체명: {companyName}
    상담 이력:
    {consultationText}";

        private readonly object _sync = new();

        private string _singleConsultationTemplate = DefaultSingleConsultationTemplate;
        private string _allConsultationsTemplate = DefaultAllConsultationsTemplate;

        public string SingleConsultationTemplate
        {
            get
            {
                lock (_sync)
                {
                    return _singleConsultationTemplate;
                }
            }
        }

        public string AllConsultationsTemplate
        {
            get
            {
                lock (_sync)
                {
                    return _allConsultationsTemplate;
                }
            }
        }

        public void Update(string singleConsultationTemplate, string allConsultationsTemplate)
        {
            if (string.IsNullOrWhiteSpace(singleConsultationTemplate))
            {
                throw new ArgumentException("단건 요약 프롬프트는 비울 수 없습니다.");
            }

            if (string.IsNullOrWhiteSpace(allConsultationsTemplate))
            {
                throw new ArgumentException("전체 요약 프롬프트는 비울 수 없습니다.");
            }

            lock (_sync)
            {
                _singleConsultationTemplate = singleConsultationTemplate.Trim();
                _allConsultationsTemplate = allConsultationsTemplate.Trim();
            }
        }
    }
}

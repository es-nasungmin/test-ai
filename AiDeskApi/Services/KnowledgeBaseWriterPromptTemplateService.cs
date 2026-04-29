using Microsoft.EntityFrameworkCore;
using AiDeskApi.Data;
using AiDeskApi.Models;

namespace AiDeskApi.Services
{
    public interface IKnowledgeBaseWriterPromptTemplateService
    {
        Task<KnowledgeBaseWriterPromptTemplateSnapshot> GetAsync();
        Task<KnowledgeBaseWriterPromptTemplateSnapshot> UpdateAsync(
            string keywordSystemPrompt,
            string keywordRulesPrompt,
            string similarQuestionSystemPrompt,
            string similarQuestionRulesPrompt,
            string topicKeywordSystemPrompt,
            string topicKeywordRulesPrompt,
            string answerRefineSystemPrompt,
            string answerRefineRulesPrompt);
    }

    public class KnowledgeBaseWriterPromptTemplateService : IKnowledgeBaseWriterPromptTemplateService
    {
        private readonly AiDeskContext _context;

        private const string DefaultKeywordSystemPrompt = "당신은 고객센터 KB 검색 최적화 도우미입니다. 제목, 본문, 예상질문을 바탕으로 실제 검색 적중률을 높이는 핵심 키워드를 추출합니다.";
        private const string DefaultKeywordRulesPrompt = "1) 반드시 JSON 문자열 배열만 응답한다\n2) 핵심 기능/대상/증상/원인 중심의 구체 키워드를 우선한다\n3) 사용자 검색어 관점(실제 문의 표현)과 도메인 용어를 함께 반영한다\n4) 중복/유사 표현은 통합한다\n5) 너무 포괄적인 단어(예: 오류, 문제, 문의)는 단독으로 사용하지 않는다";
        private const string DefaultSimilarQuestionSystemPrompt = "당신은 고객 문의 패턴 설계 도우미입니다. KB 내용을 바탕으로 실제 사용자가 입력할 다양한 질문 표현을 생성해 검색 타겟팅 확률을 높입니다.";
        private const string DefaultSimilarQuestionRulesPrompt = "1) 반드시 JSON 문자열 배열만 응답한다\n2) 각 질문은 실제 사용자 말투로 짧고 자연스럽게 작성한다\n3) 같은 의미라도 표현/어순/어휘를 바꿔 다양화한다\n4) 핵심 증상, 상황, 실패지점을 반영해 타겟팅 범위를 넓힌다\n5) 문서 근거 밖 내용은 만들지 않는다\n6) 제목 문장을 그대로 복사하지 않는다";
        private const string DefaultTopicKeywordSystemPrompt = "당신은 고객센터 KB 분류 도우미입니다. 답변 내용을 기준으로 주제와 도메인 키워드를 추출합니다.";
        private const string DefaultTopicKeywordRulesPrompt = "1) 반드시 JSON 문자열 배열만 응답한다\n2) 도메인 용어와 주제 분류에 중점을 둔다\n3) 다른 KB와의 관련도 연결에 도움이 되는 키워드를 우선한다\n4) 너무 일반적인 단어는 제외한다";
        private const string DefaultAnswerRefineSystemPrompt = "당신은 고객 안내문 편집자입니다. 초안을 고객이 이해하기 쉽고 바로 따라할 수 있는 안내문으로 정리합니다.";
        private const string DefaultAnswerRefineRulesPrompt = "1) 사실/정책/수치/조건은 바꾸지 않는다\n2) 어려운 표현은 쉬운 한국어로 바꾼다\n3) 절차가 있으면 번호 목록으로 명확히 정리한다\n4) 고객이 바로 실행할 수 있도록 단계별 행동을 분명히 적는다\n5) 길고 복잡한 문장은 짧게 나눠 가독성을 높인다";

        public KnowledgeBaseWriterPromptTemplateService(AiDeskContext context)
        {
            _context = context;
        }

        public async Task<KnowledgeBaseWriterPromptTemplateSnapshot> GetAsync()
        {
            var entity = await EnsureTemplateAsync();
            return ToSnapshot(entity);
        }

        public async Task<KnowledgeBaseWriterPromptTemplateSnapshot> UpdateAsync(
            string keywordSystemPrompt,
            string keywordRulesPrompt,
            string similarQuestionSystemPrompt,
            string similarQuestionRulesPrompt,
            string topicKeywordSystemPrompt,
            string topicKeywordRulesPrompt,
            string answerRefineSystemPrompt,
            string answerRefineRulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(keywordSystemPrompt)) throw new ArgumentException("키워드 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(keywordRulesPrompt)) throw new ArgumentException("키워드 규칙 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(similarQuestionSystemPrompt)) throw new ArgumentException("예상질문 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(similarQuestionRulesPrompt)) throw new ArgumentException("예상질문 규칙 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(topicKeywordSystemPrompt)) throw new ArgumentException("주제 키워드 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(topicKeywordRulesPrompt)) throw new ArgumentException("주제 키워드 규칙 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(answerRefineSystemPrompt)) throw new ArgumentException("답변 정리 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(answerRefineRulesPrompt)) throw new ArgumentException("답변 정리 규칙 프롬프트는 비울 수 없습니다.");

            var entity = await EnsureTemplateAsync();
            entity.KeywordSystemPrompt = keywordSystemPrompt.Trim();
            entity.KeywordRulesPrompt = keywordRulesPrompt.Trim();
            entity.SimilarQuestionSystemPrompt = similarQuestionSystemPrompt.Trim();
            entity.SimilarQuestionRulesPrompt = similarQuestionRulesPrompt.Trim();
            entity.TopicKeywordSystemPrompt = topicKeywordSystemPrompt.Trim();
            entity.TopicKeywordRulesPrompt = topicKeywordRulesPrompt.Trim();
            entity.AnswerRefineSystemPrompt = answerRefineSystemPrompt.Trim();
            entity.AnswerRefineRulesPrompt = answerRefineRulesPrompt.Trim();
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return ToSnapshot(entity);
        }

        private async Task<KnowledgeBaseWriterPromptTemplate> EnsureTemplateAsync()
        {
            var entity = await _context.KnowledgeBaseWriterPromptTemplates
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (entity != null)
            {
                var changed = false;

                var keywordSystemPrompt = NormalizeLineBreaks(entity.KeywordSystemPrompt);
                if (!string.Equals(keywordSystemPrompt, entity.KeywordSystemPrompt, StringComparison.Ordinal))
                {
                    entity.KeywordSystemPrompt = keywordSystemPrompt;
                    changed = true;
                }

                var keywordRulesPrompt = NormalizeLineBreaks(entity.KeywordRulesPrompt);
                if (!string.Equals(keywordRulesPrompt, entity.KeywordRulesPrompt, StringComparison.Ordinal))
                {
                    entity.KeywordRulesPrompt = keywordRulesPrompt;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(entity.SimilarQuestionSystemPrompt))
                {
                    entity.SimilarQuestionSystemPrompt = DefaultSimilarQuestionSystemPrompt;
                    changed = true;
                }
                else
                {
                    var similarQuestionSystemPrompt = NormalizeLineBreaks(entity.SimilarQuestionSystemPrompt);
                    if (!string.Equals(similarQuestionSystemPrompt, entity.SimilarQuestionSystemPrompt, StringComparison.Ordinal))
                    {
                        entity.SimilarQuestionSystemPrompt = similarQuestionSystemPrompt;
                        changed = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(entity.SimilarQuestionRulesPrompt))
                {
                    entity.SimilarQuestionRulesPrompt = DefaultSimilarQuestionRulesPrompt;
                    changed = true;
                }
                else
                {
                    var similarQuestionRulesPrompt = NormalizeLineBreaks(entity.SimilarQuestionRulesPrompt);
                    if (!string.Equals(similarQuestionRulesPrompt, entity.SimilarQuestionRulesPrompt, StringComparison.Ordinal))
                    {
                        entity.SimilarQuestionRulesPrompt = similarQuestionRulesPrompt;
                        changed = true;
                    }
                }

                // 기존 DB에 필드가 없을 수 있으므로 초기화
                if (string.IsNullOrWhiteSpace(entity.TopicKeywordSystemPrompt))
                {
                    entity.TopicKeywordSystemPrompt = DefaultTopicKeywordSystemPrompt;
                    changed = true;
                }
                else
                {
                    var topicKeywordSystemPrompt = NormalizeLineBreaks(entity.TopicKeywordSystemPrompt);
                    if (!string.Equals(topicKeywordSystemPrompt, entity.TopicKeywordSystemPrompt, StringComparison.Ordinal))
                    {
                        entity.TopicKeywordSystemPrompt = topicKeywordSystemPrompt;
                        changed = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(entity.TopicKeywordRulesPrompt))
                {
                    entity.TopicKeywordRulesPrompt = DefaultTopicKeywordRulesPrompt;
                    changed = true;
                }
                else
                {
                    var topicKeywordRulesPrompt = NormalizeLineBreaks(entity.TopicKeywordRulesPrompt);
                    if (!string.Equals(topicKeywordRulesPrompt, entity.TopicKeywordRulesPrompt, StringComparison.Ordinal))
                    {
                        entity.TopicKeywordRulesPrompt = topicKeywordRulesPrompt;
                        changed = true;
                    }
                }

                var answerRefineSystemPrompt = NormalizeLineBreaks(entity.AnswerRefineSystemPrompt);
                if (!string.Equals(answerRefineSystemPrompt, entity.AnswerRefineSystemPrompt, StringComparison.Ordinal))
                {
                    entity.AnswerRefineSystemPrompt = answerRefineSystemPrompt;
                    changed = true;
                }

                var answerRefineRulesPrompt = NormalizeLineBreaks(entity.AnswerRefineRulesPrompt);
                if (!string.Equals(answerRefineRulesPrompt, entity.AnswerRefineRulesPrompt, StringComparison.Ordinal))
                {
                    entity.AnswerRefineRulesPrompt = answerRefineRulesPrompt;
                    changed = true;
                }

                if (changed)
                {
                    entity.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return entity;
            }

            entity = new KnowledgeBaseWriterPromptTemplate
            {
                KeywordSystemPrompt = DefaultKeywordSystemPrompt,
                KeywordRulesPrompt = DefaultKeywordRulesPrompt,
                SimilarQuestionSystemPrompt = DefaultSimilarQuestionSystemPrompt,
                SimilarQuestionRulesPrompt = DefaultSimilarQuestionRulesPrompt,
                TopicKeywordSystemPrompt = DefaultTopicKeywordSystemPrompt,
                TopicKeywordRulesPrompt = DefaultTopicKeywordRulesPrompt,
                AnswerRefineSystemPrompt = DefaultAnswerRefineSystemPrompt,
                AnswerRefineRulesPrompt = DefaultAnswerRefineRulesPrompt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.KnowledgeBaseWriterPromptTemplates.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        private static KnowledgeBaseWriterPromptTemplateSnapshot ToSnapshot(KnowledgeBaseWriterPromptTemplate entity)
        {
            return new KnowledgeBaseWriterPromptTemplateSnapshot(
                entity.KeywordSystemPrompt,
                entity.KeywordRulesPrompt,
                entity.SimilarQuestionSystemPrompt,
                entity.SimilarQuestionRulesPrompt,
                entity.TopicKeywordSystemPrompt,
                entity.TopicKeywordRulesPrompt,
                entity.AnswerRefineSystemPrompt,
                entity.AnswerRefineRulesPrompt);
        }

        private static string NormalizeLineBreaks(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\r\n", "\n");
        }
    }

    public record KnowledgeBaseWriterPromptTemplateSnapshot(
        string KeywordSystemPrompt,
        string KeywordRulesPrompt,
        string SimilarQuestionSystemPrompt,
        string SimilarQuestionRulesPrompt,
        string TopicKeywordSystemPrompt,
        string TopicKeywordRulesPrompt,
        string AnswerRefineSystemPrompt,
        string AnswerRefineRulesPrompt);
}

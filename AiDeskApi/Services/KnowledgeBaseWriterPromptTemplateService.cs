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
            string topicKeywordSystemPrompt,
            string topicKeywordRulesPrompt,
            string answerRefineSystemPrompt,
            string answerRefineRulesPrompt);
    }

    public class KnowledgeBaseWriterPromptTemplateService : IKnowledgeBaseWriterPromptTemplateService
    {
        private readonly AiDeskContext _context;

        private const string DefaultKeywordSystemPrompt = "당신은 고객센터 KB 작성 도우미입니다. 대표질문과 유사질문을 바탕으로 검색 효율이 높은 한국어 키워드를 추출합니다.";
        private const string DefaultKeywordRulesPrompt = "1) 반드시 JSON 문자열 배열만 응답한다\n2) 중복/유사어 반복을 제거한다\n3) 사용자 검색어 관점에서 짧고 구체적인 키워드를 우선한다\n4) 너무 포괄적인 단어(예: 오류, 문제)는 지양한다";
        private const string DefaultTopicKeywordSystemPrompt = "당신은 고객센터 KB 분류 도우미입니다. 답변 내용을 기준으로 주제와 도메인 키워드를 추출합니다.";
        private const string DefaultTopicKeywordRulesPrompt = "1) 반드시 JSON 문자열 배열만 응답한다\n2) 도메인 용어와 주제 분류에 중점을 둔다\n3) 다른 KB와의 관련도 연결에 도움이 되는 키워드를 우선한다\n4) 너무 일반적인 단어는 제외한다";
        private const string DefaultAnswerRefineSystemPrompt = "당신은 고객 지원 KB 문서 편집자입니다. 초안을 고객이 읽기 쉬운 안내문으로 다듬습니다.";
        private const string DefaultAnswerRefineRulesPrompt = "1) 사실을 바꾸지 말고 문장만 정리한다\n2) 단계가 있으면 번호 목록으로 정리한다\n3) 한 문단이 너무 길지 않게 끊는다\n4) 불필요한 수식어를 줄이고 실행 지시를 명확히 쓴다";

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
            string topicKeywordSystemPrompt,
            string topicKeywordRulesPrompt,
            string answerRefineSystemPrompt,
            string answerRefineRulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(keywordSystemPrompt)) throw new ArgumentException("키워드 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(keywordRulesPrompt)) throw new ArgumentException("키워드 규칙 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(topicKeywordSystemPrompt)) throw new ArgumentException("주제 키워드 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(topicKeywordRulesPrompt)) throw new ArgumentException("주제 키워드 규칙 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(answerRefineSystemPrompt)) throw new ArgumentException("답변 정리 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(answerRefineRulesPrompt)) throw new ArgumentException("답변 정리 규칙 프롬프트는 비울 수 없습니다.");

            var entity = await EnsureTemplateAsync();
            entity.KeywordSystemPrompt = keywordSystemPrompt.Trim();
            entity.KeywordRulesPrompt = keywordRulesPrompt.Trim();
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
        string TopicKeywordSystemPrompt,
        string TopicKeywordRulesPrompt,
        string AnswerRefineSystemPrompt,
        string AnswerRefineRulesPrompt);
}

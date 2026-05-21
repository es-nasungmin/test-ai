using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AiDeskApi.Data;
using AiDeskApi.Models;

namespace AiDeskApi.Services
{
    /// <summary>
    /// 사용자 질문을 임베딩, 벡터 검색, rerank, 답변 생성까지 이어주는 RAG 오케스트레이터입니다.
    /// </summary>
    public interface IRagService
    {
        // role: "admin" = 전체 KB 조회, "user" = visibility='user'만 조회
        // history: 이전 대화 메시지 [(role, content)] 최근 순 (오래된 것부터)
        // platform: 전체 플랫폼이면 전체 조회, 공통이면 공통만 조회, 특정값이면 공통 + 해당 플랫폼 조회
        /// <summary>
        /// 질문을 검색하고, 관련 KB를 바탕으로 최종 답변을 생성합니다.
        /// </summary>
        Task<RagResponse> SearchAndGenerateAsync(
            string question,
            string role = "user",
            string platform = "공통",
            IList<(string Role, string Content)>? history = null,
            RagRuntimeOptions? runtimeOptions = null);
    }

    // 질문 임베딩 -> 유사 KB 검색 -> 답변 생성까지 RAG 전체 흐름을 담당
    // KB 검색과 답변 생성을 묶어서 실제 챗봇 응답을 만드는 핵심 RAG 구현체
    public class OpenAiRagService : IRagService
    {
        // RAG 실행 시 고려하는 최근 대화이력 범위와 후보 수를 정의한다.
        // 1턴 = 질문 + 답변(2메시지) 기준
        private const int HistoryTurnLimit = 3;
        // 1턴을 구성하는 메시지 수 (질문 1 + 답변 1)
        private const int MessagesPerTurn = 2;
        // 최근 대화이력에서 RAG 답변 생성 프롬프트에 함께 보낼 메시지 수 (최근 3턴 = 최대 6메시지)
        private const int PromptHistoryMessageLimit = HistoryTurnLimit * MessagesPerTurn;
        // 질문 정제(RefineQuestionWithLlmAsync) 시 LLM에 함께 보낼 메시지 수 (최근 3턴 = 최대 6메시지)
        private const int RefineHistoryMessageLimit = HistoryTurnLimit * MessagesPerTurn;
        // 후속 질문 접두사
        private static readonly string[] FollowUpQuestionPrefixes =
        {
            "그럼", "그러면", "그건", "이건", "저건", "그거", "이거", "저거",
            "근데", "그런데", "아니", "추가로", "이어서", "그다음", "그 다음",
            "그 말고", "이 말고", "저 말고"
        };
        // 초기 벡터 검색 후보 수. document/expected를 분리하기 전에 넓게 가져온다.
        private const int RawVectorTopK = 40;
        // KB 본문(document) 포인트에서 유지할 상위 후보 수
        private const int DocumentVectorTopK = 15;
        // 예상질문(expected) 포인트에서 유지할 상위 후보 수
        private const int ExpectedVectorTopK = 20;
        // KB 단위 병합 이후 유지할 후보 수
        private const int MergeTopK = 10;
        // LLM rerank 대상으로 넘길 최대 후보 수
        private const int ReRankTopK = 8;
        // 최종 답변 컨텍스트에 사용할 최대 후보 수
        private const int FinalTopK = 5;
        // 실제 답변 생성 프롬프트에는 선택된 해법만 소수로 압축해 전달한다.
        private const int AnswerContextTopK = 3;
        private const float ReRankSkipScoreThreshold = 0.82f;   // 상위 후보가 이 이상이면 rerank 스킵
        private const float ReRankSkipGapThreshold = 0.15f;    // 1위~2위 점수 차가 이 이상이면 rerank 스킵
        private const float KeywordBoostPerMatch = 0.01f; // 키워드 매칭당 가산점 (최대 0.03f)
        private const float MaxKeywordBoost = 0.03f; // 키워드 가산점 상한
        private const float KeywordBoostHardFloorGap = 0.05f; // 키워드 가산점이 의미 있으려면 원래 점수가 임계치에서 이만큼 가까워야 함

        // EF Core DB 접근(지식베이스 조회/조회수 업데이트)
        private readonly AiDeskContext _context;
        // 질문/본문을 임베딩 벡터로 변환
        private readonly IEmbeddingService _embeddingService;
        // Qdrant 벡터 검색/동기화 진입점
        private readonly IVectorSearchService _vectorSearchService;
        // OpenAI Chat Completions 호출 클라이언트
        private readonly HttpClient _httpClient;
        // appsettings 및 환경변수 설정 접근
        private readonly IConfiguration _configuration;
        // role별 시스템/규칙 프롬프트 템플릿 제공
        private readonly IChatbotPromptTemplateService _promptTemplates;
        // RAG 파이프라인 진단 로그 기록
        private readonly ILogger<OpenAiRagService> _logger;
        // OpenAI 채팅 엔드포인트 URL
        private readonly string _chatCompletionsEndpoint;
        // 답변/질문정제에 사용하는 모델명
        private readonly string _chatModel;
        // 답변 생성 온도 파라미터
        private readonly float _chatTemperature;
        // 답변 생성 최대 토큰 수
        private readonly int _chatMaxTokens;

        public OpenAiRagService(
            AiDeskContext context,
            IEmbeddingService embeddingService,
            IVectorSearchService vectorSearchService,
            HttpClient httpClient,
            IConfiguration configuration,
            IChatbotPromptTemplateService promptTemplates,
            ILogger<OpenAiRagService> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _vectorSearchService = vectorSearchService;
            _httpClient = httpClient;
            _configuration = configuration;
            _promptTemplates = promptTemplates;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            _chatCompletionsEndpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
            _chatModel = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
            _chatTemperature = Math.Clamp(configuration.GetValue<float?>("OpenAI:ChatTemperature") ?? 0.7f, 0f, 2f);
            _chatMaxTokens = Math.Clamp(configuration.GetValue<int?>("OpenAI:ChatMaxTokens") ?? 1000, 100, 4000);
        }

        public async Task<RagResponse> SearchAndGenerateAsync(
            string question,
            string role = "user",
            string platform = "공통",
            IList<(string Role, string Content)>? history = null, // 최근 대화이력 (역순, 최대 3턴 = 6메시지)
            RagRuntimeOptions? runtimeOptions = null) // RAG 실행 시점에 적용할 옵션 (유사도 임계치/프롬프트 오버라이드 등)
        {
            try
            {
                _logger.LogInformation("📝 질문 받음 [{Role}/{Platform}]: {Question}", role, platform, question);
                runtimeOptions ??= new RagRuntimeOptions();

                // 1) 사용자 질문 정규화 + LLM 기반 질문 정제 + 임베딩 생성
                //    - 표기 흔들림(안됨/불가/안 보여요 등)을 정규화해 임베딩 분산을 줄인다.
                //    - 후속 질문("그래도 안보여" 등)은 LLM에게 대화이력과 함께 보내 정제된 단일 질문으로 변환한다.
                //    - 정제된 질문을 임베딩해 검색 품질을 높인다.
                var recentHistory = GetRecentHistoryMessages(history); // 최대 3턴(6메시지)까지 최근 대화이력을 가져온다.
                var normalizedQuestion = NormalizeQueryForEmbedding(question); // 표기 흔들림을 줄이는 정규화 수행 (예: "안 보여요" → "안보여")
                
                // 대화이력이 있으면 LLM이 정제된 단일 질문을 생성
                var refinedQuestion = normalizedQuestion;
                if (recentHistory.Count > 0)
                {
                    refinedQuestion = await RefineQuestionWithLlmAsync(question, recentHistory);
                    _logger.LogInformation("💡 질문 정제: '{Original}' → '{Refined}'", question, refinedQuestion);
                }
                
                // 질문 정제가 실패하거나 후속 질문이 아닌 경우 원래 질문을 사용한다.
                var questionEmbedding = await _embeddingService.EmbedTextAsync(refinedQuestion);
                var normalizedPlatform = NormalizePlatform(platform);
                var similarityThreshold = ResolveSimilarityThreshold(runtimeOptions);
                // role/platform 기반으로 "조회 가능한 KB 범위"를 먼저 제한한다.
                // 실제 유사도 계산은 벡터DB에서 하되, 후속 병합 시 이 범위를 다시 검증한다.
                var kbQuery = _context.KnowledgeBases.AsNoTracking().AsQueryable();
                if (role != "admin")
                {
                    kbQuery = kbQuery.Where(x => x.Visibility == "user");
                }

                // 플랫폼이 "공통"이면 공통만, "전체 플랫폼"이면 전체, 특정 플랫폼이면 공통+해당 플랫폼을 조회하도록 한다.
                if (string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
                {
                    kbQuery = kbQuery.Where(kb => kb.Platform.Contains("공통"));
                }
                else if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                {
                    kbQuery = kbQuery.Where(kb => kb.Platform.Contains("공통") || kb.Platform.Contains(normalizedPlatform));
                }


                // 1-1) 벡터 검색: 본문(document) + 예상질문(expected) 포인트를 함께 조회
                //      실패 시 예외를 전파하지 않고 빈 결과로 진행해 서비스 가용성을 우선한다.
                IReadOnlyList<VectorSearchHit> semanticHits;
                try
                {
                    semanticHits = await _vectorSearchService.SearchAsync(questionEmbedding, role, normalizedPlatform, RawVectorTopK);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 벡터 검색 실패. 빈 결과로 진행");
                    semanticHits = Array.Empty<VectorSearchHit>();
                }

                // 1-2) 벡터 검색 결과를 document/expected 포인트별로 상위 후보를 유지하면서 KB 단위로 병합한다.
                var documentVectorHits = semanticHits
                    .Where(x => !x.IsExpectedQuestion)
                    .OrderByDescending(x => x.Score)
                    .Take(DocumentVectorTopK)
                    .ToList();

                var expectedVectorHits = semanticHits
                    .Where(x => x.IsExpectedQuestion)
                    .OrderByDescending(x => x.Score)
                    .Take(ExpectedVectorTopK)
                    .ToList();

                // 두 소스의 top-k를 결합해 KB 후보군을 만든다.
                // (한쪽만 강한 KB가 누락되지 않도록 source별 컷을 따로 적용)
                var semanticTop = documentVectorHits.Concat(expectedVectorHits).ToList();

                // 2) 질문 키워드 추출 (키워드는 임베딩하지 않고 약한 가산/진단 용도로만 사용)
                var questionTokens = ExtractKeywordTokens(normalizedQuestion);

                // 2-1) KB별 대표 임베딩 점수와 키워드 매칭 정보를 결합해 최종 후보 점수를 매긴다.
                //      - KB별 대표 임베딩 점수는 document/expected 중 더 높은 쪽을 채택한다.
                //      - 키워드 매칭 정보는 KB 내용과 질문에서 추출한 토큰의 교집합 개수에 비례해 가산점으로 활용한다. (예: 매칭당 0.01점, 최대 0.1점)
                var candidateKbIds = semanticTop
                    .Select(x => x.KbId)
                    .Distinct()
                    .ToList();
                // 후보 KB 조회 범위는 role/platform으로 이미 제한된 상태이므로, 벡터 검색 결과에 대해서만 KB 정보를 추가로 조회한다.
                var kbById = await kbQuery
                    .Where(x => candidateKbIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id);

                var documentBestByKb = documentVectorHits
                    .GroupBy(x => x.KbId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Score).First());

                var expectedBestByKb = expectedVectorHits
                    .GroupBy(x => x.KbId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Score).First());

                var mergedCandidates = candidateKbIds
                    .Where(kbById.ContainsKey)
                    .Select(id =>
                    {
                        documentBestByKb.TryGetValue(id, out var bestDoc);
                        expectedBestByKb.TryGetValue(id, out var bestExpected);

                        // KB별 대표 semantic score는 document/expected 중 더 높은 쪽을 채택한다.
                        var bestSemantic = bestExpected != null && (bestDoc == null || bestExpected.Score >= bestDoc.Score)
                            ? bestExpected
                            : bestDoc;

                        var semanticScore = bestSemantic?.Score ?? 0f;
                        var matchedEvidenceText = bestSemantic?.MatchedEvidenceText
                            ?? kbById[id].Title
                            ?? string.Empty;
                        var isExpectedMatch = bestSemantic?.IsExpectedQuestion == true;

                        // 키워드 매칭 가산점 계산 (원래 점수가 임계치에서 너무 멀면 가산점이 의미 없으므로 0으로 처리)
                        var keywordInfo = BuildKeywordBoostInfo(kbById[id], questionTokens);
                        var keywordBoost = semanticScore >= similarityThreshold - KeywordBoostHardFloorGap
                            ? keywordInfo.Boost
                            : 0f;

                        return new MergedRetrievalCandidate
                        {
                            Kb = kbById[id], // KB 정보
                            MatchedEvidenceText = matchedEvidenceText, // 검색된 KB의 대표 텍스트 (매칭된 질문 또는 KB 제목)
                            IsExpectedMatch = isExpectedMatch, // 대표 텍스트가 예상질문에서 왔는지 여부
                            SemanticScore = semanticScore, // 벡터 검색에서 계산된 유사도 점수
                            KeywordScore = keywordBoost, // 키워드 매칭에 따른 가산점
                            FinalScore = semanticScore + keywordBoost, // 최종 점수는 벡터 유사도 + 키워드 가산점
                            IncludedByKeyword = keywordInfo.MatchCount > 0, // 이 후보가 키워드 매칭으로 가산점이 부여되었는지 여부
                            MatchedKeywords = keywordInfo.MatchedKeywords, // 매칭된 키워드 목록
                            KeywordMatchCount = keywordInfo.MatchCount // 매칭된 키워드 개수
                        };
                    })
                    .OrderByDescending(x => x.FinalScore) // 최종 점수 기준으로 후보 정렬 (벡터 유사도 + 키워드 가산점)
                    .ThenByDescending(x => x.SemanticScore) // 벡터 유사도 기준으로 후보 정렬
                    .Take(MergeTopK) // 병합 후 상위 MergeTopK개 후보를 유지한다.
                    .ToList();

                // 3) 상위 후보를 AI로 재정렬 후 답변 컨텍스트 후보를 확정
                //    고신뢰 후보(상위 점수가 임계치 이상이거나 2위와 격차가 충분히 크면) rerank LLM 호출 스킵
                var reranked = ShouldSkipRerank(mergedCandidates)
                    ? mergedCandidates.OrderByDescending(x => x.FinalScore).Take(ReRankTopK).ToList()
                    : await ReRankCandidatesAsync(question, mergedCandidates, ReRankTopK);

                // 최종 답변 생성 단계로 넘길 후보 집합
                var topResults = reranked
                    .Take(FinalTopK) // 최종 답변 컨텍스트에 사용할 후보 수 (5개로 제한)
                    .ToList();

                if (topResults.Count == 0)
                {
                    _logger.LogWarning("⚠️ KB 후보 없음");
                    return new RagResponse
                    {
                        Answer = "유사한 상담내역이 없습니다. 담당자에게 문의해주세요.",
                        TopSimilarity = 0f,
                        IsLowSimilarity = true,
                        DecisionRule = "후보 없음",
                        RelatedKBs = new List<KBSummary>()
                    };
                }

                _logger.LogInformation("✓ 검색 완료 (kb={Count})", topResults.Count);

                // 답변 생성에 사용하는 FAQ 후보는 임계치 이상 사례로 제한
                var eligibleResults = topResults
                    .Where(x => x.SemanticScore >= similarityThreshold)
                    .ToList();

                // 단순 정책: semantic 임계치 통과 후보 중 재정렬 순서 상위 3개를 선택
                var selectedResults = eligibleResults
                    .Take(AnswerContextTopK)
                    .Select(x => (kb: x.Kb, similarity: x.SemanticScore))
                    .ToList();
                var decisionRule = selectedResults.Count > 0
                    ? $"semantic 임계치 통과 후보 중 상위 {AnswerContextTopK}개 선택"
                    : "semantic 임계치 통과 후보 없음";
                var topResultSet = topResults.Select(x => x.Kb.Id).ToHashSet();
                var eligibleSet = eligibleResults.Select(x => x.Kb.Id).ToHashSet();
                var selectedSet = selectedResults.Select(x => x.kb.Id).ToHashSet();

                // RAG 진단 정보 구축: 검색된 후보 전체에 대한 상세 정보를 담는다.
                var retrievalDiagnostics = new RetrievalDiagnostics
                {
                    SimilarityThreshold = similarityThreshold,
                    QuestionTokens = questionTokens.OrderBy(x => x).ToList(),
                    Candidates = mergedCandidates
                        .Where(x => topResultSet.Contains(x.Kb.Id))
                        .OrderByDescending(x => x.FinalScore)
                        .Select(x => new RetrievalCandidateDiagnostic
                        {
                            Id = x.Kb.Id,
                            Title = x.Kb.Title, // 진단에서는 KB 제목도 함께 보여준다.
                            MatchedEvidenceText = x.MatchedEvidenceText, // 검색된 KB의 대표 텍스트 (매칭된 질문 또는 KB 제목)
                            BaseSimilarity = x.SemanticScore, // 벡터 검색에서 계산된 유사도 점수
                            KeywordBoost = x.KeywordScore, // 키워드 매칭에 따른 가산점
                            AdjustedSimilarity = x.FinalScore, // 최종 점수는 벡터 유사도 + 키워드 가산점
                            KeywordMatchCount = x.KeywordMatchCount, // 매칭된 키워드 개수
                            MatchedKeywords = x.MatchedKeywords, // 매칭된 키워드 목록
                            IncludedByKeyword = x.IncludedByKeyword, // 이 후보가 키워드 매칭으로 가산점이 부여되었는지 여부
                            PassedThreshold = eligibleSet.Contains(x.Kb.Id), // 임계치 통과 여부
                            SelectedForAnswer = selectedSet.Contains(x.Kb.Id) // 최종 답변 후보로 선택되었는지 여부
                        })
                        .ToList()
                };

                // View count 증가 (실제로 사용된 모든 KB에 대해 업데이트한다. 답변 컨텍스트에 포함된 KB뿐 아니라, 후보군에 오른 KB도 포함한다.)
                _logger.LogInformation("📊 View count 처리: DisablePersistence={DisablePersistence}, selectedResults={Count}", runtimeOptions.DisablePersistence, selectedResults.Count);
                if (!runtimeOptions.DisablePersistence && selectedResults.Count > 0)
                {
                    var kbIds = selectedResults.Select(x => x.kb.Id).ToList();
                    // EF Core ExecuteUpdateAsync: 파라미터 바인딩으로 SQL 인젝션 위험 없이 배치 업데이트
                    var updatedAt = DateTime.UtcNow;
                    await _context.KnowledgeBases
                        .Where(x => kbIds.Contains(x.Id))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.ViewCount, x => x.ViewCount + 1) // ViewCount를 1 증가시키고, UpdatedAt도 현재 시간으로 갱신한다.
                            .SetProperty(x => x.UpdatedAt, updatedAt));
                    _logger.LogInformation("✓ View count 증가: {Count}개 KB 업데이트 완료", kbIds.Count);
                }

                // 답변 생성
                // 관리자/사용자 role에 따라 BuildContext에서 노출 정보 레벨이 달라진다.
                var answerContextResults = selectedResults
                    .Select(x =>
                    {
                        var candidate = topResults.FirstOrDefault(r => r.Kb.Id == x.kb.Id);
                        return (
                            x.kb,
                            x.similarity,
                            candidate?.MatchedEvidenceText ?? x.kb.Title,
                            candidate?.IsExpectedMatch ?? false);
                    })
                    .ToList();

                var kbContext = BuildContext(answerContextResults, role);
                var contextText = kbContext;

                var topKbScore = topResults.Count > 0 ? topResults[0].SemanticScore : 0f;
                var topScore = topKbScore;
                var topMatchedEvidenceText = topResults.Count > 0 ? topResults[0].MatchedEvidenceText : null;
                var topKb = topResults.Count > 0 ? topResults[0].Kb : null;
                var topMatchedKbTitle = topKb?.Title;
                var topMatchedKbContent = topKb?.Content;

                var answer = await GenerateAnswerAsync(question, contextText, role, topScore, recentHistory, runtimeOptions);
                var isLowSimilarity = topScore < similarityThreshold;

                // 최종 응답에 검색된 후보와 RAG 실행 진단 정보를 함께 담아서 반환한다.
                return new RagResponse
                {
                    Answer = answer,
                    TopSimilarity = topScore,
                    IsLowSimilarity = isLowSimilarity,
                    TopMatchedEvidenceText = topMatchedEvidenceText,
                    TopMatchedKbTitle = topMatchedKbTitle,
                    TopMatchedKbContent = topMatchedKbContent,
                    ConflictDetected = false,
                    DecisionRule = decisionRule,
                    RetrievalDiagnostics = retrievalDiagnostics,
                    RelatedKBs = isLowSimilarity
                        ? new List<KBSummary>()
                        : eligibleResults
                        .Select(x => new KBSummary
                        {
                            Id = x.Kb.Id,
                            Title = x.Kb.Title,
                            Content = x.Kb.Content,
                            Similarity = x.SemanticScore,
                            MatchedEvidenceText = x.MatchedEvidenceText,
                            IsSelected = selectedSet.Contains(x.Kb.Id)
                        }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ RAG 오류: {Message}", ex.Message);
                throw;
            }
        }

        private static string NormalizePlatform(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "공통";
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return "공통";
            if (string.Equals(trimmed, "전체", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
                return "전체 플랫폼";
            if (string.Equals(trimmed, "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "공통", StringComparison.OrdinalIgnoreCase))
                return "공통";
            return trimmed.ToLowerInvariant();
        }

        private async Task<string> GenerateAnswerAsync(
            string question, // 사용자 질문
            string context, // 답변 생성에 사용할 KB 근거 텍스트 (최대 3개 KB의 제목/내용을 압축해서 담는다)
            string role, // 사용자 역할 (admin/user)
            float topScore, // 검색된 KB 중 최고 유사도 점수 (답변 생성 시 low similarity 판단에 사용)
            IList<(string Role, string Content)>? history = null,
            RagRuntimeOptions? runtimeOptions = null)
        {
            try
            {
                runtimeOptions ??= new RagRuntimeOptions();
                var similarityThreshold = ResolveSimilarityThreshold(runtimeOptions);
                var isLowSimilarity = topScore < similarityThreshold;

                // KB 근거가 부족하면 대화 맥락으로 추정하지 않고 즉시 안내문 반환
                if (isLowSimilarity)
                {
                    return ResolveLowSimilarityMessage(role, runtimeOptions);
                }

                var systemPrompt = ResolveSystemPrompt(role, runtimeOptions);

                var messages = new List<object>
                {
                    new { role = "system", content = systemPrompt }
                };

                // 이전 대화 이력 삽입 (user/bot → user/assistant)
                if (history != null && history.Count > 0)
                {
                    foreach (var (histRole, histContent) in GetRecentHistoryMessages(history))
                    {
                        var gptRole = histRole == "bot" ? "assistant" : histRole;
                        messages.Add(new { role = gptRole, content = histContent });
                    }
                }

                string prompt;
                if (isLowSimilarity)
                {
                    prompt = question;
                }
                else
                {
                    var userRules = ResolveRulesPrompt(role, runtimeOptions);
                    prompt = $@"{context}

【사용자 질문】
{question}

답변 규칙:
{userRules}";
                }

                messages.Add(new { role = "user", content = prompt });

                var request = new
                {
                    model = _chatModel,
                    messages,
                    temperature = _chatTemperature,
                    max_tokens = _chatMaxTokens
                };

                var response = await _httpClient.PostAsJsonAsync(
                    _chatCompletionsEndpoint,
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("답변 생성 실패: {Error}", error);
                    throw new Exception("GPT 답변 생성 실패");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(jsonString).RootElement;
                var answer = json
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                _logger.LogInformation("✓ 답변 생성 완료");
                return string.IsNullOrWhiteSpace(answer)
                    ? "답변을 생성할 수 없습니다."
                    : SanitizeAnswerMarkdown(answer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "답변 생성 오류: {Message}", ex.Message);
                throw;
            }
        }

        private float ResolveSimilarityThreshold(RagRuntimeOptions options)
        {
            if (options.SimilarityThresholdOverride.HasValue)
            {
                var v = options.SimilarityThresholdOverride.Value;
                return Math.Clamp(v, 0.1f, 0.95f);
            }

            return _promptTemplates.SimilarityThreshold;
        }

        private static List<(string Role, string Content)> GetRecentHistoryMessages(IList<(string Role, string Content)>? history)
        {
            if (history == null || history.Count == 0)
            {
                return new List<(string Role, string Content)>();
            }

            return history
                .TakeLast(PromptHistoryMessageLimit)
                .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                .Select(x => (x.Role, x.Content.Trim()))
                .ToList();
        }

        private string ResolveSystemPrompt(string role, RagRuntimeOptions options)
        {
            return string.IsNullOrWhiteSpace(options.SystemPromptOverride)
                ? (role == "admin" ? _promptTemplates.AdminSystemPrompt : _promptTemplates.UserSystemPrompt)
                : options.SystemPromptOverride;
        }

        private string ResolveRulesPrompt(string role, RagRuntimeOptions options)
        {
            return string.IsNullOrWhiteSpace(options.RulesPromptOverride)
                ? (role == "admin" ? _promptTemplates.AdminRulesPrompt : _promptTemplates.UserRulesPrompt)
                : options.RulesPromptOverride;
        }

        private string ResolveLowSimilarityMessage(string role, RagRuntimeOptions options)
        {
            return string.IsNullOrWhiteSpace(options.LowSimilarityMessageOverride)
                ? (role == "admin" ? _promptTemplates.AdminLowSimilarityMessage : _promptTemplates.UserLowSimilarityMessage)
                : options.LowSimilarityMessageOverride;
        }

        private string BuildContext(List<(KnowledgeBase kb, float similarity, string matchedEvidenceText, bool matchedExpected)> results, string role)
        {
            if (role != "admin")
            {
                return BuildUserContext(results);
            }

            var sb = new StringBuilder();
            sb.AppendLine("【관련된 과거 상담 사례】");
            for (int i = 0; i < results.Count; i++)
            {
                var (kb, similarity, matchedEvidenceText, matchedExpected) = results[i];
                sb.AppendLine($"\n{i + 1}. 유사도: {similarity:P0}");
                sb.AppendLine($"   매칭 근거: {matchedEvidenceText} {(matchedExpected ? "(예상질문)" : "(제목/내용)")}");
                sb.AppendLine($"   제목: {kb.Title}");
                sb.AppendLine($"   해결: {kb.Content}");
            }

            return sb.ToString();
        }

        private string BuildUserContext(List<(KnowledgeBase kb, float similarity, string matchedEvidenceText, bool matchedExpected)> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【답변 후보 정보(우선순위 순)】");

            for (int i = 0; i < results.Count; i++)
            {
                var (kb, _, matchedEvidenceText, matchedExpected) = results[i];
                sb.AppendLine($"\n우선순위 {i + 1}");
                sb.AppendLine($"- 매칭된 근거: {matchedEvidenceText} {(matchedExpected ? "(예상질문)" : "(제목/내용)")}");
                sb.AppendLine($"- KB 제목: {kb.Title}");
                sb.AppendLine($"- 권장 안내: {kb.Content}");
            }

            sb.AppendLine("\n※ 주의: 답변에는 우선순위, 유사도, 사례/내역 건수 같은 내부 기준을 노출하지 말 것.");
            return sb.ToString();
        }

        private static string TruncateText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var text = value.Trim();
            if (text.Length <= maxLength) return text;
            return text[..maxLength] + "...";
        }

        private static string NormalizeQueryForEmbedding(string raw)
        {
            return EmbeddingTextNormalizer.NormalizeForEmbedding(raw);
        }

        private static string SanitizeAnswerMarkdown(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
            {
                return string.Empty;
            }

            var sanitized = answer.Replace("**", string.Empty)
                .Replace("__", string.Empty)
                .Replace("`", string.Empty);

            sanitized = Regex.Replace(sanitized, @"^#{1,6}\s*", string.Empty, RegexOptions.Multiline);
            sanitized = Regex.Replace(sanitized, @"\[(.*?)\]\((.*?)\)", "$1");

            return sanitized.Trim();
        }

        private static bool ShouldUseHistoryForEmbedding(string question, IList<(string Role, string Content)> recentHistory)
        {
            if (string.IsNullOrWhiteSpace(question) || recentHistory.Count == 0)
            {
                return false;
            }

            var normalizedQuestion = NormalizeQueryForEmbedding(question);
            if (string.IsNullOrWhiteSpace(normalizedQuestion))
            {
                return false;
            }

            // 키워드가 충분하거나 길이가 긴 질문은 독립 질의로 판단해 히스토리 결합을 피한다.
            var keywordTokens = ExtractKeywordTokens(normalizedQuestion);
            if (keywordTokens.Count >= 4 || normalizedQuestion.Length >= 30)
            {
                return false;
            }

            if (FollowUpQuestionPrefixes.Any(prefix => normalizedQuestion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return keywordTokens.Count <= 2;
            }

            var hasReferencePronoun = Regex.IsMatch(normalizedQuestion, "(그거|이거|저거|그건|이건|저건|그렇다면|이렇다면|그 부분|이 부분|저 부분|해당 건|이전 내용|위 내용)");
            if (hasReferencePronoun && keywordTokens.Count <= 2)
            {
                return true;
            }

            var lastUserTurn = recentHistory
                .Where(x => string.Equals(x.Role, "user", StringComparison.OrdinalIgnoreCase))
                .Select(x => NormalizeQueryForEmbedding(x.Content))
                .LastOrDefault();

            // 참조어가 없으면 직전 사용자 질문과 토큰이 일부라도 맞닿는 경우에만 히스토리를 결합한다.
            if (!string.IsNullOrWhiteSpace(lastUserTurn) && keywordTokens.Count > 0)
            {
                var previousTokens = ExtractKeywordTokens(lastUserTurn);
                if (previousTokens.Count > 0)
                {
                    var overlap = keywordTokens.Count(token => previousTokens.Contains(token));
                    if (overlap == 0)
                    {
                        return false;
                    }
                }
            }

            var looksLikeShortEllipticQuestion = normalizedQuestion.Length <= 14
                && keywordTokens.Count <= 2
                && Regex.IsMatch(normalizedQuestion, "(어떻게|어떡해|가능|불가|안돼|안보여|돼|되나|되요|되나요|취소|변경|수정|언제|왜|맞아|맞나요)");

            return looksLikeShortEllipticQuestion;
        }

        private float CosineSimilarity(float[] vec1, float[] vec2)
        {
            var length = Math.Min(vec1.Length, vec2.Length);
            if (length == 0)
            {
                return 0;
            }

            float dotProduct = 0, normA = 0, normB = 0;
            for (int i = 0; i < length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                normA += vec1[i] * vec1[i];
                normB += vec2[i] * vec2[i];
            }
            return normA == 0 || normB == 0 ? 0 : (float)(dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB)));
        }

        private static HashSet<string> ParseKeywordTokens(string rawKeywords)
        {
            return rawKeywords
                .Split(new[] { ',', ';', '|', '/', '#' }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(ExtractKeywordTokens)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // 키워드 매칭 정보 계산: KB의 제목/키워드/본문과 질문에서 추출한 토큰의 교집합 개수에 비례해 가산점으로 활용한다.
        private KeywordBoostInfo BuildKeywordBoostInfo(KnowledgeBase kb, HashSet<string> questionTokens)
        {
            if (questionTokens.Count == 0)
            {
                return new KeywordBoostInfo();
            }

            var titleTokens = string.IsNullOrWhiteSpace(kb.Title)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : ExtractKeywordTokens(kb.Title);
            var keywordTokens = string.IsNullOrWhiteSpace(kb.Keywords)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : ParseKeywordTokens(kb.Keywords);
            var contentTokens = string.IsNullOrWhiteSpace(kb.Content)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : ExtractKeywordTokens(TruncateText(kb.Content, 2000));

            var matched = questionTokens
                .Where(token => titleTokens.Contains(token) || keywordTokens.Contains(token) || contentTokens.Contains(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            if (matched.Count == 0)
            {
                return new KeywordBoostInfo();
            }

            var boost = Math.Min(matched.Count * KeywordBoostPerMatch, MaxKeywordBoost);

            return new KeywordBoostInfo
            {
                Boost = boost,
                MatchCount = matched.Count,
                MatchedKeywords = matched
            };
        }

        private static HashSet<string> ExtractKeywordTokens(string text)
        {
            var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "문의", "관련", "처리", "요청", "문제", "해결", "오류", "에러", "이슈", "안내"
            };

            return Regex.Matches(text, "[\\p{L}\\p{Nd}]{2,}")
                .Select(m => m.Value.Trim().ToLowerInvariant())
                .Where(token => !stopwords.Contains(token))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipRerank(List<MergedRetrievalCandidate> candidates)
        {
            if (candidates.Count <= 1) return true;

            var sorted = candidates.OrderByDescending(x => x.FinalScore).ToList();
            var top = sorted[0].FinalScore;
            var second = sorted[1].FinalScore;

            // 상위 후보가 충분히 높으면 스킵
            if (top >= ReRankSkipScoreThreshold) return true;

            // 1위와 2위 격차가 충분히 크면 스킵
            if (top - second >= ReRankSkipGapThreshold) return true;

            return false;
        }

        /// <summary>
        /// 대화이력을 기반으로 현재 질문을 정제된 단일 질문으로 변환합니다.
        /// LLM이 이전 대화 맥락을 파악해 축약된 후속 질문을 완전한 문장으로 복원합니다.
        /// </summary>
        private async Task<string> RefineQuestionWithLlmAsync(
            string currentQuestion,
            IList<(string Role, string Content)> recentHistory)
        {
            try
            {
                if (recentHistory == null || recentHistory.Count == 0)
                {
                    return NormalizeQueryForEmbedding(currentQuestion);
                }

                var messages = new List<object>
                {
                    new { role = "system", content = @"당신은 고객 지원 챗봇의 질문 정제기입니다.
사용자의 현재 질문과 이전 대화 이력을 보고, 현재 질문의 의도를 명확한 독립 질문으로 변환하세요.

규칙:
1. 축약·생략된 질문(예: '그럼 안돼?', '어디서 받아?')을 완전한 문장으로 복원
2. 이전 대화의 핵심 주제어(예: '중복 결제', '인증서', 'OTP', '비밀번호' 등)를 반드시 정제 질문에 포함
3. 현재 질문이 이전 주제의 후속이면, [이전 핵심 주제] + [현재 행동/요청]을 결합 (예: '중복 결제 환불은 어떻게 받나요?')
4. 핵심 주제어는 절대 제거하지 말 것 — 불필요한 감탄사·접속사만 제거
5. 이전 대화와 현재 질문의 주제가 명확히 다르면, 이전 대화를 섞지 말고 현재 질문만 자연스럽게 정리해 반환
6. 한 문장의 명확한 질문만 출력, 부연 설명 없이" }
                };

                // 최근 대화이력 추가 (RefineHistoryMessageLimit 기준)
                foreach (var (role, content) in recentHistory.TakeLast(RefineHistoryMessageLimit))
                {
                    var gptRole = role == "bot" ? "assistant" : role;
                    messages.Add(new { role = gptRole, content = content });
                }

                // 현재 질문
                messages.Add(new { role = "user", content = $"현재 질문: {currentQuestion}\n\n위 질문을 정제해 하나의 명확한 질문으로 변환해주세요." });

                var request = new
                {
                    model = _chatModel,
                    messages,
                    temperature = 0.3,
                    max_tokens = 100
                };

                var response = await _httpClient.PostAsJsonAsync(
                    _chatCompletionsEndpoint,
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("⚠️ 질문 정제 LLM 호출 실패, 원본 사용");
                    return NormalizeQueryForEmbedding(currentQuestion);
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(jsonString).RootElement;
                var refinedQuestion = json
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(refinedQuestion))
                {
                    return NormalizeQueryForEmbedding(currentQuestion);
                }

                // 정제 결과를 정규화
                return NormalizeQueryForEmbedding(refinedQuestion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ 질문 정제 중 오류, 원본 사용");
                return NormalizeQueryForEmbedding(currentQuestion);
            }
        }

        private async Task<List<MergedRetrievalCandidate>> ReRankCandidatesAsync(
            string question,
            List<MergedRetrievalCandidate> candidates,
            int topK)
        {
            if (candidates.Count <= 1)
            {
                return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
            }

            var candidateLines = candidates.Select((x, idx) =>
                $"{idx + 1}. id={x.Kb.Id}, title={x.Kb.Title}, semantic={x.SemanticScore:F3}, final={x.FinalScore:F3}, matched={x.MatchedEvidenceText}, source={(x.IsExpectedMatch ? "expected" : "document")}, content={TruncateText(x.Kb.Content, 180)}");

            var prompt = $@"사용자 질문과 관련성 순으로 후보를 재정렬하세요.

질문:
{question}

후보:
{string.Join("\n", candidateLines)}

규칙:
- 반드시 JSON 배열만 응답
- 배열 요소는 후보 id(int)
- 관련성 높은 순서로 최대 {topK}개 반환
- 근거가 약한 후보는 제외 가능";

            var request = new
            {
                model = _chatModel,
                messages = new[]
                {
                    new { role = "system", content = "당신은 RAG 검색 재정렬기입니다. 반드시 JSON 정수 배열만 응답하세요." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.1,
                max_tokens = 180
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(_chatCompletionsEndpoint, request);
                if (!response.IsSuccessStatusCode)
                {
                    return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
                }

                var raw = await response.Content.ReadAsStringAsync();
                var root = JsonDocument.Parse(raw).RootElement;
                var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                var rankedIds = ParseIdArray(text, topK);

                if (rankedIds.Count == 0)
                {
                    return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
                }

                var candidateById = candidates.ToDictionary(x => x.Kb.Id);
                var reranked = rankedIds
                    .Where(candidateById.ContainsKey)
                    .Select(id => candidateById[id])
                    .ToList();

                if (reranked.Count < topK)
                {
                    var fill = candidates
                        .Where(x => !rankedIds.Contains(x.Kb.Id))
                        .OrderByDescending(x => x.FinalScore)
                        .Take(topK - reranked.Count);
                    reranked.AddRange(fill);
                }

                return reranked.Take(topK).ToList();
            }
            catch
            {
                return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
            }
        }

        private static List<int> ParseIdArray(string? raw, int maxCount)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<int>();

            var trimmed = raw.Trim();
            var jsonStart = trimmed.IndexOf('[');
            var jsonEnd = trimmed.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return new List<int>();
            }

            try
            {
                var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var ids = JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
                return ids.Distinct().Take(maxCount).ToList();
            }
            catch
            {
                return new List<int>();
            }
        }

        // 제목/키워드 매칭으로 계산한 가산점 결과
        private sealed class KeywordBoostInfo
        {
            // 최종 점수에 더할 가산점 값
            public float Boost { get; init; }
            // 매칭된 키워드 개수
            public int MatchCount { get; init; }
            // 실제로 매칭된 키워드 목록(진단용)
            public List<string> MatchedKeywords { get; init; } = new();
        }

        // document/expected 검색 결과를 KB 단위로 병합한 후보
        private sealed class MergedRetrievalCandidate
        {
            // 후보 KB 원문
            public KnowledgeBase Kb { get; init; } = default!;
            // 매칭 근거로 사용된 질문/제목
            public string MatchedEvidenceText { get; init; } = string.Empty;
            // true면 expected 포인트 기반 매칭
            public bool IsExpectedMatch { get; init; }
            // 벡터 유사도 기반 점수
            public float SemanticScore { get; init; }
            // 키워드 매칭 가산점
            public float KeywordScore { get; init; }
            // semantic + keyword 합산 최종 점수
            public float FinalScore { get; init; }
            // 키워드 매칭으로 포함 근거가 있는지 여부
            public bool IncludedByKeyword { get; init; }
            // 매칭된 키워드 수
            public int KeywordMatchCount { get; init; }
            // 매칭된 키워드 목록
            public List<string> MatchedKeywords { get; init; } = new();
        }

        private float[]? ParseEmbedding(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    return ParseNumberArray(root);
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (TryGetArrayProperty(root, "embedding", out var embeddingArray))
                {
                    return ParseNumberArray(embeddingArray);
                }

                if (TryGetArrayProperty(root, "values", out var valuesArray))
                {
                    return ParseNumberArray(valuesArray);
                }

                if (TryGetArrayProperty(root, "vector", out var vectorArray))
                {
                    return ParseNumberArray(vectorArray);
                }

                if (root.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array
                    && data.GetArrayLength() > 0)
                {
                    var first = data[0];
                    if (TryGetArrayProperty(first, "embedding", out var dataEmbeddingArray))
                    {
                        return ParseNumberArray(dataEmbeddingArray);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetArrayProperty(JsonElement obj, string propertyName, out JsonElement arrayElement)
        {
            if (obj.ValueKind == JsonValueKind.Object
                && obj.TryGetProperty(propertyName, out var element)
                && element.ValueKind == JsonValueKind.Array)
            {
                arrayElement = element;
                return true;
            }

            arrayElement = default;
            return false;
        }

        private static float[]? ParseNumberArray(JsonElement arrayElement)
        {
            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<float>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (item.TryGetSingle(out var singleValue))
                {
                    result.Add(singleValue);
                }
                else
                {
                    result.Add((float)item.GetDouble());
                }
            }

            return result.Count == 0 ? null : result.ToArray();
        }
    }

    /// <summary>
    /// RAG 질의 1회 실행 결과를 API로 반환하는 응답 모델입니다.
    /// </summary>
    public class RagResponse
    {
        // 최종 사용자 답변
        public string Answer { get; set; } = string.Empty;
        // 최상위 후보의 최종 유사도 점수
        public float TopSimilarity { get; set; }
        // 임계치 미달 여부
        public bool IsLowSimilarity { get; set; }
        // 최상위 후보의 매칭 질문(제목/예상질문)
        public string? TopMatchedEvidenceText { get; set; }
        // 최상위 후보 KB 제목
        public string? TopMatchedKbTitle { get; set; }
        // 최상위 후보 KB 본문
        public string? TopMatchedKbContent { get; set; }
        // 상충 후보 감지 여부(확장 포인트)
        public bool ConflictDetected { get; set; }
        // 후보 선택 정책 설명 문자열
        public string? DecisionRule { get; set; }
        // 검색/선정 상세 진단 정보
        public RetrievalDiagnostics? RetrievalDiagnostics { get; set; }
        // 관련 KB 목록(임계치 통과 후보 중심)
        public List<KBSummary> RelatedKBs { get; set; } = new();
    }

    /// <summary>
    /// 검색 단계의 내부 진단 데이터를 담는 모델입니다.
    /// </summary>
    public class RetrievalDiagnostics
    {
        // 적용된 유사도 임계치
        public float SimilarityThreshold { get; set; }
        // 질문에서 추출한 키워드 토큰
        public List<string> QuestionTokens { get; set; } = new();
        // 후보별 점수/선택 여부 상세
        public List<RetrievalCandidateDiagnostic> Candidates { get; set; } = new();
    }

    /// <summary>
    /// 개별 후보의 점수 구성과 통과 여부를 나타냅니다.
    /// </summary>
    public class RetrievalCandidateDiagnostic
    {
        // KB 식별자
        public int Id { get; set; }
        // KB 제목
        public string? Title { get; set; }
        // 매칭 근거 텍스트
        public string? MatchedEvidenceText { get; set; }
        // 벡터 검색 원점수
        public float BaseSimilarity { get; set; }
        // 키워드 가산점
        public float KeywordBoost { get; set; }
        // 최종 점수(Base + Boost)
        public float AdjustedSimilarity { get; set; }
        // 매칭 키워드 수
        public int KeywordMatchCount { get; set; }
        // 매칭 키워드 목록
        public List<string> MatchedKeywords { get; set; } = new();
        // 키워드 매칭으로 포함 근거가 있는지 여부
        public bool IncludedByKeyword { get; set; }
        // 임계치 통과 여부
        public bool PassedThreshold { get; set; }
        // 최종 답변 컨텍스트 선택 여부
        public bool SelectedForAnswer { get; set; }
    }

    /// <summary>
    /// 응답에 노출할 KB 요약 정보입니다.
    /// </summary>
    public class KBSummary
    {
        // KB 식별자
        public int Id { get; set; }
        // KB 제목
        public string? Title { get; set; }
        // KB 본문(요약 없이 원문 전달)
        public string? Content { get; set; }
        // 해당 KB의 유사도 점수
        public float Similarity { get; set; }
        // 매칭 근거 질문
        public string? MatchedEvidenceText { get; set; }
        // 답변 컨텍스트에 실제 선택됐는지 여부
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// RAG 실행 시 동작을 제어하는 런타임 옵션입니다.
    /// </summary>
    public class RagRuntimeOptions
    {
        // true면 조회수 증가/저장 같은 영속 작업을 생략
        public bool DisablePersistence { get; set; }
        // 시스템 프롬프트 강제 덮어쓰기
        public string? SystemPromptOverride { get; set; }
        // 답변 규칙 프롬프트 강제 덮어쓰기
        public string? RulesPromptOverride { get; set; }
        // 저유사도 fallback 안내문 강제 덮어쓰기
        public string? LowSimilarityMessageOverride { get; set; }
        // 유사도 임계치 강제 덮어쓰기
        public float? SimilarityThresholdOverride { get; set; }
    }
}

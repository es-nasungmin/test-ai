namespace AiDeskApi.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        // "user" 또는 "bot"
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // 답변 생성에 참조된 KB ID 목록 (JSON 배열)
        public string? RelatedKbIds { get; set; }
        // 답변 생성에 참조된 KB 메타 (JSON 배열: [{id, similarity}])
        public string? RelatedKbMeta { get; set; }
        // 답변 생성에 참조된 문서 KB 메타 (JSON 배열: [{documentId, documentName, pageNumber, similarity, excerpt}])
        public string? RelatedDocumentMeta { get; set; }
        // 유사도/키워드 추출 근거 모니터링용 JSON
        public string? RetrievalDebugMeta { get; set; }
        public float? TopSimilarity { get; set; }
        public bool IsLowSimilarity { get; set; } = false;

        public ChatSession? Session { get; set; }
    }
}

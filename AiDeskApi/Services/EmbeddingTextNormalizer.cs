using System.Text.RegularExpressions;

namespace AiDeskApi.Services
{
    /// <summary>
    /// 임베딩 전 텍스트를 정규화해 표현 흔들림으로 인한 벡터 분산을 줄입니다.
    /// </summary>
    internal static class EmbeddingTextNormalizer
    {
        public static string NormalizeForEmbedding(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var normalized = raw.Trim().ToLowerInvariant();
            normalized = Regex.Replace(normalized, "[\\s]+", " ");

            // 의미가 같은 표현을 통일해 검색 적중률을 높인다.
            normalized = Regex.Replace(normalized, "안\\s*됨|안\\s*돼요|안\\s*되요|안\\s*됩니다|안\\s*되는", "안돼");
            normalized = Regex.Replace(normalized, "불가|조회\\s*불가|확인\\s*불가", "안돼");
            normalized = Regex.Replace(normalized, "안\\s*보임|안\\s*보여요|안\\s*보입니다", "안보여");

            return normalized;
        }
    }
}
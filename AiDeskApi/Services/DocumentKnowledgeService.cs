using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using AiDeskApi.Data;
using AiDeskApi.Models;

namespace AiDeskApi.Services
{
    public interface IDocumentKnowledgeService
    {
        Task<DocumentKnowledgeUploadResult> UploadPdfAsync(
            Stream stream,
            string fileName,
            string displayName,
            string visibility,
            string platform,
            string actor,
            CancellationToken cancellationToken = default);

        Task<List<DocumentChunkSearchHit>> SearchChunksAsync(
            float[] questionEmbedding,
            string role,
            string platform,
            int topK,
            CancellationToken cancellationToken = default);

        Task<List<DocumentKnowledgeListItem>> ListAsync(string role, string? platform = null, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int documentId, CancellationToken cancellationToken = default);

        Task<DocumentKnowledgeListItem?> UpdateAsync(
            int documentId,
            string? displayName,
            string? visibility,
            string? platform,
            string actor,
            CancellationToken cancellationToken = default);

        Task<DocumentKnowledgeUploadResult> ReindexAsync(
            int documentId,
            Stream stream,
            string actor,
            CancellationToken cancellationToken = default);

        Task<DocumentDownloadInfo?> GetDownloadInfoAsync(int documentId, CancellationToken cancellationToken = default);
    }

    public sealed class DocumentKnowledgeService : IDocumentKnowledgeService
    {
        private readonly AiDeskContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DocumentKnowledgeService> _logger;

        public DocumentKnowledgeService(
            AiDeskContext context,
            IEmbeddingService embeddingService,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<DocumentKnowledgeService> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public async Task<DocumentKnowledgeUploadResult> UploadPdfAsync(
            Stream stream,
            string fileName,
            string displayName,
            string visibility,
            string platform,
            string actor,
            CancellationToken cancellationToken = default)
        {
            var normalizedVisibility = NormalizeVisibility(visibility);
            var normalizedPlatform = NormalizePlatform(platform);
            var now = DateTime.UtcNow;

            var pdfBytes = await ReadAllBytesAsync(stream, cancellationToken);
            if (pdfBytes.Length == 0)
            {
                throw new InvalidOperationException("업로드된 PDF 파일이 비어 있습니다.");
            }

            var preparedChunks = await PrepareChunkPayloadsAsync(pdfBytes, cancellationToken);

            var doc = new DocumentKnowledge
            {
                FileName = fileName,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? fileName : displayName.Trim(),
                Visibility = normalizedVisibility,
                Platform = normalizedPlatform,
                Status = "indexing",
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actor,
                UpdatedBy = actor
            };

            _context.DocumentKnowledges.Add(doc);
            await _context.SaveChangesAsync(cancellationToken);

            var chunkEntities = preparedChunks
                .Select((c, i) => new DocumentKnowledgeChunk
                {
                    DocumentKnowledgeId = doc.Id,
                    PageNumber = c.PageNumber,
                    ChunkOrder = i,
                    Content = c.Text,
                    ContentEmbedding = c.EmbeddingJson,
                    CreatedAt = now
                })
                .ToList();

            _context.DocumentKnowledgeChunks.AddRange(chunkEntities);
            doc.Status = "ready";
            doc.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await SaveOriginalPdfAsync(doc.Id, pdfBytes, cancellationToken);

            _logger.LogInformation("문서 인덱싱 완료. docId={DocId}, chunks={ChunkCount}", doc.Id, chunkEntities.Count);

            return new DocumentKnowledgeUploadResult
            {
                DocumentId = doc.Id,
                ChunkCount = chunkEntities.Count,
                DisplayName = doc.DisplayName,
                Status = doc.Status
            };
        }

        public async Task<List<DocumentChunkSearchHit>> SearchChunksAsync(
            float[] questionEmbedding,
            string role,
            string platform,
            int topK,
            CancellationToken cancellationToken = default)
        {
            if (questionEmbedding.Length == 0 || topK <= 0)
            {
                return new List<DocumentChunkSearchHit>();
            }

            var normalizedPlatform = NormalizePlatform(platform);
            var query = _context.DocumentKnowledgeChunks
                .AsNoTracking()
                .Include(x => x.DocumentKnowledge)
                .Where(x => x.DocumentKnowledge.Status == "ready");

            if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.DocumentKnowledge.Visibility == "user");
            }

            if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.DocumentKnowledge.Platform.Contains("공통") || x.DocumentKnowledge.Platform.Contains(normalizedPlatform));
            }

            var maxCandidates = Math.Clamp(_configuration.GetValue<int?>("Rag:DocumentMaxCandidates") ?? 1200, 200, 5000);
            var rows = await query
                .OrderByDescending(x => x.DocumentKnowledge.UpdatedAt)
                .ThenBy(x => x.DocumentKnowledgeId)
                .ThenBy(x => x.ChunkOrder)
                .Take(maxCandidates)
                .ToListAsync(cancellationToken);

            var results = rows
                .Select(chunk =>
                {
                    var embedding = ParseEmbedding(chunk.ContentEmbedding);
                    if (embedding == null) return null;

                    var baseSimilarity = CosineSimilarity(questionEmbedding, embedding);
                    var score = Math.Clamp(baseSimilarity, 0f, 1f);

                    return new DocumentChunkSearchHit
                    {
                        DocumentId = chunk.DocumentKnowledgeId,
                        ChunkId = chunk.Id,
                        DocumentName = chunk.DocumentKnowledge.DisplayName,
                        PageNumber = chunk.PageNumber,
                        Content = chunk.Content,
                        BaseSimilarity = baseSimilarity,
                        Score = score
                    };
                })
                .Where(x => x != null)
                .Select(x => x!)
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .ToList();

            return results;
        }

        public async Task<List<DocumentKnowledgeListItem>> ListAsync(string role, string? platform = null, CancellationToken cancellationToken = default)
        {
            var normalizedPlatform = NormalizePlatform(platform);
            var query = _context.DocumentKnowledges.AsNoTracking().AsQueryable();

            if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.Visibility == "user");
            }

            if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.Platform.Contains("공통") || x.Platform.Contains(normalizedPlatform));
            }

            return await query
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new DocumentKnowledgeListItem
                {
                    Id = x.Id,
                    DisplayName = x.DisplayName,
                    FileName = x.FileName,
                    Status = x.Status,
                    Visibility = x.Visibility,
                    Platform = x.Platform,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> DeleteAsync(int documentId, CancellationToken cancellationToken = default)
        {
            var doc = await _context.DocumentKnowledges
                .Include(x => x.Chunks)
                .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

            if (doc == null) return false;

            _context.DocumentKnowledgeChunks.RemoveRange(doc.Chunks);
            _context.DocumentKnowledges.Remove(doc);
            await _context.SaveChangesAsync(cancellationToken);

            DeleteOriginalPdfIfExists(documentId);

            _logger.LogInformation("문서 삭제 완료. docId={DocId}", documentId);
            return true;
        }

        public async Task<DocumentKnowledgeListItem?> UpdateAsync(
            int documentId,
            string? displayName,
            string? visibility,
            string? platform,
            string actor,
            CancellationToken cancellationToken = default)
        {
            var doc = await _context.DocumentKnowledges
                .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

            if (doc == null)
            {
                return null;
            }

            if (displayName != null)
            {
                var normalizedDisplayName = displayName.Trim();
                if (string.IsNullOrWhiteSpace(normalizedDisplayName))
                {
                    throw new InvalidOperationException("표시 이름은 비워둘 수 없습니다.");
                }

                doc.DisplayName = normalizedDisplayName;
            }

            if (visibility != null)
            {
                doc.Visibility = NormalizeVisibility(visibility);
            }

            if (platform != null)
            {
                doc.Platform = NormalizePlatform(platform);
            }

            doc.UpdatedAt = DateTime.UtcNow;
            doc.UpdatedBy = string.IsNullOrWhiteSpace(actor) ? "알 수 없음" : actor.Trim();

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("문서 메타데이터 수정 완료. docId={DocId}", documentId);

            return new DocumentKnowledgeListItem
            {
                Id = doc.Id,
                DisplayName = doc.DisplayName,
                FileName = doc.FileName,
                Status = doc.Status,
                Visibility = doc.Visibility,
                Platform = doc.Platform,
                UpdatedAt = doc.UpdatedAt
            };
        }

        public async Task<DocumentKnowledgeUploadResult> ReindexAsync(
            int documentId,
            Stream stream,
            string actor,
            CancellationToken cancellationToken = default)
        {
            var doc = await _context.DocumentKnowledges
                .Include(x => x.Chunks)
                .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken)
                ?? throw new InvalidOperationException($"문서를 찾을 수 없습니다. id={documentId}");

            // 기존 인덱스를 유지하기 위해 새 텍스트/임베딩 준비가 완료된 뒤 교체한다.
            var pdfBytes = await ReadAllBytesAsync(stream, cancellationToken);
            if (pdfBytes.Length == 0)
            {
                throw new InvalidOperationException("교체할 PDF 파일이 비어 있습니다.");
            }

            var preparedChunks = await PrepareChunkPayloadsAsync(pdfBytes, cancellationToken);

            var now = DateTime.UtcNow;
            _context.DocumentKnowledgeChunks.RemoveRange(doc.Chunks);

            var chunkEntities = preparedChunks
                .Select((c, i) => new DocumentKnowledgeChunk
                {
                    DocumentKnowledgeId = doc.Id,
                    PageNumber = c.PageNumber,
                    ChunkOrder = i,
                    Content = c.Text,
                    ContentEmbedding = c.EmbeddingJson,
                    CreatedAt = now
                })
                .ToList();

            _context.DocumentKnowledgeChunks.AddRange(chunkEntities);
            doc.Status = "ready";
            doc.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await SaveOriginalPdfAsync(doc.Id, pdfBytes, cancellationToken);

            _logger.LogInformation("문서 재인덱싱 완료. docId={DocId}, chunks={ChunkCount}", doc.Id, chunkEntities.Count);

            return new DocumentKnowledgeUploadResult
            {
                DocumentId = doc.Id,
                ChunkCount = chunkEntities.Count,
                DisplayName = doc.DisplayName,
                Status = doc.Status
            };
        }

        public async Task<DocumentDownloadInfo?> GetDownloadInfoAsync(int documentId, CancellationToken cancellationToken = default)
        {
            var doc = await _context.DocumentKnowledges
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

            if (doc == null)
            {
                return null;
            }

            var filePath = BuildStoredPdfPath(documentId);
            if (!File.Exists(filePath))
            {
                return null;
            }

            var baseName = string.IsNullOrWhiteSpace(doc.DisplayName) ? doc.FileName : doc.DisplayName;
            if (!baseName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                baseName += ".pdf";
            }

            return new DocumentDownloadInfo
            {
                DocumentId = doc.Id,
                FilePath = filePath,
                DownloadFileName = baseName
            };
        }

        private async Task<List<PreparedChunkPayload>> PrepareChunkPayloadsAsync(
            byte[] pdfBytes,
            CancellationToken cancellationToken)
        {
            string fullText;
            int pageCount;
            int extractedPages;
            bool usedLetterFallback;
            bool usedPdfToTextFallback = false;
            bool usedOcrFallback = false;

            using (var textStream = new MemoryStream(pdfBytes, writable: false))
            {
                fullText = ExtractPdfText(textStream, out pageCount, out extractedPages, out usedLetterFallback);

                if (string.IsNullOrWhiteSpace(fullText))
                {
                    var plainText = await TryExtractPdfTextWithPdfToTextAsync(pdfBytes, cancellationToken);
                    fullText = plainText.Text;
                    if (plainText.ExtractedPageCount > 0)
                    {
                        extractedPages = plainText.ExtractedPageCount;
                        usedPdfToTextFallback = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(fullText))
                {
                    var ocr = await TryExtractPdfTextWithOcrAsync(pdfBytes, cancellationToken);
                    fullText = ocr.Text;
                    if (ocr.ExtractedPageCount > 0)
                    {
                        extractedPages = ocr.ExtractedPageCount;
                        usedOcrFallback = true;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(fullText))
            {
                throw new InvalidOperationException(
                    "PDF에서 텍스트를 추출하지 못했습니다. 스캔본/이미지 기반 PDF이거나 비정상 인코딩일 수 있습니다.");
            }

            var chunks = BuildChunks(fullText);
            if (chunks.Count == 0)
            {
                throw new InvalidOperationException("PDF에서 인덱싱 가능한 텍스트 청크를 만들지 못했습니다.");
            }

            var payloads = new List<PreparedChunkPayload>(chunks.Count);
            foreach (var c in chunks)
            {
                var text = NormalizeExtractedText(c.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var embedding = await _embeddingService.EmbedTextAsync(text);
                payloads.Add(new PreparedChunkPayload
                {
                    PageNumber = c.PageNumber,
                    Text = text,
                    EmbeddingJson = JsonSerializer.Serialize(embedding)
                });
            }

            if (payloads.Count == 0)
            {
                throw new InvalidOperationException("PDF 텍스트가 추출되었지만 인덱싱 가능한 문장 콘텐츠가 없습니다.");
            }

            _logger.LogInformation(
                "PDF 텍스트 추출 완료. pages={PageCount}, extractedPages={ExtractedPages}, chunks={ChunkCount}, letterFallback={LetterFallback}, pdfToTextFallback={PdfToTextFallback}, ocrFallback={OcrFallback}",
                pageCount,
                extractedPages,
                payloads.Count,
                usedLetterFallback,
                usedPdfToTextFallback,
                usedOcrFallback);

            return payloads;
        }

        private async Task<(string Text, int ExtractedPageCount)> TryExtractPdfTextWithOcrAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            var ocrEnabled = _configuration.GetValue<bool?>("Rag:DocumentOcrEnabled") ?? true;
            if (!ocrEnabled)
            {
                return (string.Empty, 0);
            }

            var tesseractPath = _configuration["Rag:Ocr:TesseractPath"];
            if (string.IsNullOrWhiteSpace(tesseractPath))
            {
                tesseractPath = "tesseract";
            }

            var pdftoppmPath = _configuration["Rag:Ocr:PdfToPpmPath"];
            if (string.IsNullOrWhiteSpace(pdftoppmPath))
            {
                pdftoppmPath = "pdftoppm";
            }

            var lang = _configuration["Rag:Ocr:Language"];
            if (string.IsNullOrWhiteSpace(lang))
            {
                lang = "eng";
            }

            var dpi = Math.Clamp(_configuration.GetValue<int?>("Rag:Ocr:Dpi") ?? 240, 120, 400);
            var psm = Math.Clamp(_configuration.GetValue<int?>("Rag:Ocr:Psm") ?? 6, 3, 13);

            var tempDir = Path.Combine(Path.GetTempPath(), $"aidesk-ocr-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var pdfPath = Path.Combine(tempDir, "input.pdf");
                await File.WriteAllBytesAsync(pdfPath, pdfBytes, cancellationToken);

                var pagePrefix = Path.Combine(tempDir, "page");
                var convert = await RunProcessCaptureAsync(
                    pdftoppmPath,
                    $"-r {dpi} -png \"{pdfPath}\" \"{pagePrefix}\"",
                    tempDir,
                    cancellationToken);

                if (convert.ExitCode != 0)
                {
                    _logger.LogWarning("OCR 전처리 실패(pdftoppm). exit={ExitCode}, err={Error}", convert.ExitCode, convert.Stderr);
                    return (string.Empty, 0);
                }

                var imageFiles = Directory
                    .GetFiles(tempDir, "page-*.png", SearchOption.TopDirectoryOnly)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    return (string.Empty, 0);
                }

                var sb = new StringBuilder();
                var extractedPages = 0;

                for (var i = 0; i < imageFiles.Count; i++)
                {
                    var imagePath = imageFiles[i];
                    var ocr = await RunProcessCaptureAsync(
                        tesseractPath,
                        $"\"{imagePath}\" stdout -l {lang} --psm {psm}",
                        tempDir,
                        cancellationToken);

                    if (ocr.ExitCode != 0)
                    {
                        _logger.LogWarning("OCR 실패(tesseract). file={File}, exit={ExitCode}, err={Error}", Path.GetFileName(imagePath), ocr.ExitCode, ocr.Stderr);
                        continue;
                    }

                    var text = NormalizeExtractedText(ocr.Stdout);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    extractedPages++;
                    sb.AppendLine($"[Page {i + 1}]");
                    sb.AppendLine(text);
                    sb.AppendLine();
                }

                return (sb.ToString(), extractedPages);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR fallback 처리 중 오류가 발생했습니다.");
                return (string.Empty, 0);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                    // 임시 폴더 정리는 실패해도 다음 요청 처리에는 영향이 없다.
                }
            }
        }

        private async Task<(string Text, int ExtractedPageCount)> TryExtractPdfTextWithPdfToTextAsync(
            byte[] pdfBytes,
            CancellationToken cancellationToken)
        {
            var pdftotextPath = _configuration["Rag:Ocr:PdfToTextPath"];
            if (string.IsNullOrWhiteSpace(pdftotextPath))
            {
                pdftotextPath = "pdftotext";
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"aidesk-pdftotext-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var pdfPath = Path.Combine(tempDir, "input.pdf");
                await File.WriteAllBytesAsync(pdfPath, pdfBytes, cancellationToken);

                var layout = await TryRunPdfToTextVariantAsync(pdftotextPath, pdfPath, tempDir, "layout", cancellationToken);
                if (!string.IsNullOrWhiteSpace(layout.Text))
                {
                    return (layout.Text, layout.ExtractedPageCount);
                }

                var rawVariant = await TryRunPdfToTextVariantAsync(pdftotextPath, pdfPath, tempDir, "raw", cancellationToken);
                if (!string.IsNullOrWhiteSpace(rawVariant.Text))
                {
                    return (rawVariant.Text, rawVariant.ExtractedPageCount);
                }

                return (string.Empty, 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "pdftotext fallback 처리 중 오류가 발생했습니다.");
                return (string.Empty, 0);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                    // 임시 폴더 정리는 실패해도 다음 요청 처리에는 영향이 없다.
                }
            }
        }

        private async Task<(string Text, int ExtractedPageCount)> TryRunPdfToTextVariantAsync(
            string pdftotextPath,
            string pdfPath,
            string tempDir,
            string mode,
            CancellationToken cancellationToken)
        {
            var outputPath = Path.Combine(tempDir, $"out-{mode}.txt");
            var modeArg = string.Equals(mode, "raw", StringComparison.OrdinalIgnoreCase) ? "-raw" : "-layout";

            var run = await RunProcessCaptureAsync(
                pdftotextPath,
                $"-enc UTF-8 {modeArg} \"{pdfPath}\" \"{outputPath}\"",
                tempDir,
                cancellationToken);

            if (run.ExitCode != 0)
            {
                _logger.LogWarning("pdftotext({Mode}) 실패. exit={ExitCode}, err={Error}", mode, run.ExitCode, run.Stderr);
                return (string.Empty, 0);
            }

            if (!File.Exists(outputPath))
            {
                return (string.Empty, 0);
            }

            var raw = await File.ReadAllTextAsync(outputPath, Encoding.UTF8, cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return (string.Empty, 0);
            }

            var sb = new StringBuilder();
            var pages = raw.Split('\f');
            var extractedPages = 0;

            for (var i = 0; i < pages.Length; i++)
            {
                var pageText = NormalizeExtractedText(pages[i]);
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                extractedPages++;
                sb.AppendLine($"[Page {i + 1}]");
                sb.AppendLine(pageText);
                sb.AppendLine();
            }

            return (sb.ToString(), extractedPages);
        }

        private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessCaptureAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return (process.ExitCode, stdout, stderr);
        }

        private static string ExtractPdfText(Stream stream, out int pageCount, out int extractedPages, out bool usedLetterFallback)
        {
            using var doc = PdfDocument.Open(stream);
            var sb = new StringBuilder();
            pageCount = 0;
            extractedPages = 0;
            usedLetterFallback = false;

            foreach (var page in doc.GetPages())
            {
                pageCount++;

                var pageText = NormalizeExtractedText(page.Text);
                if (string.IsNullOrWhiteSpace(pageText))
                {
                    pageText = ExtractTextFromLetters(page);
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        usedLetterFallback = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(pageText)) continue;

                extractedPages++;
                sb.AppendLine($"[Page {page.Number}]");
                sb.AppendLine(pageText);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string NormalizeExtractedText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var normalized = text
                .Replace("\0", string.Empty)
                .Replace("\u00A0", " ")
                .Trim();

            return normalized;
        }

        private static string ExtractTextFromLetters(Page page)
        {
            var letters = page.Letters?.ToList();
            if (letters == null || letters.Count == 0) return string.Empty;

            var ordered = letters
                .OrderByDescending(l => Math.Round(l.GlyphRectangle.Bottom, 1))
                .ThenBy(l => l.GlyphRectangle.Left)
                .ToList();

            var sb = new StringBuilder();
            double? prevBottom = null;
            double? prevRight = null;

            foreach (var letter in ordered)
            {
                var glyph = letter.Value;
                if (string.IsNullOrEmpty(glyph)) continue;

                var bottom = letter.GlyphRectangle.Bottom;
                var left = letter.GlyphRectangle.Left;

                if (prevBottom.HasValue)
                {
                    if (Math.Abs(bottom - prevBottom.Value) > 2.5)
                    {
                        sb.AppendLine();
                        prevRight = null;
                    }
                    else if (prevRight.HasValue && left - prevRight.Value > 2.0)
                    {
                        sb.Append(' ');
                    }
                }

                sb.Append(glyph);
                prevBottom = bottom;
                prevRight = letter.GlyphRectangle.Right;
            }

            return NormalizeExtractedText(sb.ToString());
        }

        private static List<(int PageNumber, string Text)> BuildChunks(string fullText)
        {
            const int MaxChunkSize = 900;
            const int MinChunkSize = 20;

            var chunks = new List<(int PageNumber, string Text)>();
            var currentPage = 1;

            // [Page N] 마커 기준으로 페이지 블록 분리
            var pageBlocks = new List<(int Page, string Content)>();
            var sb = new StringBuilder();

            foreach (var line in fullText.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[Page ", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(']'))
                {
                    if (sb.Length > 0)
                    {
                        pageBlocks.Add((currentPage, sb.ToString()));
                        sb.Clear();
                    }
                    var numberPart = trimmed["[Page ".Length..^1];
                    if (int.TryParse(numberPart, out var parsed)) currentPage = parsed;
                    continue;
                }
                sb.AppendLine(trimmed);
            }
            if (sb.Length > 0) pageBlocks.Add((currentPage, sb.ToString()));

            // 페이지 블록을 문단 → 문장 순으로 청킹
            foreach (var (page, content) in pageBlocks)
            {
                // 빈 줄(\n\n) 기준 문단 분리
                var paragraphs = content
                    .Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                var buffer = new StringBuilder();
                string lastSentence = string.Empty;

                foreach (var para in paragraphs)
                {
                    // 문단이 최대 크기를 초과하면 문장 단위로 추가 분리
                    var segments = para.Length <= MaxChunkSize
                        ? new List<string> { para }
                        : SplitBySentence(para, MaxChunkSize);

                    foreach (var segment in segments)
                    {
                        if (buffer.Length > 0 && buffer.Length + segment.Length > MaxChunkSize)
                        {
                            var text = NormalizeExtractedText(buffer.ToString());
                            if (!string.IsNullOrWhiteSpace(text))
                                chunks.Add((page, text));

                            // 오버랩: 직전 문장 1개 이어붙이기
                            buffer.Clear();
                            if (!string.IsNullOrWhiteSpace(lastSentence))
                                buffer.AppendLine(lastSentence);
                        }

                        buffer.AppendLine(segment);
                        lastSentence = segment;
                    }
                }

                if (buffer.Length >= MinChunkSize)
                {
                    var text = NormalizeExtractedText(buffer.ToString());
                    if (!string.IsNullOrWhiteSpace(text))
                        chunks.Add((page, text));
                    buffer.Clear();
                }
            }

            return chunks;
        }

        private static List<string> SplitBySentence(string text, int maxSize)
        {
            // 문장 구분자: 마침표/느낌표/물음표 뒤 공백 or 줄바꿈
            var sentenceEnders = new[] { ". ", ".\n", "! ", "!\n", "? ", "?\n", "。" };
            var results = new List<string>();
            var sb = new StringBuilder();

            var i = 0;
            while (i < text.Length)
            {
                sb.Append(text[i]);

                bool isBoundary = false;
                foreach (var ender in sentenceEnders)
                {
                    if (i + ender.Length <= text.Length &&
                        text.Substring(i, ender.Length) == ender)
                    {
                        // ender 나머지 문자 추가
                        for (var j = 1; j < ender.Length; j++)
                            sb.Append(text[++i]);
                        isBoundary = true;
                        break;
                    }
                }

                if (isBoundary && sb.Length >= maxSize / 2)
                {
                    results.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else if (sb.Length >= maxSize)
                {
                    results.Add(sb.ToString().Trim());
                    sb.Clear();
                }

                i++;
            }

            if (sb.Length > 0) results.Add(sb.ToString().Trim());
            return results.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private static float[]? ParseEmbedding(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array) return null;

                var result = new List<float>();
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Number) continue;
                    if (item.TryGetSingle(out var v)) result.Add(v);
                    else result.Add((float)item.GetDouble());
                }

                return result.Count == 0 ? null : result.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static float CosineSimilarity(float[] vec1, float[] vec2)
        {
            var length = Math.Min(vec1.Length, vec2.Length);
            if (length == 0) return 0f;

            float dot = 0f, normA = 0f, normB = 0f;
            for (var i = 0; i < length; i++)
            {
                dot += vec1[i] * vec2[i];
                normA += vec1[i] * vec1[i];
                normB += vec2[i] * vec2[i];
            }

            return normA == 0f || normB == 0f ? 0f : (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
        }

        private static string NormalizeVisibility(string? value)
        {
            if (string.Equals(value, "user", StringComparison.OrdinalIgnoreCase)) return "user";
            return "admin";
        }

        private static string NormalizePlatform(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "공통";
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return "공통";

            if (string.Equals(trimmed, "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "공통", StringComparison.OrdinalIgnoreCase))
                return "공통";

            if (string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "전체", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                return "전체 플랫폼";

            return trimmed.ToLowerInvariant();
        }

        private sealed class PreparedChunkPayload
        {
            public int PageNumber { get; set; }
            public string Text { get; set; } = string.Empty;
            public string EmbeddingJson { get; set; } = string.Empty;
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            return ms.ToArray();
        }

        private string GetStorageRootPath()
        {
            var configured = _configuration["Rag:DocumentStorage:Path"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var trimmed = configured.Trim();
                if (Path.IsPathRooted(trimmed)) return trimmed;
                return Path.Combine(_environment.ContentRootPath, trimmed);
            }

            return Path.Combine(_environment.ContentRootPath, "storage", "documents");
        }

        private string BuildStoredPdfPath(int documentId)
        {
            return Path.Combine(GetStorageRootPath(), $"doc-{documentId}.pdf");
        }

        private async Task SaveOriginalPdfAsync(int documentId, byte[] pdfBytes, CancellationToken cancellationToken)
        {
            var root = GetStorageRootPath();
            Directory.CreateDirectory(root);
            var path = BuildStoredPdfPath(documentId);
            await File.WriteAllBytesAsync(path, pdfBytes, cancellationToken);
        }

        private void DeleteOriginalPdfIfExists(int documentId)
        {
            var path = BuildStoredPdfPath(documentId);
            if (!File.Exists(path)) return;
            File.Delete(path);
        }
    }

    public sealed class DocumentKnowledgeUploadResult
    {
        public int DocumentId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int ChunkCount { get; set; }
    }

    public sealed class DocumentChunkSearchHit
    {
        public int DocumentId { get; set; }
        public int ChunkId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public string Content { get; set; } = string.Empty;
        public float BaseSimilarity { get; set; }
        public float Score { get; set; }
    }

    public sealed class DocumentKnowledgeListItem
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Visibility { get; set; } = "admin";
        public string Platform { get; set; } = "공통";
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class DocumentDownloadInfo
    {
        public int DocumentId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string DownloadFileName { get; set; } = string.Empty;
    }
}

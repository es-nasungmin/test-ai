using System.Collections.Concurrent;
using System.Threading.Channels;
using AiDeskApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AiDeskApi.Services
{
    public interface IKnowledgeBaseVectorSyncQueue
    {
        ValueTask EnqueueAsync(int kbId, CancellationToken cancellationToken = default);
        Task EnqueuePendingKnowledgeBasesAsync(CancellationToken cancellationToken = default);
        ValueTask<int> DequeueAsync(CancellationToken cancellationToken = default);
        void Complete(int kbId);
    }

    public sealed class KnowledgeBaseVectorSyncQueue : IKnowledgeBaseVectorSyncQueue
    {
        private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        private readonly ConcurrentDictionary<int, byte> _queuedIds = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<KnowledgeBaseVectorSyncQueue> _logger;

        public KnowledgeBaseVectorSyncQueue(IServiceScopeFactory scopeFactory, ILogger<KnowledgeBaseVectorSyncQueue> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async ValueTask EnqueueAsync(int kbId, CancellationToken cancellationToken = default)
        {
            if (kbId <= 0)
            {
                return;
            }

            if (!_queuedIds.TryAdd(kbId, 0))
            {
                return;
            }

            await _channel.Writer.WriteAsync(kbId, cancellationToken);
        }

        public async Task EnqueuePendingKnowledgeBasesAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AiDeskContext>();

            var pendingIds = await db.KnowledgeBases
                .AsNoTracking()
                .Where(x => x.VectorSyncStatus == "pending")
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var kbId in pendingIds)
            {
                await EnqueueAsync(kbId, cancellationToken);
            }
        }

        public ValueTask<int> DequeueAsync(CancellationToken cancellationToken = default)
        {
            return _channel.Reader.ReadAsync(cancellationToken);
        }

        public void Complete(int kbId)
        {
            _queuedIds.TryRemove(kbId, out _);
        }
    }

    public sealed class KnowledgeBaseVectorSyncWorker : BackgroundService
    {
        private readonly IKnowledgeBaseVectorSyncQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<KnowledgeBaseVectorSyncWorker> _logger;

        public KnowledgeBaseVectorSyncWorker(
            IKnowledgeBaseVectorSyncQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<KnowledgeBaseVectorSyncWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 KB 벡터 백그라운드 동기화 워커 시작");
            await _queue.EnqueuePendingKnowledgeBasesAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var kbId = await _queue.DequeueAsync(stoppingToken);

                try
                {
                    await ProcessAsync(kbId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ 백그라운드 KB 벡터 동기화 처리 실패 kbId={KbId}", kbId);
                }
                finally
                {
                    _queue.Complete(kbId);
                }
            }
        }

        private async Task ProcessAsync(int kbId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AiDeskContext>();
            var vectorSearchService = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();

            var kb = await db.KnowledgeBases
                .Include(x => x.ExpectedQuestions)
                .FirstOrDefaultAsync(x => x.Id == kbId, cancellationToken);

            if (kb == null)
            {
                _logger.LogInformation("ℹ️ 백그라운드 벡터 동기화 스킵: KB 없음 kbId={KbId}", kbId);
                return;
            }

            try
            {
                await vectorSearchService.UpsertKnowledgeBaseAsync(kb, cancellationToken);
                kb.VectorSyncStatus = "synced";
                kb.VectorSyncedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("✅ 백그라운드 KB 벡터 동기화 완료 kbId={KbId}", kbId);
            }
            catch (Exception ex)
            {
                kb.VectorSyncStatus = "failed";
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(ex, "⚠️ 백그라운드 KB 벡터 동기화 실패 kbId={KbId}", kbId);
            }
        }
    }
}
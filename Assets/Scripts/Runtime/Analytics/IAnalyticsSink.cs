using System;
using System.Collections.Generic;

namespace IdleDefense.Analytics
{
    /// <summary>
    /// 전송 계층. IRewardedAdProvider가 광고 SDK를 격리한 것과 같은 구조다.
    /// SDK를 나중에 바꿔도 게임 코드는 건드리지 않는다.
    /// </summary>
    public interface IAnalyticsSink
    {
        /// <summary>배치 전송. 성공하면 true. 실패분은 호출자가 큐에 되돌린다.</summary>
        bool Send(IReadOnlyList<AnalyticsEvent> batch);
    }

    /// <summary>아무것도 하지 않는 기본 구현. Sink가 없을 때 NullReference를 막는다.</summary>
    public sealed class NullAnalyticsSink : IAnalyticsSink
    {
        public bool Send(IReadOnlyList<AnalyticsEvent> batch) => true;
    }

    /// <summary>
    /// 이벤트 버퍼. 큐잉·배치·유실 방지를 담당한다. 순수 C#이라 테스트가 가능하다.
    ///
    /// 오프라인 게임이라 유실 방지가 특히 중요하다.
    /// 비행기 모드로 며칠을 플레이할 수 있으므로, 전송 실패분은 버리지 않고 큐에 남긴다.
    /// </summary>
    public sealed class AnalyticsBuffer
    {
        private readonly List<AnalyticsEvent> queue = new List<AnalyticsEvent>();
        private readonly IAnalyticsSink sink;
        private readonly int batchSize;
        private readonly int maxQueue;

        public AnalyticsBuffer(IAnalyticsSink sink, int batchSize = 20, int maxQueue = 2000)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            if (batchSize <= 0) throw new ArgumentException("배치 크기는 1 이상", nameof(batchSize));
            if (maxQueue < batchSize) throw new ArgumentException("큐 상한이 배치보다 작습니다", nameof(maxQueue));
            this.batchSize = batchSize;
            this.maxQueue = maxQueue;
        }

        public int PendingCount => queue.Count;
        /// <summary>큐 상한을 넘겨 버린 이벤트 수. 0이 아니면 전송이 막혀 있다는 신호다.</summary>
        public int DroppedCount { get; private set; }

        public void Enqueue(AnalyticsEvent e)
        {
            if (e == null) return;

            if (queue.Count >= maxQueue)
            {
                // 상한을 넘으면 가장 오래된 것을 버린다.
                // 최근 이벤트가 분석 가치가 높고, 무한 증가는 메모리를 잠식한다.
                queue.RemoveAt(0);
                DroppedCount++;
            }
            queue.Add(e);
        }

        public bool ShouldFlush => queue.Count >= batchSize;

        /// <summary>
        /// 한 배치를 보낸다. 실패하면 큐를 그대로 두고 false를 돌려준다.
        /// </summary>
        public bool Flush()
        {
            if (queue.Count == 0) return true;

            int take = Math.Min(batchSize, queue.Count);
            var batch = queue.GetRange(0, take);

            if (!sink.Send(batch)) return false;

            queue.RemoveRange(0, take);
            return true;
        }

        /// <summary>앱 종료·백그라운드 전환 시. 성공한 만큼만 큐에서 제거된다.</summary>
        public bool FlushAll()
        {
            while (queue.Count > 0)
                if (!Flush()) return false;
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using IdleDefense.Core;
using IdleDefense.Analytics;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 계측 계층 검증.
    ///
    /// 계측 로직을 MonoBehaviour에 두지 않은 이유가 여기 있다.
    /// P0의 근본 원인은 검증이 실제 코드 경로에 닿지 못한 것이었고,
    /// Update() 안에 로직을 넣으면 같은 사각지대가 다시 생긴다.
    ///
    /// docs/Analytics_v0.1_설계.md
    /// </summary>
    public class AnalyticsTests
    {
        private static AnalyticsContext Ctx() => new AnalyticsContext
        {
            SessionId = "s-1", RunId = 42, UserDay = 7,
            Tier = 3, Wave = 150, BestWave = 166,
            CoresLog10 = 3.14, Gems = 500, AdsRemoved = false,
            RunsSinceAscend = 2,
        };

        // ── 스키마 완결성 ──

        [Test]
        public void 모든_이벤트가_버전을_선언한다()
        {
            foreach (var name in AnalyticsSchema.AllEvents)
            {
                Assert.IsTrue(AnalyticsSchema.Versions.ContainsKey(name),
                    $"'{name}'의 스키마 버전이 없습니다");
                Assert.Greater(AnalyticsSchema.VersionOf(name), 0, name);
            }
            Assert.AreEqual(12, AnalyticsSchema.AllEvents.Length,
                "설계 문서는 이벤트 12종입니다. 늘리거나 줄였다면 문서도 갱신하세요");
        }

        [Test]
        public void 스키마에_없는_이벤트는_거부된다()
        {
            Assert.Throws<ArgumentException>(() => AnalyticsSchema.VersionOf("made_up_event"));
        }

        [Test]
        public void 모든_이벤트가_공통_필드를_갖는다()
        {
            var ctx = Ctx();
            foreach (var name in AnalyticsSchema.AllEvents)
            {
                var e = AnalyticsEvents.Create(name, ctx, 1_700_000_000_000L);
                foreach (var f in AnalyticsSchema.RequiredCommonFields)
                    Assert.IsTrue(e.Has(f),
                        $"'{name}'에 공통 필드 '{f}'가 없습니다 — 분석에서 조인이 불가능해집니다");
            }
        }

        [Test]
        public void 승천직후_축이_모든_이벤트에_붙는다()
        {
            // runs_since_ascend 는 P0에서 발견한 유일한 역전 지점을 추적하는 축이다.
            // 광고 시청자의 승천 직후 런이 44초로 가장 짧았다.
            var ctx = Ctx();
            foreach (var name in AnalyticsSchema.AllEvents)
            {
                var e = AnalyticsEvents.Create(name, ctx, 0);
                Assert.AreEqual(2, e.Get("runs_since_ascend"), name);
            }
        }

        [Test]
        public void 버전이_0이하면_이벤트를_만들_수_없다()
        {
            Assert.Throws<ArgumentException>(() => new AnalyticsEvent("x", 0, 0));
            Assert.Throws<ArgumentException>(() => new AnalyticsEvent("", 1, 0));
        }

        // ── BigNumber 전송 ──

        [Test]
        public void BigNumber는_log10과_원본을_함께_보낸다()
        {
            var e = new AnalyticsEvent(AnalyticsSchema.RunEnd, 1, 0);
            e.SetBig("coin", new BigNumber(1e15));

            Assert.IsTrue(e.Has("coin_log10"), "분석용 log10 필드가 없습니다");
            Assert.IsTrue(e.Has("coin_raw"), "디버그용 원본 필드가 없습니다");
            Assert.AreEqual(15.0, (double)e.Get("coin_log10"), 1e-9);
        }

        /// <summary>
        /// 숫자 필드가 BigNumber 경로로 새지 않는가 — 실제로 났던 버그의 회귀 테스트.
        ///
        /// BigNumber에 int/double 암시적 변환이 있어서, Set(string, object)와
        /// Set(string, BigNumber) 오버로드가 겹치면 C#이 BigNumber 쪽을 고른다.
        /// (BigNumber -> object 변환은 있고 그 반대는 없으므로 BigNumber가 '더 나은' 타입)
        /// 그 결과 run_id / tier / wave 같은 정수 필드가 전부 _log10, _raw 로 새어 나갔다.
        ///
        /// 지금은 BigNumber 전용 메서드를 SetBig으로 분리해 오버로드 자체를 없앴다.
        /// 누군가 다시 Set(string, BigNumber)를 추가하면 여기서 잡힌다.
        /// </summary>
        [Test]
        public void 숫자_필드가_BigNumber_경로로_새지_않는다()
        {
            var e = new AnalyticsEvent(AnalyticsSchema.RunEnd, 1, 0);
            e.Set("an_int", 42)
             .Set("a_double", 3.5)
             .Set("a_long", 1234567890123L)
             .Set("a_bool", true)
             .Set("a_string", "x");

            foreach (var key in new[] { "an_int", "a_double", "a_long", "a_bool", "a_string" })
            {
                Assert.IsTrue(e.Has(key), $"'{key}'가 평범한 필드로 담기지 않았습니다");
                Assert.IsFalse(e.Has(key + "_log10"),
                    $"'{key}'가 BigNumber 경로로 샜습니다 — 오버로드가 다시 겹쳤습니다");
                Assert.IsFalse(e.Has(key + "_raw"), $"'{key}'에 _raw가 붙었습니다");
            }

            Assert.AreEqual(42, e.Get("an_int"));
            Assert.AreEqual(3.5, (double)e.Get("a_double"), 1e-12);
        }

        [Test]
        public void 공통_필드가_전부_평범한_필드로_담긴다()
        {
            // 공통 필드는 대부분 int다. 위 오버로드 함정의 직접 피해자였다.
            var e = AnalyticsEvents.Create(AnalyticsSchema.SessionStart, Ctx(), 0);

            Assert.AreEqual(42, e.Get("run_id"));
            Assert.AreEqual(3, e.Get("tier"));
            Assert.AreEqual(150, e.Get("wave"));
            Assert.AreEqual(166, e.Get("best_wave"));
            Assert.AreEqual(500, e.Get("gems"));
            Assert.AreEqual(7, e.Get("user_day"));
            Assert.AreEqual("s-1", e.Get("session_id"));

            foreach (var f in AnalyticsSchema.RequiredCommonFields)
                Assert.IsFalse(e.Has(f + "_log10"), $"공통 필드 '{f}'가 BigNumber 경로로 샜습니다");
        }

        [Test]
        public void BigNumber_log10이_극단값에서도_유한하다()
        {
            var huge = new BigNumber(1e150) * new BigNumber(1e150);   // 1e300
            Assert.AreEqual(300.0, AnalyticsEvent.Log10OrZero(huge), 1e-6);

            Assert.AreEqual(0.0, AnalyticsEvent.Log10OrZero(BigNumber.Zero),
                "0 코인은 0으로 보내야 합니다 (-Infinity가 JSON을 깨뜨립니다)");
            Assert.AreEqual(0.0, AnalyticsEvent.Log10OrZero(new BigNumber(-5.0)),
                "음수도 0으로 보내야 합니다");
        }

        [Test]
        public void JSON에_NaN이나_Infinity가_들어가지_않는다()
        {
            var e = new AnalyticsEvent(AnalyticsSchema.RunEnd, 1, 0);
            e.Set("bad_nan", double.NaN)
             .Set("bad_inf", double.PositiveInfinity);

            string json = e.ToJson();
            Assert.IsFalse(json.Contains("NaN"), "JSON에 NaN이 들어가면 파이프라인이 깨집니다");
            Assert.IsFalse(json.Contains("Infinity"), "JSON에 Infinity가 들어가면 파이프라인이 깨집니다");
        }

        [Test]
        public void JSON_문자열이_이스케이프된다()
        {
            var e = new AnalyticsEvent(AnalyticsSchema.AdRequest, 1, 0);
            e.Set("fail_reason", "네트워크 \"끊김\"\n재시도");

            string json = e.ToJson();
            Assert.IsFalse(json.Contains("\"끊김\"\n"), "따옴표·개행이 이스케이프되지 않았습니다");
            Assert.IsTrue(json.Contains("\\\""), "따옴표 이스케이프 누락");
            Assert.IsTrue(json.Contains("\\n"), "개행 이스케이프 누락");
        }

        [Test]
        public void 트랙_배열이_JSON_배열로_직렬화된다()
        {
            var e = new AnalyticsEvent(AnalyticsSchema.RunEnd, 1, 0);
            e.Set("upgrades_by_track", new[] { 20, 15, 10, 8, 10 })
             .Set("total_upgrade_levels", 63);

            string json = e.ToJson();
            Assert.IsTrue(json.Contains("[20,15,10,8,10]"),
                $"트랙 배열이 배열로 직렬화되지 않았습니다: {json}");
        }

        // ── 버퍼 ──

        private sealed class CountingSink : IAnalyticsSink
        {
            public int Batches, Events;
            public bool Fail;
            public bool Send(IReadOnlyList<AnalyticsEvent> batch)
            {
                if (Fail) return false;
                Batches++; Events += batch.Count;
                return true;
            }
        }

        private static AnalyticsEvent Dummy(int i)
            => AnalyticsEvents.Create(AnalyticsSchema.RunEnd, Ctx(), i);

        [Test]
        public void 배치_크기에_도달하면_플러시_대상이_된다()
        {
            var sink = new CountingSink();
            var buf = new AnalyticsBuffer(sink, batchSize: 5);

            for (int i = 0; i < 4; i++) buf.Enqueue(Dummy(i));
            Assert.IsFalse(buf.ShouldFlush, "4건인데 플러시 대상이 됐습니다");

            buf.Enqueue(Dummy(4));
            Assert.IsTrue(buf.ShouldFlush, "5건인데 플러시 대상이 아닙니다");
        }

        [Test]
        public void 플러시하면_큐에서_제거된다()
        {
            var sink = new CountingSink();
            var buf = new AnalyticsBuffer(sink, batchSize: 5);
            for (int i = 0; i < 12; i++) buf.Enqueue(Dummy(i));

            Assert.IsTrue(buf.Flush());
            Assert.AreEqual(7, buf.PendingCount, "한 배치(5)만 나가야 합니다");

            Assert.IsTrue(buf.FlushAll());
            Assert.AreEqual(0, buf.PendingCount);
            Assert.AreEqual(12, sink.Events);
        }

        [Test]
        public void 전송_실패시_이벤트를_잃지_않는다()
        {
            // 오프라인 게임이라 유실 방지가 중요하다.
            // 비행기 모드로 며칠 플레이할 수 있다.
            var sink = new CountingSink { Fail = true };
            var buf = new AnalyticsBuffer(sink, batchSize: 5);
            for (int i = 0; i < 8; i++) buf.Enqueue(Dummy(i));

            Assert.IsFalse(buf.Flush(), "전송이 실패했는데 성공으로 보고했습니다");
            Assert.AreEqual(8, buf.PendingCount, "전송 실패인데 큐에서 사라졌습니다");

            sink.Fail = false;
            Assert.IsTrue(buf.FlushAll());
            Assert.AreEqual(8, sink.Events, "복구 후 전부 전송돼야 합니다");
            Assert.AreEqual(0, buf.DroppedCount, "상한에 안 닿았는데 버렸습니다");
        }

        [Test]
        public void 큐_상한을_넘으면_오래된_것부터_버리고_기록한다()
        {
            var sink = new CountingSink { Fail = true };
            var buf = new AnalyticsBuffer(sink, batchSize: 5, maxQueue: 10);
            for (int i = 0; i < 25; i++) buf.Enqueue(Dummy(i));

            Assert.AreEqual(10, buf.PendingCount, "큐가 상한을 넘겼습니다");
            Assert.AreEqual(15, buf.DroppedCount,
                "버린 이벤트 수가 기록되지 않으면 전송 장애를 눈치챌 수 없습니다");
        }

        [Test]
        public void 빈_큐를_플러시해도_실패하지_않는다()
        {
            var buf = new AnalyticsBuffer(new CountingSink(), batchSize: 5);
            Assert.IsTrue(buf.Flush());
            Assert.IsTrue(buf.FlushAll());
        }

        [Test]
        public void 잘못된_버퍼_설정은_생성_시점에_거부된다()
        {
            var sink = new CountingSink();
            Assert.Throws<ArgumentNullException>(() => new AnalyticsBuffer(null));
            Assert.Throws<ArgumentException>(() => new AnalyticsBuffer(sink, batchSize: 0));
            Assert.Throws<ArgumentException>(() => new AnalyticsBuffer(sink, batchSize: 20, maxQueue: 5));
        }
    }
}

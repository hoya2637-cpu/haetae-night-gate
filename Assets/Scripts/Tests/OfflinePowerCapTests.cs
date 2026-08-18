using System;
using NUnit.Framework;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 오프라인 시작 웨이브의 전투력 상한 — 회귀 테스트.
    ///
    /// 배경: 티어 승천 시 코어 85%가 소각되어 전투력이 급감하는데,
    /// 오프라인 보상은 더 강했던 직전 런의 도달 웨이브를 기준으로 시작 웨이브를 잡았다.
    /// 그 결과 승천 직후 런이 1.36분 만에 벽에 걸렸다. (docs/P0_계측결과_2차.md 2장)
    ///
    /// 처방은 승천 전용 예외가 아니라 일반 계약이다.
    ///   "지금 전투력으로 넘을 수 있는 웨이브를 넘겨 시작하지 않는다"
    /// 코어 소각·업그레이드 초기화 등 전투력이 떨어지는 어떤 경우에도 작동한다.
    /// </summary>
    public class OfflinePowerCapTests
    {
        private EconomyConfig cfg;

        [SetUp]
        public void SetUp() => cfg = ScriptableObject.CreateInstance<EconomyConfig>();

        [TearDown]
        public void TearDown()
        {
            if (cfg != null) UnityEngine.Object.DestroyImmediate(cfg);
            cfg = null;
        }

        /// <summary>주어진 시작 웨이브를 실제로 넘을 수 있는지 BattleRunner로 확인한다.</summary>
        private bool CanActuallyClear(int startWave, BigNumber coin, double atkMul)
        {
            var runner = new BattleRunner(cfg);
            var tracks = new UpgradeTracks(cfg);
            runner.BeginRun(startWave, coin);
            runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;

            while (tracks.BuyBest(runner.Coin, out var cost))
            {
                runner.SpendCoin(cost);
                runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;
            }

            runner.Tick(0.02, tracks.TotalLevel);
            return !runner.IsWalled;
        }

        // ── 1. 일반 환생 ──

        [Test]
        public void 일반환생_시작웨이브가_현재전투력으로_돌파가능하다()
        {
            // 티어 2, 코어 300 — 승천 직후가 아닌 평상시 상태
            double atk = EconomyCore.AttackMultiplier(cfg, 300, 2);
            double coin = EconomyCore.CoinMultiplier(cfg, 300, 2);

            var off = EconomyCore.CalculateOffline(
                cfg, 8.0, 129, coin, false, cfg.offlineCapHours, atk);

            int start = Math.Max(1, (int)off.StartWave);
            Assert.IsTrue(CanActuallyClear(start, off.Coin, atk),
                $"시작 웨이브 {start}를 현재 전투력으로 넘지 못합니다");
        }

        // ── 2. 승천 직후 (코어 85% 소각) ──

        [Test]
        public void 승천직후_전투력이_떨어져도_돌파가능한_웨이브에서_시작한다()
        {
            const int lastWave = 166;          // 승천 전 도달 웨이브
            const double coresBefore = 2300;

            double coresAfter = EconomyCore.CoresAfterAscend(cfg, coresBefore);
            double atkAfter = EconomyCore.AttackMultiplier(cfg, coresAfter, 4);
            double coinAfter = EconomyCore.CoinMultiplier(cfg, coresAfter, 4);

            var off = EconomyCore.CalculateOffline(
                cfg, 8.0, lastWave, coinAfter, false, cfg.offlineCapHours, atkAfter);

            int start = Math.Max(1, (int)off.StartWave);

            Assert.IsTrue(CanActuallyClear(start, off.Coin, atkAfter),
                $"승천 직후 시작 웨이브 {start}에서 즉시 벽에 걸립니다 " +
                $"(코어 {coresBefore:N0} -> {coresAfter:N0})");
        }

        [Test]
        public void 승천직후_시작웨이브가_상한없을때보다_낮아진다()
        {
            const int lastWave = 166;
            double coresAfter = EconomyCore.CoresAfterAscend(cfg, 2300);
            double atkAfter = EconomyCore.AttackMultiplier(cfg, coresAfter, 4);
            double coinAfter = EconomyCore.CoinMultiplier(cfg, coresAfter, 4);

            var capped = EconomyCore.CalculateOffline(
                cfg, 8.0, lastWave, coinAfter, false, cfg.offlineCapHours, atkAfter);
            var uncapped = EconomyCore.CalculateOffline(
                cfg, 8.0, lastWave, coinAfter, false, cfg.offlineCapHours);

            Assert.Less(capped.StartWave, uncapped.StartWave,
                "승천 직후인데 상한이 전혀 작동하지 않았습니다");
            Assert.AreEqual(uncapped.Coin.ToString(), capped.Coin.ToString(),
                "상한은 시작 웨이브만 낮춰야 하며 코인 보상은 그대로여야 합니다");
        }

        // ── 3. 극단적인 코어 소각 ──

        [Test]
        public void 전투력이_바닥이면_시작웨이브가_1까지_내려간다()
        {
            // 코어 0, 티어 1 — 사실상 맨몸
            double atk = EconomyCore.AttackMultiplier(cfg, 0, 1);
            double coin = EconomyCore.CoinMultiplier(cfg, 0, 1);

            var off = EconomyCore.CalculateOffline(
                cfg, 8.0, 200, coin, false, cfg.offlineCapHours, atk);

            int start = Math.Max(1, (int)off.StartWave);
            Assert.GreaterOrEqual(start, 1);
            Assert.IsTrue(CanActuallyClear(start, off.Coin, atk),
                $"맨몸 상태에서 시작 웨이브 {start}를 넘지 못합니다");
        }

        // ── 4. 감당 가능하면 낮추지 않는다 ──

        [Test]
        public void 현재전투력이_충분하면_시작웨이브를_낮추지_않는다()
        {
            // 코어가 넉넉한 평상시 상태 — 상한이 걸릴 이유가 없다
            double atk = EconomyCore.AttackMultiplier(cfg, 5000, 4);
            double coin = EconomyCore.CoinMultiplier(cfg, 5000, 4);

            var capped = EconomyCore.CalculateOffline(
                cfg, 8.0, 190, coin, false, cfg.offlineCapHours, atk);
            var uncapped = EconomyCore.CalculateOffline(
                cfg, 8.0, 190, coin, false, cfg.offlineCapHours);

            Assert.AreEqual(uncapped.StartWave, capped.StartWave,
                "감당 가능한 상황인데 시작 웨이브가 불필요하게 깎였습니다");
        }

        // ── 5. 광고 2배 ──

        [Test]
        public void 광고배수는_코인만_2배로_올리고_전투력_상한은_유지된다()
        {
            double coresAfter = EconomyCore.CoresAfterAscend(cfg, 2300);
            double atk = EconomyCore.AttackMultiplier(cfg, coresAfter, 4);
            double coin = EconomyCore.CoinMultiplier(cfg, coresAfter, 4);

            var plain = EconomyCore.CalculateOffline(
                cfg, 8.0, 166, coin, false, cfg.offlineCapHours, atk);
            var withAd = EconomyCore.CalculateOffline(
                cfg, 8.0, 166, coin, true, cfg.offlineCapHours, atk);

            Assert.Greater(withAd.Coin, plain.Coin, "광고 보상이 코인을 늘리지 않았습니다");

            int start = Math.Max(1, (int)withAd.StartWave);
            Assert.IsTrue(CanActuallyClear(start, withAd.Coin, atk),
                $"광고 시청 후 시작 웨이브 {start}에서 즉시 벽에 걸립니다");
        }

        // ── 6. 기존 보호장치가 유지되는가 ──

        [Test]
        public void 상한을_붙여도_직전_도달웨이브를_넘지_않는다()
        {
            foreach (int lastWave in new[] { 50, 100, 166, 220 })
            {
                foreach (int tier in new[] { 1, 3, 5 })
                {
                    double atk = EconomyCore.AttackMultiplier(cfg, 1000, tier);
                    double coin = EconomyCore.CoinMultiplier(cfg, 1000, tier);

                    var off = EconomyCore.CalculateOffline(
                        cfg, 999.0, lastWave, coin, true, cfg.offlineCapHours, atk);

                    Assert.Less(off.StartWave, lastWave,
                        $"티어 {tier}, 직전 {lastWave}: 시작 웨이브가 직전 도달을 넘었습니다");
                }
            }
        }

        [Test]
        public void 상한을_붙여도_코어는_지급되지_않는다()
        {
            double atk = EconomyCore.AttackMultiplier(cfg, 1000, 3);
            double coin = EconomyCore.CoinMultiplier(cfg, 1000, 3);

            var off = EconomyCore.CalculateOffline(
                cfg, 999.0, 166, coin, true, cfg.offlineCapHours, atk);

            // OfflineReward에 코어 필드가 존재하지 않는 것이 설계다.
            // 젬과 코인만 지급된다. (CLAUDE.md 절대 규칙 2)
            Assert.GreaterOrEqual(off.Gems, 0);
            Assert.IsTrue(off.Coin.IsPositive || off.Coin.IsZero);
        }

        // ── 7. MaxClearableWave 자체 ──

        [Test]
        public void MaxClearableWave가_전투력에_단조증가한다()
        {
            var coin = new BigNumber(1e6);
            int prev = 0;
            foreach (double atk in new[] { 1.0, 2.5, 6.25, 15.6, 39.0 })
            {
                int w = EconomyCore.MaxClearableWave(cfg, coin, atk);
                Assert.GreaterOrEqual(w, prev, $"전투배수 {atk}에서 역전이 발생했습니다");
                prev = w;
            }
        }
    }
}

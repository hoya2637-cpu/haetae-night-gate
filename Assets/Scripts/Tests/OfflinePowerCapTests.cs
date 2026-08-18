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

        /// <summary>런 1회를 벽까지 돌리고 도달 웨이브를 돌려준다.</summary>
        private int RunToWall(int startWave, BigNumber coin, double atkMul, double coinMul)
        {
            var runner = new BattleRunner(cfg);
            var tracks = new UpgradeTracks(cfg);
            runner.BeginRun(startWave, coin);
            runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;
            runner.CoinMultiplier = coinMul * tracks.CoinMultiplier;

            long guard = 0;
            while (runner.IsRunning && guard++ < 5000000)
            {
                while (tracks.BuyBest(runner.Coin, out var cost))
                {
                    runner.SpendCoin(cost);
                    runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;
                    runner.CoinMultiplier = coinMul * tracks.CoinMultiplier;
                }
                runner.Tick(0.1, tracks.TotalLevel);
            }
            return runner.DeepestWave;
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
        public void 상한은_시작웨이브를_올리지_않는다()
        {
            // 상한은 안전망이다. 시작 웨이브를 낮출 수는 있어도 올려서는 안 되고,
            // 코인 보상에는 손대지 않아야 한다.
            //
            // 주의 - 승천 직후에도 이 상한은 대개 걸리지 않는다.
            // 실측(docs/P0_계측결과_2차.md 2장)의 1.36분 런은
            // "못 넘는 웨이브에서 시작해서"가 아니라
            // "넘을 수 있는 한계와 시작점 사이 여유(헤드룸)가 1웨이브뿐"이라서였다.
            // 헤드룸 문제는 별도 처방이 필요하며 계측 하네스가 추적한다.
            foreach (int tier in new[] { 1, 2, 3, 4, 5 })
            {
                double cores = EconomyCore.CoresAfterAscend(cfg, 2300);
                double atk = EconomyCore.AttackMultiplier(cfg, cores, tier);
                double coin = EconomyCore.CoinMultiplier(cfg, cores, tier);

                var capped = EconomyCore.CalculateOffline(
                    cfg, 8.0, 166, coin, false, cfg.offlineCapHours, atk);
                var uncapped = EconomyCore.CalculateOffline(
                    cfg, 8.0, 166, coin, false, cfg.offlineCapHours);

                Assert.LessOrEqual(capped.StartWave, uncapped.StartWave,
                    $"티어 {tier}: 상한이 시작 웨이브를 올렸습니다");
                Assert.AreEqual(uncapped.Coin.ToString(), capped.Coin.ToString(),
                    $"티어 {tier}: 상한이 코인 보상을 바꿨습니다");
            }
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

        // ═══════════════════════════════════════════════════════════
        //  결정 기록 — 승천 직후 짧은 런을 허용한다
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 승천 직후 헤드룸(도달 − 시작)이 좁아지는 것은 방치가 아니라 측정 후 선택이다.
        ///
        /// 2026-08 실측 (docs/P0_계측결과_3차_헤드룸.md):
        ///   승천 3→4 직후 런 = 헤드룸 1, 1.36분 (광고 시청 시 0, 0.73분)
        ///   그 다음 런       = 헤드룸 22, 8.55분      ← 한 런 만에 회복
        ///   60회차 중 붕괴는 승천당 1회뿐. 지속되지 않는다.
        ///
        /// ★ '회복'은 두 가지이며 이 테스트가 보는 것은 ①뿐이다.
        ///   ① 전투 헤드룸 회복 — 1런. 런 길이가 정상으로 돌아온다. (여기서 검증)
        ///   ② 최고기록 회복   — 승천 이전 최고 웨이브를 다시 넘는 데 걸리는 런 수.
        ///      실측 1 / 3 / 7 / 20 / 40런으로 승천이 거듭될수록 길어진다.
        ///      후반 승천에서는 일반 유저 기준 약 13일간 자기 기록을 못 깬다.
        ///      게임 동작은 정상이지만 UX 관찰 지표이며 SimulationTests가 계측한다.
        ///
        /// 회복하는 이유: 오프라인 시작 웨이브가 '직전 런의 도달'을 기준으로 산출되므로,
        /// 짧은 런이 나오면 그 결과가 다음 시작점을 자동으로 낮춘다. 자기 교정 구조다.
        /// 여기에 minHeadroom 같은 인위적 하한을 넣으면 이 교정을 덮어쓰게 된다.
        ///
        /// 따라서 minHeadroom 보정은 적용하지 않는다.
        /// 대신 마스터문서 8.4의 티어 진입 연출로 그 순간을 덮고,
        /// 소프트런치에서 아래 두 지표를 확인한 뒤 재검토한다.
        ///   - 승천 직후 세션 이탈률 vs 평시
        ///   - 승천 직후 광고 시청률 vs 평시  (유일하게 확인된 역전 지점)
        ///
        /// 이 테스트는 그 '자기 교정'이 실제로 작동하는지를 고정한다.
        /// 교정이 깨지면 짧은 런이 연쇄되므로 여기서 먼저 잡힌다.
        /// </summary>
        [Test]
        public void 승천직후_헤드룸_감소는_한_런으로_끝난다()
        {
            const int lastWave = 173;          // 승천 직전 런의 도달 웨이브
            const double coresBefore = 2309;
            const int tierAfter = 4;

            double cores = EconomyCore.CoresAfterAscend(cfg, coresBefore);

            // ── 런 A : 승천 직후 ──
            double atkA = EconomyCore.AttackMultiplier(cfg, cores, tierAfter);
            double coinA = EconomyCore.CoinMultiplier(cfg, cores, tierAfter);
            var offA = EconomyCore.CalculateOffline(
                cfg, 8.0, lastWave, coinA, false, cfg.offlineCapHours, atkA);
            int startA = Math.Max(1, (int)offA.StartWave);
            int reachedA = RunToWall(startA, offA.Coin, atkA, coinA);
            int headroomA = reachedA - startA;

            // ── 런 B : 그 다음 ──
            cores += EconomyCore.CoreGainWithDecay(cfg, reachedA, 1);
            double atkB = EconomyCore.AttackMultiplier(cfg, cores, tierAfter);
            double coinB = EconomyCore.CoinMultiplier(cfg, cores, tierAfter);
            var offB = EconomyCore.CalculateOffline(
                cfg, 8.0, reachedA, coinB, false, cfg.offlineCapHours, atkB);
            int startB = Math.Max(1, (int)offB.StartWave);
            int reachedB = RunToWall(startB, offB.Coin, atkB, coinB);
            int headroomB = reachedB - startB;

            Assert.Less(headroomA, 10,
                $"승천 직후 헤드룸이 {headroomA} — 전제가 바뀌었다면 이 결정을 재검토하세요");

            Assert.GreaterOrEqual(headroomB, 10,
                $"자기 교정이 작동하지 않았습니다. 승천 직후 {headroomA} -> 다음 런 {headroomB}. " +
                "짧은 런이 연쇄되면 minHeadroom 보정을 넣어야 합니다 " +
                "(docs/P0_계측결과_3차_헤드룸.md 4장)");

            Assert.Greater(headroomB, headroomA,
                "다음 런이 회복되지 않았습니다");
        }
    }
}

using System;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// cross — 계층 간 정합성.
    ///
    /// 이 스위트가 없어서 생긴 사고가 P0의 출발점이었다.
    /// 검증은 EconomyCore 근사식으로 전투를 '다시 구현'했고, 게임은 BattleRunner로 돌았다.
    /// 두 경로가 갈라져도 아무도 몰랐고, 오방색 곱연산이 빠진 채로 30웨이브를 틀리게 봤다.
    ///
    ///     EconomyCore  (순수 공식)
    ///          ↕
    ///     BattleRunner (실제 전투 루프)
    ///          ↕
    ///     UpgradeTracks (구매 정책)
    ///
    /// 여기서 검증하는 것은 "숫자가 맞는가"가 아니라 "세 계층이 같은 답을 내는가"다.
    /// 어느 한쪽만 고치면 반드시 여기서 걸린다.
    ///
    /// docs/P0_검증스위트_재작성_계획.md 3.4
    /// </summary>
    public class CrossLayerTests
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

        // ═══════════════════════════════════════════════════
        //  EconomyCore  ↔  BattleRunner
        // ═══════════════════════════════════════════════════

        [Test]
        public void 웨이브_소요시간이_EconomyCore_공식과_일치한다()
        {
            const double dt = 0.01;

            foreach (int level in new[] { 20, 60, 120 })
            {
                var dps = EconomyCore.BaseDpsAtLevel(cfg, level);

                foreach (int wave in new[] { 1, 10, 30, 50 })
                {
                    double expected = EconomyCore.WaveClearSeconds(cfg, wave, dps);
                    if (expected > cfg.waveTimeWall) continue;   // 벽 구간은 다른 테스트가 본다

                    var runner = new BattleRunner(cfg);
                    runner.BeginRun(wave, BigNumber.Zero);
                    runner.AttackMultiplier = 1.0;
                    runner.TalismanMultiplier = 1.0;

                    long guard = 0;
                    while (runner.CurrentWave == wave && runner.IsRunning && guard++ < 1000000)
                        runner.Tick(dt, level);

                    Assert.AreEqual(wave + 1, runner.CurrentWave,
                        $"레벨 {level}, 웨이브 {wave}: 클리어되지 않았습니다");
                    Assert.AreEqual(expected, runner.RunElapsed, dt * 2,
                        $"레벨 {level}, 웨이브 {wave}: 공식 {expected:F3}초 vs 실측 {runner.RunElapsed:F3}초");
                }
            }
        }

        [Test]
        public void 벽_판정이_EconomyCore_IsWall과_일치한다()
        {
            const double dt = 0.01;
            int checkedPairs = 0;

            foreach (int level in new[] { 0, 10, 40, 80 })
            {
                var dps = EconomyCore.BaseDpsAtLevel(cfg, level);

                foreach (int wave in new[] { 20, 40, 45, 48, 50, 60, 80 })
                {
                    bool formulaSaysWall = EconomyCore.IsWall(cfg, wave, dps);

                    var runner = new BattleRunner(cfg);
                    runner.BeginRun(wave, BigNumber.Zero);
                    runner.AttackMultiplier = 1.0;
                    runner.TalismanMultiplier = 1.0;
                    runner.Tick(dt, level);

                    // 한 틱에 클리어된 경우는 벽이 아닌 것이 자명하다.
                    bool runnerSaysWall = runner.IsWalled;

                    Assert.AreEqual(formulaSaysWall, runnerSaysWall,
                        $"레벨 {level}, 웨이브 {wave}: 공식 {formulaSaysWall} vs 러너 {runnerSaysWall}");
                    checkedPairs++;
                }
            }

            Assert.Greater(checkedPairs, 20, "검증한 조합이 너무 적습니다");
        }

        [Test]
        public void 누적_코인이_CumulativeCoin_공식과_일치한다()
        {
            const double dt = 0.05;
            const int level = 200;      // 벽에 안 걸릴 만큼 충분히 높게
            const int target = 55;

            foreach (double coinMul in new[] { 1.0, 3.5, 42.0 })
            {
                var runner = new BattleRunner(cfg);
                runner.BeginRun(1, BigNumber.Zero);
                runner.AttackMultiplier = 1.0;
                runner.CoinMultiplier = coinMul;
                runner.TalismanMultiplier = 1.0;

                long guard = 0;
                while (runner.CurrentWave <= target && runner.IsRunning && guard++ < 5000000)
                    runner.Tick(dt, level);

                Assert.IsFalse(runner.IsWalled,
                    $"레벨 {level}로 웨이브 {target}까지 못 갔습니다 — 테스트 전제가 깨졌습니다");

                var expected = EconomyCore.CumulativeCoin(cfg, target, coinMul);
                double rel = Math.Abs((runner.Coin - expected).ToDouble() / expected.ToDouble());

                Assert.Less(rel, 1e-9,
                    $"배수 {coinMul}: 공식 {expected} vs 러너 {runner.Coin} (상대오차 {rel:E2})");
            }
        }

        [Test]
        public void 부적은_도달웨이브를_바꾸지_않고_시간만_줄인다()
        {
            // CLAUDE.md 부적 원칙 — "조작은 속도를 바꾸되 도달점을 바꾸지 않는다".
            // 벽 판정이 BaseDpsWithoutTalisman으로 이뤄지는 것이 그 장치이며,
            // 여기서 그 계약이 실제로 지켜지는지 확인한다.
            var plain = RunFull(talismanMul: 1.0);
            var buffed = RunFull(talismanMul: 1.5);

            Assert.AreEqual(plain.wave, buffed.wave,
                $"부적이 도달 웨이브를 바꿨습니다: {plain.wave} -> {buffed.wave}");
            Assert.AreEqual(plain.coin, buffed.coin,
                "부적이 코인 총량을 바꿨습니다");
            Assert.Less(buffed.seconds, plain.seconds,
                $"부적이 시간을 줄이지 못했습니다: {plain.seconds:F1}초 -> {buffed.seconds:F1}초");

            double saved = 1.0 - buffed.seconds / plain.seconds;
            TestContext.WriteLine($"부적 1.5배 → 도달 {plain.wave} 유지, 소요 시간 {saved:P1} 단축");
        }

        private (int wave, string coin, double seconds) RunFull(double talismanMul)
        {
            var runner = new BattleRunner(cfg);
            var tracks = new UpgradeTracks(cfg);
            runner.BeginRun(1, BigNumber.Zero);
            runner.AttackMultiplier = tracks.CombatMultiplier;
            runner.CoinMultiplier = tracks.CoinMultiplier;
            runner.TalismanMultiplier = talismanMul;

            long guard = 0;
            while (runner.IsRunning && guard++ < 5000000)
            {
                while (tracks.BuyBest(runner.Coin, out var cost))
                {
                    runner.SpendCoin(cost);
                    runner.AttackMultiplier = tracks.CombatMultiplier;
                    runner.CoinMultiplier = tracks.CoinMultiplier;
                }
                runner.Tick(0.05, tracks.TotalLevel);
            }
            return (runner.DeepestWave, runner.Coin.ToString(4), runner.RunElapsed);
        }

        // ═══════════════════════════════════════════════════
        //  EconomyCore  ↔  UpgradeTracks
        // ═══════════════════════════════════════════════════

        [Test]
        public void 구매_총레벨이_AffordableLevel과_일치한다()
        {
            // NextCost가 TotalLevel 기준(트랙 간 비용 곡선 공유)이므로
            // 총 레벨은 코인만으로 결정되어야 한다. 이 전제가 깨지면
            // MaxClearableWave와 오프라인 상한이 함께 틀어진다.
            foreach (double magnitude in new[] { 1e2, 1e4, 1e6, 1e9, 1e14 })
            {
                var coin = new BigNumber(magnitude);

                var tracks = new UpgradeTracks(cfg);
                var purse = coin;
                while (tracks.BuyBest(purse, out var cost)) purse -= cost;

                int expected = EconomyCore.AffordableLevel(cfg, coin);

                Assert.LessOrEqual(Math.Abs(tracks.TotalLevel - expected), 1,
                    $"코인 {magnitude:E0}: 실제 구매 {tracks.TotalLevel}레벨 vs 공식 {expected}레벨");
            }
        }

        [Test]
        public void CombatMultiplier가_EconomyCore_공식과_일치한다()
        {
            var tracks = new UpgradeTracks(cfg);
            var purse = new BigNumber(1e8);
            while (tracks.BuyBest(purse, out var cost)) purse -= cost;

            var levels = tracks.Snapshot();

            Assert.AreEqual(EconomyCore.CombatMultiplier(levels), tracks.CombatMultiplier, 1e-12,
                "UpgradeTracks와 EconomyCore의 전투 배수가 다릅니다");
            Assert.AreEqual(EconomyCore.CoinTrackMultiplier(levels), tracks.CoinMultiplier, 1e-12,
                "UpgradeTracks와 EconomyCore의 코인 배수가 다릅니다");
            Assert.AreEqual(EconomyCore.DefenseMultiplier(levels), tracks.DefenseMultiplier, 1e-12,
                "UpgradeTracks와 EconomyCore의 방어 배수가 다릅니다");
        }

        // ═══════════════════════════════════════════════════
        //  공식 내부 정합성
        // ═══════════════════════════════════════════════════

        [Test]
        public void CumulativeCoin과_WaveFromCumulativeCoin이_역함수다()
        {
            foreach (double coinMul in new[] { 1.0, 7.0, 130.0 })
            {
                foreach (int wave in new[] { 5, 30, 80, 150, 220 })
                {
                    var coin = EconomyCore.CumulativeCoin(cfg, wave, coinMul);
                    double back = EconomyCore.WaveFromCumulativeCoin(cfg, coin, coinMul);

                    Assert.AreEqual(wave, back, 1e-6,
                        $"배수 {coinMul}, 웨이브 {wave}: 역산 결과 {back:F6}");
                }
            }
        }

        // ═══════════════════════════════════════════════════
        //  MaxClearableWave  ↔  BattleRunner
        // ═══════════════════════════════════════════════════

        [Test]
        public void MaxClearableWave가_실제_돌파경계와_일치한다()
        {
            foreach (double atk in new[] { 1.0, 15.6, 123.9, 737.6 })
            {
                foreach (double magnitude in new[] { 1e4, 1e8, 1e12 })
                {
                    var coin = new BigNumber(magnitude);
                    int w = EconomyCore.MaxClearableWave(cfg, coin, atk);

                    Assert.IsTrue(Clears(w, coin, atk),
                        $"전투배수 {atk}, 코인 {magnitude:E0}: 경계 {w}를 못 넘습니다");

                    if (w < cfg.maxWavePerRun)
                        Assert.IsFalse(Clears(w + 1, coin, atk),
                            $"전투배수 {atk}, 코인 {magnitude:E0}: 경계가 {w}인데 {w + 1}도 넘습니다");
                }
            }
        }

        private bool Clears(int wave, BigNumber coin, double atkMul)
        {
            var runner = new BattleRunner(cfg);
            var tracks = new UpgradeTracks(cfg);
            runner.BeginRun(wave, coin);
            runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;

            while (tracks.BuyBest(runner.Coin, out var cost))
            {
                runner.SpendCoin(cost);
                runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;
            }

            runner.Tick(0.01, tracks.TotalLevel);
            return !runner.IsWalled;
        }
    }
}

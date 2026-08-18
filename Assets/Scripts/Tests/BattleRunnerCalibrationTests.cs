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
    /// 계측 하네스 — P0 검증 스위트 재작성의 근거 수집용.
    ///
    /// 판정이 아니라 계측이다. 실제 BattleRunner를 GameController와 동일하게
    /// 배선해 구동하고, 수치를 표로 남긴다.
    ///
    ///     BeginNewRun : Tracks.ResetForRebirth() 후 웨이브/코인 지정
    ///     AttackMultiplier = 코어·티어 배수 x Tracks.CombatMultiplier
    ///     Tick(dt, Tracks.TotalLevel)
    ///
    /// 실제 순환은 "접속 1회 = 오프라인 수령 + 런 1회"다.
    /// 세션 안에서 환생하면 웨이브 1·코인 0으로 시작하고,
    /// 오프라인 보상은 앱을 다시 열 때만 적용된다. (GameController 참조)
    ///
    /// docs/P0_검증스위트_재작성_계획.md · docs/P0_계측결과_1차.md
    /// </summary>
    public class BattleRunnerCalibrationTests
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

        private struct RunResult
        {
            public int StartWave;
            public int ReachedWave;
            public double RunSeconds;
            public long Ticks;
            public double WallMs;
            public int TotalLevel;
            public double CombatMul;
        }

        /// <summary>런 1회를 실제 BattleRunner로 끝까지 돌린다.</summary>
        private RunResult RunOnce(double dt,
                                  int startWave = 1,
                                  BigNumber startCoin = default,
                                  double coreAtkMul = 1.0,
                                  double coreCoinMul = 1.0,
                                  bool applyCombatMul = true,
                                  bool blueOnly = false,
                                  long maxTicks = 20000000)
        {
            var runner = new BattleRunner(cfg);
            var tracks = new UpgradeTracks(cfg);

            runner.BeginRun(startWave, startCoin);
            Refresh(runner, tracks, coreAtkMul, coreCoinMul, applyCombatMul);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long ticks = 0;

            while (runner.IsRunning && ticks < maxTicks)
            {
                while (TryBuy(tracks, runner.Coin, blueOnly, out var cost))
                {
                    runner.SpendCoin(cost);
                    Refresh(runner, tracks, coreAtkMul, coreCoinMul, applyCombatMul);
                }

                runner.Tick(dt, tracks.TotalLevel);
                ticks++;
            }

            sw.Stop();

            return new RunResult
            {
                StartWave = startWave,
                ReachedWave = runner.DeepestWave,
                RunSeconds = runner.RunElapsed,
                Ticks = ticks,
                WallMs = sw.Elapsed.TotalMilliseconds,
                TotalLevel = tracks.TotalLevel,
                CombatMul = tracks.CombatMultiplier,
            };
        }

        private static bool TryBuy(UpgradeTracks tracks, BigNumber coin,
                                   bool blueOnly, out BigNumber cost)
            => blueOnly
                ? tracks.TryBuy(EconomyCore.Track.Blue, coin, out cost)
                : tracks.BuyBest(coin, out cost);

        /// <summary>
        /// applyCombatMul=false 는 기존(무효화된) 검증이 쓰던 모델이다.
        /// 오방색 곱연산 항이 통째로 빠진다.
        /// </summary>
        private static void Refresh(BattleRunner runner, UpgradeTracks tracks,
                                    double coreAtkMul, double coreCoinMul,
                                    bool applyCombatMul)
        {
            runner.AttackMultiplier = applyCombatMul
                ? coreAtkMul * tracks.CombatMultiplier
                : coreAtkMul;
            runner.CoinMultiplier = applyCombatMul
                ? coreCoinMul * tracks.CoinMultiplier
                : coreCoinMul;
        }

        // ═════════════════════════════════════════════════════════
        //  ② 오프라인 포함 실제 순환
        // ═════════════════════════════════════════════════════════

        /// <summary>
        /// 접속 1회 = 오프라인 수령 + 런 1회. 실제 플레이어가 겪는 런 시간을 잰다.
        /// 앞선 계측(오프라인 없음)에서 첫 런 14.26분이 나왔던 것을 재확인한다.
        /// </summary>
        [Test]
        public void 계측_오프라인_포함_런시간()
        {
            foreach (bool watchAd in new[] { false, true })
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== 오프라인 포함 순환 (3런/일, 자리비움 8h, 상한 {cfg.offlineCapHours}h, " +
                              $"광고 {(watchAd ? "시청" : "미시청")}, dt=0.02) ===");
                sb.AppendLine("회차 | 티어 |    코어 | 전투배수 | 시작 | 도달 | 헤드룸 | 런시간(분) | 비고");

                double cores = 0; int tier = 1, lastWave = 1, runsToday = 1;
                double sumMin = 0; double maxMin = 0;
                int minHeadroom = int.MaxValue, worstRun = 0;

                for (int k = 1; k <= 60; k++)
                {
                    double atk = EconomyCore.AttackMultiplier(cfg, cores, tier);
                    double coin = EconomyCore.CoinMultiplier(cfg, cores, tier);

                    var off = EconomyCore.CalculateOffline(
                        cfg, 8.0, lastWave, coin, watchAd, cfg.offlineCapHours, atk);
                    int start = Math.Max(1, (int)off.StartWave);

                    var r = RunOnce(0.02, start, off.Coin, atk, coin);
                    double min = r.RunSeconds / 60.0;
                    sumMin += min; if (min > maxMin) maxMin = min;

                    // 헤드룸 = 시작점과 실제 한계 사이의 여유.
                    // 승천으로 전투력이 떨어지면 여기가 붕괴하고 런이 즉시 끝난다.
                    // 처방 판단을 위해 전 회차를 출력한다 (샘플링 없음).
                    int headroom = r.ReachedWave - start;
                    if (headroom < minHeadroom) { minHeadroom = headroom; worstRun = k; }

                    string note = headroom < 5 ? "<< 헤드룸 부족" : (min < 3.0 ? "<< 런 3분 미만" : "");
                    sb.AppendLine(
                        $"{k,4} | {tier,4} | {cores,7:N0} | {atk,8:F1} | {start,4} | {r.ReachedWave,4} | " +
                        $"{headroom,6} | {min,10:F2} | {note}");

                    cores += EconomyCore.CoreGainWithDecay(cfg, r.ReachedWave, runsToday);
                    lastWave = r.ReachedWave;
                    runsToday = runsToday >= 3 ? 1 : runsToday + 1;

                    if (EconomyCore.CanAscend(cfg, tier, r.ReachedWave, cores))
                    {
                        double before = cores;
                        tier++;
                        cores = EconomyCore.CoresAfterAscend(cfg, cores);
                        sb.AppendLine(
                            $"     >>> 승천 티어 {tier - 1} -> {tier} : 코어 {before,-8:N0} -> {cores:N0}, " +
                            $"전투배수 {EconomyCore.AttackMultiplier(cfg, before, tier - 1),0:F1} -> " +
                            $"{EconomyCore.AttackMultiplier(cfg, cores, tier),0:F1}");
                    }
                }

                sb.AppendLine($"평균 {sumMin / 60.0:F2}분 | 최대 {maxMin:F2}분 | " +
                              $"최소 헤드룸 {minHeadroom}웨이브 (회차 {worstRun})");
                Debug.Log(sb.ToString());
                TestContext.WriteLine(sb.ToString());
            }
            Assert.Pass();
        }

        // ═════════════════════════════════════════════════════════
        //  ③-1 곱연산 유무 대조군 (1차 계측의 오류 정정)
        // ═════════════════════════════════════════════════════════

        /// <summary>
        /// 기존 실패 검증이 쓰던 모델은 CombatMultiplier 항이 통째로 빠져 있었다.
        /// "5트랙 vs 1트랙"이 아니라 "곱연산 있음 vs 없음"이 진짜 격차다.
        /// </summary>
        [Test]
        public void 계측_곱연산_유무_대조()
        {
            const double dt = 0.05;

            var sb = new StringBuilder();
            sb.AppendLine("=== 곱연산 유무 대조 (오프라인 없음, 코어 0, 티어 1) ===");
            sb.AppendLine("모델                          | 도달웨이브 | 총레벨 | 전투배수");

            var full = RunOnce(dt);
            var blue = RunOnce(dt, blueOnly: true);
            var noMul = RunOnce(dt, applyCombatMul: false);

            sb.AppendLine($"실제 (5트랙 x 곱연산)          | {full.ReachedWave,10} | {full.TotalLevel,6} | {full.CombatMul,8:F1}");
            sb.AppendLine($"청 트랙만 (곱연산 유지)        | {blue.ReachedWave,10} | {blue.TotalLevel,6} | {blue.CombatMul,8:F1}");
            sb.AppendLine($"구 검증 모델 (곱연산 없음)     | {noMul.ReachedWave,10} | {noMul.TotalLevel,6} | {1.0,8:F1}");
            sb.AppendLine($"실제 - 구모델 = {full.ReachedWave - noMul.ReachedWave} 웨이브");

            Debug.Log(sb.ToString());
            TestContext.WriteLine(sb.ToString());
            Assert.Greater(full.ReachedWave, noMul.ReachedWave);
        }

        // ═════════════════════════════════════════════════════════
        //  ③-2 300회차 확장 + 설계 곡선 재적합
        // ═════════════════════════════════════════════════════════

        /// <summary>
        /// 300회차(3런/일 = 100일)까지 오프라인 포함으로 돌린다.
        /// 마스터문서 90일 커브 표와 대조하고,
        /// 실측에 맞는 waveCoefficient / waveExponent를 로그-로그 최소자승으로 적합한다.
        ///
        /// 적합값은 보고만 한다. 수치 변경은 승인 후에. (CLAUDE.md)
        /// </summary>
        [Test]
        public void 계측_300회차_확장_및_곡선적합()
        {
            const double dt = 0.25;   // 도달 웨이브는 dt와 무관함이 1차 계측에서 확인됨
            const int runs = 300;

            int[] milestones = { 3, 9, 21, 42, 63, 90, 135, 180, 225, 270, 300 };

            var sb = new StringBuilder();
            sb.AppendLine("=== 300회차 확장 (3런/일, 자리비움 8h, 광고 미시청) ===");
            sb.AppendLine("회차 | 일차 | 티어 |     코어 | 도달웨이브 | 설계웨이브 | 비율");

            double cores = 0; int tier = 1, lastWave = 1, runsToday = 1;
            double sumLnK = 0, sumLnW = 0, sumLnKLnW = 0, sumLnK2 = 0;

            for (int k = 1; k <= runs; k++)
            {
                double atk = EconomyCore.AttackMultiplier(cfg, cores, tier);
                double coin = EconomyCore.CoinMultiplier(cfg, cores, tier);

                var off = EconomyCore.CalculateOffline(
                    cfg, 8.0, lastWave, coin, false, cfg.offlineCapHours, atk);
                int start = Math.Max(1, (int)off.StartWave);

                var r = RunOnce(dt, start, off.Coin, atk, coin);

                // 로그-로그 최소자승 누적:  ln(w) = ln(a) + b * ln(k+1)
                double lnK = Math.Log(k + 1.0);
                double lnW = Math.Log(Math.Max(1, r.ReachedWave));
                sumLnK += lnK; sumLnW += lnW;
                sumLnKLnW += lnK * lnW; sumLnK2 += lnK * lnK;

                int design = EconomyCore.TargetWave(cfg, k);
                if (Array.IndexOf(milestones, k) >= 0)
                    sb.AppendLine(
                        $"{k,4} | {k / 3,4} | {tier,4} | {cores,8:N0} | {r.ReachedWave,10} | " +
                        $"{design,10} | {(design > 0 ? (double)r.ReachedWave / design : 0),5:F2}");

                cores += EconomyCore.CoreGainWithDecay(cfg, r.ReachedWave, runsToday);
                lastWave = r.ReachedWave;
                runsToday = runsToday >= 3 ? 1 : runsToday + 1;

                if (EconomyCore.CanAscend(cfg, tier, r.ReachedWave, cores))
                {
                    tier++;
                    cores = EconomyCore.CoresAfterAscend(cfg, cores);
                }
            }

            double n = runs;
            double b = (n * sumLnKLnW - sumLnK * sumLnW) / (n * sumLnK2 - sumLnK * sumLnK);
            double a = Math.Exp((sumLnW - b * sumLnK) / n);

            sb.AppendLine();
            sb.AppendLine("--- 로그-로그 최소자승 적합  w = a x (회차+1)^b ---");
            sb.AppendLine($"  적합 a (waveCoefficient) : {a:F2}   (현재 {cfg.waveCoefficient})");
            sb.AppendLine($"  적합 b (waveExponent)    : {b:F4}   (현재 {cfg.waveExponent})");
            sb.AppendLine($"  적합식 기준 회차 270 도달 : {a * Math.Pow(271, b):F0} 웨이브");
            sb.AppendLine($"  현재식 기준 회차 270 도달 : {EconomyCore.TargetWave(cfg, 270)} 웨이브");
            sb.AppendLine($"  최종 티어 : {tier}");

            Debug.Log(sb.ToString());
            TestContext.WriteLine(sb.ToString());
            Assert.Greater(tier, 1);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// sim — 실제 BattleRunner 90일 순환.
    ///
    /// GameController와 동일 배선으로 4인 페르소나를 돌린다.
    ///   접속 1회 = 오프라인 수령 + 런 1회 / 환생 시 Tracks.ResetForRebirth()
    ///
    /// ★ 설계곡선 TargetWave(k)에 대한 주의
    ///   a=83.92 / b=0.1849는 실측 궤적에 적합된 런 인덱스 기준선이다.
    ///   실측(2026-08): 회차 30/90/180/270/540에서 비율 1.05/1.04/1.01/0.99/0.96.
    ///
    ///   일일 런 수가 코어 감쇠 소프트캡(6) 이하인 코호트는 빈도와 무관하게
    ///   같은 회차에서 같은 웨이브에 도달한다 (일반 3런/일 = 헤비 6런/일, 실측 확인).
    ///   소프트캡을 넘는 코호트(7런/일 이상)는 감쇠로 회차당 진행이 느려지므로
    ///   이 곡선을 그대로 적용하면 안 된다.
    ///
    ///   코호트끼리 비교할 때는 런 축이 아니라 날짜 축을 쓴다.
    ///   같은 90일에 라이트는 90런, 헤비는 540런을 돌기 때문이다.
    ///
    /// ★ 여유(slack)에 대한 주의
    ///   "설계 목표 웨이브를 현재 DPS로 45초 안에 깰 수 있는가"는 웨이브 차이를
    ///   지수적으로 증폭한다(HP가 웨이브당 9% 증가하므로 8웨이브 차이 = 여유 0.50).
    ///   재적합 곡선의 정상적인 진동이 '미달'로 잡힌다.
    ///   따라서 slack은 진단용으로만 유지하고 판정에는 쓰지 않는다.
    ///   설계 대비 진행은 웨이브 비율로 잰다.
    ///
    /// docs/P0_검증스위트_재작성_계획.md 3.4 · docs/P0_계측결과_2차.md
    /// </summary>
    public class SimulationTests
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

        private struct Persona
        {
            public string Name;
            public int LoginsPerDay;
            public double HoursAway;
            public bool WatchAd;
        }

        private static readonly Persona[] Personas =
        {
            new Persona { Name = "라이트",   LoginsPerDay = 1, HoursAway = 24.0, WatchAd = false },
            new Persona { Name = "일반",     LoginsPerDay = 3, HoursAway = 8.0,  WatchAd = false },
            new Persona { Name = "일반광고", LoginsPerDay = 3, HoursAway = 8.0,  WatchAd = true  },
            new Persona { Name = "헤비",     LoginsPerDay = 6, HoursAway = 4.0,  WatchAd = true  },
        };

        private static readonly int[] DayMarks = { 30, 60, 90 };
        private static readonly int[] RunMarks = { 30, 90, 180, 270, 540 };
        private const int SegmentSize = 30;

        private struct Mark
        {
            public int Index, Tier, Wave, BestWave;
            public double Cores;
        }

        private struct Segment
        {
            public int From, To, BestStart, BestEnd, Ascends;
            public double AvgMinutes;
            public int Gain => BestEnd - BestStart;
        }

        private class Result
        {
            public string Name;
            public int Runs, Tier, Wave, BestWave, Ascends;
            public double Cores, AvgMinutes, MaxMinutes;

            // 진단용 — 판정에 쓰지 않는다
            public double MinSlack = double.MaxValue;
            public int MinSlackRun, SubOneRuns;

            public readonly Dictionary<int, Mark> ByDay = new Dictionary<int, Mark>();
            public readonly Dictionary<int, Mark> ByRun = new Dictionary<int, Mark>();
            public readonly List<Segment> Segments = new List<Segment>();
            /// <summary>승천마다 최고 웨이브를 되찾는 데 걸린 런 수.</summary>
            public readonly List<int> RecoveryRuns = new List<int>();
        }

        private Result Simulate(Persona p, int days = 90, double dt = 0.25)
        {
            var r = new Result { Name = p.Name };

            double cores = 0; int tier = 1, lastWave = 1, runsToday = 1;
            double sumMin = 0, segMin = 0;
            int bestWave = 0, segStartBest = 0, segAscends = 0, segRuns = 0;
            bool recovering = false; int recoverTarget = 0, recoverRuns = 0;

            for (int day = 1; day <= days; day++)
            {
                runsToday = 1;
                for (int login = 0; login < p.LoginsPerDay; login++)
                {
                    r.Runs++;

                    double atk = EconomyCore.AttackMultiplier(cfg, cores, tier);
                    double coin = EconomyCore.CoinMultiplier(cfg, cores, tier);

                    var off = EconomyCore.CalculateOffline(
                        cfg, p.HoursAway, lastWave, coin, p.WatchAd, cfg.offlineCapHours, atk);
                    int start = Math.Max(1, (int)off.StartWave);

                    var runner = new BattleRunner(cfg);
                    var tracks = new UpgradeTracks(cfg);
                    runner.BeginRun(start, off.Coin);
                    runner.AttackMultiplier = atk * tracks.CombatMultiplier;
                    runner.CoinMultiplier = coin * tracks.CoinMultiplier;

                    long guard = 0;
                    while (runner.IsRunning && guard++ < 2000000)
                    {
                        while (tracks.BuyBest(runner.Coin, out var cost))
                        {
                            runner.SpendCoin(cost);
                            runner.AttackMultiplier = atk * tracks.CombatMultiplier;
                            runner.CoinMultiplier = coin * tracks.CoinMultiplier;
                        }
                        runner.Tick(dt, tracks.TotalLevel);
                    }

                    int reached = runner.DeepestWave;
                    double minutes = runner.RunElapsed / 60.0;
                    sumMin += minutes; segMin += minutes; segRuns++;
                    if (minutes > r.MaxMinutes) r.MaxMinutes = minutes;
                    if (reached > bestWave) bestWave = reached;

                    // 진단용 여유 — 판정에 쓰지 않는다 (클래스 주석 참조).
                    var dps = EconomyCore.BaseDpsAtLevel(cfg, tracks.TotalLevel)
                            * atk * tracks.CombatMultiplier;
                    double need = EconomyCore.WaveClearSeconds(
                        cfg, EconomyCore.TargetWave(cfg, r.Runs), dps);
                    double slack = need > 0 ? cfg.waveTimeWall / need : double.MaxValue;
                    if (slack < r.MinSlack) { r.MinSlack = slack; r.MinSlackRun = r.Runs; }
                    if (slack < 1.0) r.SubOneRuns++;

                    if (recovering)
                    {
                        recoverRuns++;
                        if (reached >= recoverTarget)
                        { r.RecoveryRuns.Add(recoverRuns); recovering = false; }
                    }

                    cores += EconomyCore.CoreGainWithDecay(cfg, reached, runsToday);
                    lastWave = reached;
                    runsToday++;

                    if (EconomyCore.CanAscend(cfg, tier, reached, cores))
                    {
                        tier++;
                        cores = EconomyCore.CoresAfterAscend(cfg, cores);
                        r.Ascends++; segAscends++;
                        recovering = true; recoverTarget = bestWave; recoverRuns = 0;
                    }

                    r.Tier = tier; r.Wave = reached; r.Cores = cores; r.BestWave = bestWave;

                    if (Array.IndexOf(RunMarks, r.Runs) >= 0)
                        r.ByRun[r.Runs] = new Mark
                        { Index = r.Runs, Tier = tier, Wave = reached, BestWave = bestWave, Cores = cores };

                    if (segRuns == SegmentSize)
                    {
                        r.Segments.Add(new Segment
                        {
                            From = r.Runs - SegmentSize + 1, To = r.Runs,
                            BestStart = segStartBest, BestEnd = bestWave,
                            AvgMinutes = segMin / segRuns, Ascends = segAscends,
                        });
                        segStartBest = bestWave; segMin = 0; segRuns = 0; segAscends = 0;
                    }
                }

                if (Array.IndexOf(DayMarks, day) >= 0)
                    r.ByDay[day] = new Mark
                    { Index = day, Tier = tier, Wave = lastWave, BestWave = bestWave, Cores = cores };
            }

            r.AvgMinutes = sumMin / r.Runs;
            return r;
        }

        // ═══════════════════════════════════════════════════
        //  계측 (판정 없음)
        // ═══════════════════════════════════════════════════

        [Test]
        public void 계측_페르소나_90일_리포트()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 종합 ===");
            sb.AppendLine("페르소나 | 런수 | 티어 | 최고웨이브 |    코어 | 평균분 | 최대분 | 승천 | 기록회복런");
            var all = new List<Result>();
            foreach (var p in Personas)
            {
                var r = Simulate(p); all.Add(r);
                string rec = r.RecoveryRuns.Count == 0 ? "-" : string.Join(",", r.RecoveryRuns);
                sb.AppendLine(
                    $"{r.Name,-8} | {r.Runs,4} | {r.Tier,4} | {r.BestWave,10} | {r.Cores,7:N0} | " +
                    $"{r.AvgMinutes,6:F2} | {r.MaxMinutes,6:F2} | {r.Ascends,4} | {rec}");
            }

            sb.AppendLine();
            sb.AppendLine("=== 날짜 기준 (코호트 비교는 이 축으로) ===");
            sb.AppendLine("페르소나 |  일차 | 티어 | 최고웨이브 |    코어");
            foreach (var r in all)
                foreach (int d in DayMarks)
                    if (r.ByDay.TryGetValue(d, out var m))
                        sb.AppendLine($"{r.Name,-8} | {d,5} | {m.Tier,4} | {m.BestWave,10} | {m.Cores,7:N0}");

            sb.AppendLine();
            sb.AppendLine("=== 런 기준 (설계곡선 대조) ===");
            sb.AppendLine("페르소나 |  회차 | 티어 | 최고웨이브 | 설계 | 비율");
            foreach (var r in all)
                foreach (int k in RunMarks)
                    if (r.ByRun.TryGetValue(k, out var m))
                    {
                        int design = EconomyCore.TargetWave(cfg, k);
                        sb.AppendLine($"{r.Name,-8} | {k,5} | {m.Tier,4} | {m.BestWave,10} | " +
                                      $"{design,4} | {(double)m.BestWave / design,5:F2}");
                    }

            sb.AppendLine();
            sb.AppendLine("=== 승천 후 회복 — 두 지표는 서로 다르다 ===");
            sb.AppendLine("  ① 전투 헤드룸 회복 : 1런 (OfflinePowerCapTests가 고정)");
            sb.AppendLine("     승천 직후 런이 짧아졌다가 다음 런에 정상 길이로 돌아온다.");
            sb.AppendLine("  ② 최고기록 회복    : 아래 '기록회복런' 열");
            sb.AppendLine("     승천 이전 최고 웨이브를 다시 넘는 데 걸리는 런 수.");
            sb.AppendLine("     승천이 거듭될수록 길어진다. UX 관찰 지표이며 판정하지 않는다.");
            sb.AppendLine();
            sb.AppendLine("=== 진단용 여유 (판정에 쓰지 않음) ===");
            foreach (var r in all)
                sb.AppendLine($"{r.Name,-8} | 최소 {r.MinSlack:F3}(회차 {r.MinSlackRun}) | " +
                              $"1.0 미만 {r.SubOneRuns}/{r.Runs}회");

            Debug.Log(sb.ToString());
            TestContext.WriteLine(sb.ToString());
            Assert.Pass();
        }

        /// <summary>
        /// 구간별 최고 웨이브 증가 추이. 정체 판정 기준의 근거 데이터다.
        /// 최고 웨이브(rollingBest)를 쓰는 이유는 승천 직후 현재 웨이브 하락이 정상이기 때문.
        /// </summary>
        [Test]
        public void 계측_구간별_진행추이()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {SegmentSize}런 구간별 최고 웨이브 증가 ===");
            sb.AppendLine("페르소나 |    구간 | 최고웨이브 | 증가 | 평균분 | 승천");
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                foreach (var s in r.Segments)
                    sb.AppendLine($"{r.Name,-8} | {s.From,3}~{s.To,3} | {s.BestEnd,10} | " +
                                  $"{s.Gain,4} | {s.AvgMinutes,6:F2} | {s.Ascends,4}");
            }
            Debug.Log(sb.ToString());
            TestContext.WriteLine(sb.ToString());
            Assert.Pass();
        }

        // ═══════════════════════════════════════════════════
        //  계약
        // ═══════════════════════════════════════════════════

        [Test]
        public void 설계곡선이_런인덱스_기준으로_10퍼센트_이내로_맞는다()
        {
            var r = Simulate(Personas[1]);

            foreach (int k in RunMarks)
            {
                if (!r.ByRun.TryGetValue(k, out var m)) continue;
                if (k < 30) continue;   // 온보딩 제외

                int design = EconomyCore.TargetWave(cfg, k);
                double ratio = (double)m.BestWave / design;
                TestContext.WriteLine($"회차 {k}: 실측 {m.BestWave} vs 설계 {design} ({ratio:F3})");

                Assert.Greater(ratio, 0.90, $"회차 {k}: 실측 {m.BestWave}이 설계 {design}에 못 미칩니다");
                Assert.Less(ratio, 1.10, $"회차 {k}: 실측 {m.BestWave}이 설계 {design}를 앞섭니다");
            }
        }

        /// <summary>
        /// 최고 웨이브(rollingBest)가 60런 연속으로 갱신되지 않는 구간이 있는가.
        ///
        /// ★ 60런은 "설계상 허용되는 회복 한도"가 아니다.
        ///   현재 90일 실측에서 연속 무증가 구간이 관측되지 않았다는 사실을 근거로 삼은
        ///   **회귀 감시 기준선**이다. 이 숫자를 밸런스 규칙으로 승격하지 말 것.
        ///
        /// 실측(2026-08, 30런 구간별 최고 웨이브 증가폭)
        ///   일반 270런: 166 / 21 / 14 / 6 / 6 / 8 / 6 / 4 / 2
        ///   헤비 540런: 위와 동일 + 0 / 5 / 5 / 3 / 3 / 2 / 3 / 1 / 2
        ///   증가 0인 구간은 헤비 271~300 하나뿐이고 직전 구간에 승천이 있었다.
        ///   즉 그 0은 정체가 아니라 승천 후 기록 회복 구간이다.
        ///   데이터가 말하는 것은 "연속 0 구간이 없었다"까지이며,
        ///   "60런까지는 정상"을 증명하지는 않는다.
        ///
        /// 무엇을 재는가 — 게임이 정체했는가라는 판단이 아니라,
        /// rollingBest가 60런 동안 한 번도 갱신되지 않았다는 관측 가능한 사실이다.
        ///
        /// 현재 웨이브가 아니라 최고 웨이브를 쓰는 이유 —
        /// 승천 직후 현재 웨이브가 173 -> 157로 떨어지는 것은 정상이며 기록은 유지된다.
        /// 현재 웨이브로 재면 정상적인 승천 하락을 정체로 오판한다.
        /// </summary>
        [Test]
        public void 최고웨이브가_60런_연속_증가하지_않는_구간이_없다()
        {
            foreach (var p in Personas)
            {
                var r = Simulate(p);

                int run = 0, worst = 0, worstFrom = 0;
                foreach (var s in r.Segments)
                {
                    if (s.Gain == 0)
                    {
                        if (run == 0) worstFrom = s.From;
                        run++;
                        if (run > worst) worst = run;
                    }
                    else run = 0;
                }

                TestContext.WriteLine(
                    $"{r.Name}: 최장 무증가 {worst}구간 ({worst * SegmentSize}런)" +
                    (worst > 0 ? $" 시작 회차 {worstFrom}" : ""));

                Assert.Less(worst, 2,
                    $"{r.Name}: 회차 {worstFrom}부터 {worst * SegmentSize}런 동안 " +
                    "최고 웨이브가 한 번도 갱신되지 않았습니다. " +
                    "실측 기준선(연속 무증가 0구간)에서 벗어났으므로 원인을 확인하세요 — " +
                    "승천 후 기록 회복이 길어진 것인지, 성장이 실제로 멈춘 것인지 구분이 필요합니다");
            }
        }

        [Test]
        public void _90일차에_티어6에_도달한다()
        {
            var r = Simulate(Personas[1]);
            Assert.AreEqual(6, r.Tier, $"일반 유저가 90일차에 티어 {r.Tier}입니다 (설계 6)");
        }

        [Test]
        public void _90일_이후에도_남은_티어가_있다()
        {
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                Assert.Less(r.Tier, cfg.tierGates.Length + 1,
                    $"{r.Name}가 90일차에 최대 티어에 도달했습니다 — 다음 목표가 없습니다");
            }
        }

        /// <summary>코호트 비교는 날짜 기준으로 한다 (클래스 주석 참조).</summary>
        [Test]
        public void 헤비유저가_라이트유저를_지나치게_앞서지_않는다()
        {
            var light = Simulate(Personas[0]);
            var heavy = Simulate(Personas[3]);

            double ratio = (double)heavy.BestWave / light.BestWave;
            TestContext.WriteLine(
                $"90일차 — 라이트 T{light.Tier}/W{light.BestWave} vs " +
                $"헤비 T{heavy.Tier}/W{heavy.BestWave} (웨이브 {ratio:F2}배)");

            Assert.Less(ratio, 2.0,
                $"6배 플레이한 헤비가 웨이브 {ratio:F2}배를 앞섭니다 — 코어 감쇠가 약합니다");
            Assert.GreaterOrEqual(heavy.Tier, light.Tier,
                "헤비가 라이트보다 뒤처지면 더 할 이유가 없습니다");
        }

        [Test]
        public void 런이_후반으로_갈수록_길어지지_않는다()
        {
            var r = Simulate(Personas[1]);
            Assert.Less(r.AvgMinutes, 10.0, $"평균 런 시간이 {r.AvgMinutes:F2}분입니다 (목표 5~10분)");
            Assert.Less(r.MaxMinutes, 20.0, $"최장 런이 {r.MaxMinutes:F2}분입니다");
        }

        [Test]
        public void 광고_시청이_런_시간을_단축하고_도달점은_바꾸지_않는다()
        {
            var plain = Simulate(Personas[1]);
            var withAd = Simulate(Personas[2]);

            TestContext.WriteLine(
                $"미시청 {plain.AvgMinutes:F2}분 T{plain.Tier}/W{plain.BestWave} / " +
                $"시청 {withAd.AvgMinutes:F2}분 T{withAd.Tier}/W{withAd.BestWave}");

            Assert.Less(withAd.AvgMinutes, plain.AvgMinutes,
                "광고를 봐도 런 시간이 줄지 않습니다 — 오프라인 2배의 유인이 사라집니다");
            Assert.AreEqual(plain.Tier, withAd.Tier,
                "광고 시청이 티어 진행을 바꿨습니다 — 광고는 속도만 바꿔야 합니다");
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 90일 플레이 시뮬레이션.
    ///
    /// EconomyTests가 "공식이 맞는가"를 검사한다면
    /// 여기는 "실제로 플레이했을 때 경제가 성립하는가"를 검사한다.
    ///
    /// EconomyConfig의 assumedBoostsPerDay 같은 '유저 행동 가정'은
    /// 설정이 아니라 추정치이므로, 그 근거를 여기서 만든다.
    /// 시뮬레이션 결과가 바뀌면 Config의 가정치를 여기에 맞춰 갱신할 것.
    /// </summary>
    public class EconomySimulationTests
    {
        private EconomyConfig cfg;

        [SetUp]
        public void SetUp() => cfg = ScriptableObject.CreateInstance<EconomyConfig>();

        [TearDown]
        public void TearDown()
        {
            if (cfg != null) UnityEngine.Object.DestroyImmediate(cfg);
        }

        // ─────────────────────────────────────────

        private struct Persona
        {
            public string Name;
            public int Seed;              // 명시적 고정 시드
            public int LoginsPerDay;
            public double HoursAway;      // 접속 사이 평균 자리비움
            public double AdRate;         // 리워드 광고 시청률 0~1
            public double BoostAppetite;  // 접속당 부스트 구매 성향 0~1
        }

        private static readonly Persona[] Personas =
        {
            // 시드는 반드시 명시적 상수로 둔다.
            // string.GetHashCode()는 런타임/환경에 따라 값이 달라져
            // "어제는 통과했는데 오늘은 실패"하는 테스트가 된다.
            new Persona { Name = "라이트",   Seed = 1001, LoginsPerDay = 1, HoursAway = 12, AdRate = 0.3, BoostAppetite = 0.3 },
            new Persona { Name = "일반",     Seed = 1002, LoginsPerDay = 3, HoursAway = 8,  AdRate = 0.6, BoostAppetite = 0.6 },
            new Persona { Name = "헤비",     Seed = 1003, LoginsPerDay = 6, HoursAway = 4,  AdRate = 0.7, BoostAppetite = 0.9 },
            new Persona { Name = "광고적극", Seed = 1004, LoginsPerDay = 3, HoursAway = 8,  AdRate = 1.0, BoostAppetite = 0.6 },
        };

        private class Result
        {
            public string Name;
            public int Wave, Tier, Runs, Ads, Boosts, Gems, DeepestWave;
            public int PermanentDoneDay;      // 영구 해금 완료일 (0이면 미완)
            public double Cores, ActiveMinutes;
            public double OfflineCoinShare;
            public readonly Dictionary<int, int> GemsAtDay = new Dictionary<int, int>();
            public readonly Dictionary<int, int> WaveAtDay = new Dictionary<int, int>();
        }

        private Result Simulate(Persona p, int days = 90)
        {
            var r = new Result { Name = p.Name };
            var rng = new System.Random(p.Seed);

            double cores = 0;
            int tier = 1, gems = 0, lastWave = 1;
            int capStep = 0, automationOwned = 0;
            double cap = cfg.offlineCapHours;
            double offShare = 0, activeShare = 0;

            for (int day = 1; day <= days; day++)
            {
                for (int login = 0; login < p.LoginsPerDay; login++)
                {
                    r.Runs++;
                    int wave = EconomyCore.TargetWave(cfg, r.Runs);   // 설계 목표 (참고용)
                    double coinMul = EconomyCore.CoinMultiplier(cfg, cores, tier);
                    double atkMul = EconomyCore.AttackMultiplier(cfg, cores, tier);

                    bool watchAd = rng.NextDouble() < p.AdRate;
                    var off = EconomyCore.CalculateOffline(
                        cfg, p.HoursAway, lastWave, coinMul, watchAd, cap);

                    if (watchAd) r.Ads++;
                    gems += off.Gems + (watchAd ? cfg.gemsPerAd : 0);

                    offShare += off.AppliedRatio;
                    activeShare += 1.0 - off.AppliedRatio;

                    // ── 웨이브 단위 실제 진행 ──
                    // 최종 DPS를 런 시작부터 적용하면 낙관 편향이 생긴다.
                    // 실제 게임처럼 웨이브마다 코인을 벌고 그때그때 업그레이드한다.
                    int startWave = Math.Max(1, (int)off.StartWave);

                    // 오프라인 보상 코인 = startWave까지의 누적 수확분
                    var purse = off.Coin;
                    double seconds = 0;
                    int reached = startWave;

                    for (int w = startWave; w <= cfg.maxWavePerRun; w++)
                    {
                        // 지금 가진 코인으로 살 수 있는 만큼만 강화한다
                        int lv = EconomyCore.AffordableLevel(cfg, purse);
                        var dps = EconomyCore.BaseDpsAtLevel(cfg, lv) * atkMul;

                        double waveSec = EconomyCore.WaveClearSeconds(cfg, w, dps);
                        if (waveSec > cfg.waveTimeWall) break;   // 벽 — 런 종료

                        seconds += waveSec;
                        purse += EconomyCore.WaveCoinReward(cfg, w) * coinMul;
                        reached = w;
                    }

                    r.ActiveMinutes += seconds / 60.0;
                    if (reached > r.DeepestWave) r.DeepestWave = reached;

                    cores += EconomyCore.CoreGain(cfg, reached);
                    lastWave = reached;
                    wave = reached;

                    if (EconomyCore.CanAscend(cfg, tier, wave, cores))
                    {
                        tier++;
                        cores = EconomyCore.CoresAfterAscend(cfg, cores);
                    }
                }

                // 구슬 소비 — 유저는 영구 해금을 먼저 사고 남는 것으로 부스트를 산다
                if (capStep == 0 && gems >= cfg.gemCostCapTier1)
                {
                    gems -= cfg.gemCostCapTier1; capStep = 1; cap = 8.0;
                }
                else if (capStep == 1 && gems >= cfg.gemCostCapTier2)
                {
                    gems -= cfg.gemCostCapTier2; capStep = 2; cap = cfg.offlineCapHoursMax;
                }
                else if (automationOwned < cfg.automationUnlockCount &&
                         gems >= cfg.gemCostAutomation)
                {
                    gems -= cfg.gemCostAutomation; automationOwned++;
                }

                bool permanentDone = capStep >= 2 && automationOwned >= cfg.automationUnlockCount;
                if (permanentDone && r.PermanentDoneDay == 0) r.PermanentDoneDay = day;

                if (permanentDone)
                {
                    double want = p.LoginsPerDay * p.BoostAppetite;
                    int n = (int)want;
                    if (rng.NextDouble() < want - n) n++;
                    int cost = n * cfg.gemCostBoost;
                    if (n > 0 && gems >= cost) { gems -= cost; r.Boosts += n; }
                }

                if (day == 1 || day == 7 || day == 30 || day == 90)
                {
                    r.GemsAtDay[day] = gems;
                    r.WaveAtDay[day] = lastWave;
                }
            }

            r.Wave = lastWave; r.Tier = tier; r.Cores = cores; r.Gems = gems;
            r.OfflineCoinShare = offShare / (offShare + activeShare);
            return r;
        }

        // ─────────────────────────────────────────

        [Test]
        public void 페르소나_90일_전체_리포트()
        {
            Assert.IsTrue(cfg.Validate(out string err), err);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("페르소나 | 웨이브 | 티어 | 코어 | 젬(7일/30일/90일) | 해금완료 | 부스트 | 광고 | 일평균분");
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                sb.AppendLine(
                    $"{r.Name,-8} | {r.Wave,6} | {r.Tier,4} | {r.Cores,8:N0} | " +
                    $"{r.GemsAtDay[7],6:N0}/{r.GemsAtDay[30],6:N0}/{r.GemsAtDay[90],6:N0} | " +
                    $"{(r.PermanentDoneDay > 0 ? r.PermanentDoneDay + "일" : "미완"),6} | " +
                    $"{r.Boosts,5} | {r.Ads,5} | {r.ActiveMinutes / 90.0,7:F1}");
            }
            Debug.Log(sb.ToString());
        }

        [Test]
        public void 모든_페르소나가_구슬을_무한축적하지_않는다()
        {
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                Assert.LessOrEqual(r.Gems, cfg.gemSurplus90Limit,
                    $"{p.Name}: 90일 후 구슬 {r.Gems:N0}개가 남았습니다. 싱크가 부족합니다.");
            }
        }

        [Test]
        public void 모든_페르소나가_90일내_영구해금을_마친다()
        {
            // 라이트 유저도 90일 안에는 편의 기능을 다 열 수 있어야 한다.
            // 못 열면 가격이 너무 비싸거나 구슬 수급이 너무 적은 것이다.
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                Assert.Greater(r.PermanentDoneDay, 0,
                    $"{p.Name}: 90일 안에 영구 해금을 완료하지 못했습니다.");
            }
        }

        [Test]
        public void 일반유저는_한달내_영구해금을_마친다()
        {
            var normal = Array.Find(Personas, x => x.Name == "일반");
            var r = Simulate(normal);
            Assert.LessOrEqual(r.PermanentDoneDay, 35,
                $"일반 유저의 영구 해금이 {r.PermanentDoneDay}일 걸립니다. 너무 느립니다.");
        }

        [Test]
        public void 부스트_구매가_구슬_부족으로_막히지_않는다()
        {
            // assumedBoostsPerDay는 '경제 설정'이 아니라 '목표 행동 모델'이다.
            //
            // 순환논리 주의 — 일반 페르소나는 LoginsPerDay(3) x BoostAppetite(0.6) = 1.8로
            // 이미 의도값이 정해져 있다. 그 결과가 1.8인지 재확인하는 것은 의미가 없다.
            // 여기서 실제로 검사할 것은
            //   "유저가 사고 싶은 만큼 실제로 살 수 있는가"
            // 즉 구슬 부족으로 구매가 막히지 않는가이다.
            var normal = Array.Find(Personas, x => x.Name == "일반");
            var r = Simulate(normal);

            int activeDays = 90 - r.PermanentDoneDay;
            Assert.Greater(activeDays, 0, "영구 해금이 완료되지 않아 측정 불가");

            double intended = normal.LoginsPerDay * normal.BoostAppetite;   // 사고 싶은 양
            double achieved = (double)r.Boosts / activeDays;                // 실제 산 양

            Assert.GreaterOrEqual(achieved, intended * 0.9,
                $"의도 {intended:F2}회/일 중 {achieved:F2}회만 구매했습니다. " +
                "구슬 수급이 부족하거나 부스트가 너무 비쌉니다.");
        }

        [Test]
        public void Config의_접속가정이_일반_페르소나와_일치한다()
        {
            // assumedLoginsPerDay는 Validate()의 공급 추정에 쓰인다.
            // '일반' 페르소나가 기준 유저이므로 두 값이 어긋나면 안 된다.
            var normal = Array.Find(Personas, x => x.Name == "일반");
            Assert.AreEqual(normal.LoginsPerDay, cfg.assumedLoginsPerDay, 0.05,
                $"Config {cfg.assumedLoginsPerDay:F1} vs 일반 페르소나 {normal.LoginsPerDay}회. " +
                "한쪽을 다른 쪽에 맞추세요.");
        }

        [Test]
        public void Config의_행동가정이_페르소나_모델과_일치한다()
        {
            // assumedBoostsPerDay는 Validate()의 구슬 균형 판정에 쓰인다.
            // 그 값이 페르소나 모델의 의도값과 어긋나면
            // 검증기가 실제와 다른 경제를 판정하게 된다.
            var normal = Array.Find(Personas, x => x.Name == "일반");
            double modelIntent = normal.LoginsPerDay * normal.BoostAppetite;

            Assert.AreEqual(modelIntent, cfg.assumedBoostsPerDay, 0.05,
                $"Config {cfg.assumedBoostsPerDay:F2} vs 페르소나 모델 {modelIntent:F2}. " +
                "한쪽을 다른 쪽에 맞추세요.");
        }

        [Test]
        public void 런이_절대상한_10분을_넘지_않는다()
        {
            // 목표는 5분, 절대 상한은 10분이다.
            // 모든 유저의 런을 5분 이하로 강제하면 지나치게 빡빡하므로
            // 여기서는 상한만 검사하고, 실제 평균은 리포트 테스트에서 관찰한다.
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                double perRun = r.ActiveMinutes / r.Runs;
                Assert.Less(perRun, 10.0,
                    $"{p.Name}: 런당 {perRun:F1}분. 5분 세션 포지션을 벗어납니다.");
            }
        }

        [Test]
        public void 오프라인_기여도가_적정_범위에_있다()
        {
            // 상한과 하한을 모두 검사한다.
            //   너무 높으면 → 게임을 안 해도 되는 게임이 된다
            //   너무 낮으면 → "진짜 방치되는 게임"이라는 차별화가 사라진다
            //
            // 하한이 없으면 offlineMaxRatio를 5%로 낮춰도 아무 테스트가 걸리지 않는다.
            // (뮤테이션 테스트에서 실제로 발견된 구멍)
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                Assert.Less(r.OfflineCoinShare, 0.70,
                    $"{p.Name}: 오프라인 기여 {r.OfflineCoinShare:P0}. 액티브 플레이 유인이 부족합니다.");
                Assert.Greater(r.OfflineCoinShare, 0.20,
                    $"{p.Name}: 오프라인 기여 {r.OfflineCoinShare:P0}. " +
                    "방치형이라는 정체성이 무너집니다.");
            }
        }

        [Test]
        public void 오프라인_보상이_런을_유의미하게_단축한다()
        {
            // 오프라인의 실질 가치는 '접속했을 때 얼마나 건너뛰어 주는가'다.
            // 4시간 방치 후 최소 절반 이상의 웨이브를 건너뛸 수 있어야
            // 5분 세션이 성립한다.
            const int Wave = 127;
            var reward = EconomyCore.CalculateOffline(
                cfg, cfg.offlineCapHours, Wave, 1.0, false);

            double skipRatio = reward.StartWave / Wave;
            Assert.Greater(skipRatio, 0.50,
                $"4시간 방치 후 웨이브 {reward.StartWave:F0}부터 시작 " +
                $"(도달 {Wave}의 {skipRatio:P0}). 오프라인 보상이 너무 짜서 " +
                "매번 처음부터 다시 해야 합니다.");
        }

        [Test]
        public void 런당_평균시간이_목표대역_안에_있다()
        {
            // 절대 상한(10분)만 보면 "8분짜리 게임"이 되어도 통과한다.
            // 목표는 5분이므로 평균이 그 근처인지도 함께 본다.
            // 하한이 있는 이유: 너무 짧으면 한 사이클에 의미 있는 변화가 없다.
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                double perRun = r.ActiveMinutes / r.Runs;
                Assert.Greater(perRun, 3.0,
                    $"{p.Name}: 런당 {perRun:F1}분. 너무 짧아 한 사이클의 의미가 약합니다.");
                Assert.Less(perRun, 8.0,
                    $"{p.Name}: 런당 {perRun:F1}분. 5분 목표에서 너무 멀어졌습니다.");
            }
        }

        [Test]
        [Ignore("P0 재작성 대기 - 단일 트랙 DPS + 승천 이중 조건 이전의 TargetWave 곡선. docs/P0_검증스위트_재작성_계획.md 3.2/3.3 참조")]
        public void 설계곡선이_실제_도달웨이브와_크게_어긋나지_않는다()
        {
            // TargetWave()의 설계 곡선은 환생 메타 검증('여유' 계산)의 기준이다.
            // 실제 플레이가 설계보다 훨씬 멀리 가면 그 검증 자체가 무의미해진다.
            //
            // 참고 — 실제가 설계보다 앞서는 것은 안전한 방향이다(여유가 더 있다는 뜻).
            // 반대로 실제가 설계에 못 미치면 유저가 벽에 갇힌다는 뜻이라 위험하다.
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                int design = EconomyCore.TargetWave(cfg, r.Runs);
                double ratio = (double)r.Wave / design;

                Assert.Greater(ratio, 0.95,
                    $"{p.Name}: 실제 {r.Wave} vs 설계 {design} ({ratio:P0}). " +
                    "설계 곡선만큼도 못 갑니다 — 유저가 벽에 갇힙니다.");
                Assert.Less(ratio, 1.35,
                    $"{p.Name}: 실제 {r.Wave} vs 설계 {design} ({ratio:P0}). " +
                    "설계 곡선이 현실과 너무 어긋나 환생 메타 검증이 무의미해집니다.");
            }
        }

        [Test]
        public void _90일차에_다음_목표가_남아있다()
        {
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                Assert.Less(r.Tier, cfg.tierGates.Length + 1,
                    $"{p.Name}: 90일차에 최대 티어 도달. 콘텐츠가 소진됩니다.");
            }
        }

        [Test]
        public void 헤비유저가_라이트유저보다_지나치게_앞서지_않는다()
        {
            // 접속 6회와 1회의 격차가 과하면 라이트 유저가 이탈한다.
            var light = Simulate(Array.Find(Personas, x => x.Name == "라이트"));
            var heavy = Simulate(Array.Find(Personas, x => x.Name == "헤비"));

            double ratio = (double)heavy.Wave / light.Wave;
            Assert.Less(ratio, 2.0,
                $"헤비 {heavy.Wave} vs 라이트 {light.Wave} (배율 {ratio:F2}). 격차가 과합니다.");
        }

        // ───────── 실제 결과값 고정 ─────────

        /// <summary>
        /// 페르소나별 90일 구슬 잔액의 허용 범위. 가격을 바꾸면 이 테스트가 먼저 깨진다.
        ///
        /// 라이트만 상한이 높은 이유:
        ///   라이트는 공급(144/일)이 적은 대신 부스트 소비도 적어(0.3회/일)
        ///   영구 해금을 마친 뒤 잔액이 서서히 쌓이는 '축적형' 페르소나다.
        ///   90일 최대 공급 12,960 - 영구싱크 8,000 = 약 4,960이 구조적 상한이며,
        ///   5,000이라는 값은 여기서 나온 것이지 임의로 정한 수치가 아니다.
        ///
        ///   반면 일반·헤비·광고적극은 공급도 많지만 부스트로 계속 태우는
        ///   '소비형' 페르소나라 잔액이 낮게 유지되는 것이 정상이다.
        ///   이들의 잔액이 3,000을 넘으면 반복 싱크가 작동하지 않는다는 뜻이다.
        /// </summary>
        private static readonly Dictionary<string, (int Min, int Max)> GemRange =
            new Dictionary<string, (int, int)>
            {
                { "라이트",   (0, 5000) },
                { "일반",     (0, 3000) },
                { "헤비",     (0, 3000) },
                { "광고적극", (0, 3000) },
            };

        [Test]
        public void _90일_구슬_잔액이_허용범위_안에_있다()
        {
            foreach (var p in Personas)
            {
                var r = Simulate(p);
                var range = GemRange[p.Name];
                Assert.GreaterOrEqual(r.Gems, range.Min,
                    $"{p.Name}: 90일 구슬 {r.Gems:N0}개. 하한 {range.Min} 미만입니다.");
                Assert.LessOrEqual(r.Gems, range.Max,
                    $"{p.Name}: 90일 구슬 {r.Gems:N0}개. 상한 {range.Max} 초과 — 싱크가 부족합니다.");
            }
        }

        [Test]
        public void 구슬_지급량이_공식과_정확히_일치한다()
        {
            // floor(min(자리비움, 확장상한) x gemsPerHour) 를 정확히 따라야 한다.
            // 부등호(<=)로만 검사하면 실제 지급이 절반이어도 통과해버린다.
            foreach (var p in Personas)
            {
                double credited = Math.Min(p.HoursAway, cfg.offlineCapHoursMax);
                int expectedPerLogin = (int)Math.Floor(credited * cfg.gemsPerHour);

                var reward = EconomyCore.CalculateOffline(
                    cfg, p.HoursAway, 127, 1.0, false, cfg.offlineCapHoursMax);

                Assert.AreEqual(expectedPerLogin, reward.Gems,
                    $"{p.Name}: 자리비움 {p.HoursAway}h에서 " +
                    $"{expectedPerLogin}젬이어야 하는데 {reward.Gems}젬을 지급했습니다.");
            }
        }

        [Test]
        public void Config의_공급_추정이_실제_일일_지급량과_일치한다()
        {
            // EconomyConfig.Validate()가 쓰는 추정식이
            // 실제 지급량과 같은 값을 내는지 검사한다.
            // 두 계산이 갈라지면 검증기가 잘못된 판정을 내린다.
            //
            // Config는 assumedLoginsPerDay(=3)를 전제로 하므로
            // 같은 접속 패턴을 가진 페르소나로만 비교한다.
            foreach (var p in Personas)
            {
                if (Math.Abs(p.LoginsPerDay - cfg.assumedLoginsPerDay) > 0.01) continue;

                int actualDaily = EconomyCore.CalculateOffline(
                    cfg, p.HoursAway, 127, 1.0, false, cfg.offlineCapHoursMax).Gems
                    * p.LoginsPerDay;

                double creditedHours = Math.Min(
                    24.0, cfg.assumedLoginsPerDay * cfg.offlineCapHoursMax);
                double configEstimate = creditedHours * cfg.gemsPerHour;

                Assert.AreEqual(configEstimate, actualDaily, 1.0,
                    $"{p.Name}: Config 추정 {configEstimate:F0} vs 실제 {actualDaily}. " +
                    "EconomyConfig의 공급 추정식을 실제 지급 로직에 맞추세요.");
            }
        }

        [Test]
        public void 라이트유저의_실제_공급이_추정의_절반이다()
        {
            // Config의 추정은 '하루 24시간이 인정되는' 유저 기준이다.
            // 하루 1회만 접속하는 유저는 확장상한(12h)까지만 받으므로 정확히 절반이다.
            // 이 비대칭을 모르면 라이트 유저 경제를 과대평가하게 된다.
            var light = Array.Find(Personas, x => x.Name == "라이트");

            int actualDaily = EconomyCore.CalculateOffline(
                cfg, light.HoursAway, 127, 1.0, false, cfg.offlineCapHoursMax).Gems
                * light.LoginsPerDay;

            double fullDayEstimate = 24.0 * cfg.gemsPerHour;

            Assert.AreEqual(fullDayEstimate / 2.0, actualDaily, 1.0,
                $"라이트 유저 실제 공급 {actualDaily} vs 24시간 기준 {fullDayEstimate:F0}의 절반");
        }

        [Test]
        public void 접속이_적은_유저는_구슬을_덜_받는다()
        {
            // 구슬은 경과 시간에 비례하되 접속당 상한이 걸린다.
            // 하루 1회 접속(12시간 자리비움) 유저는 24시간분을 받을 수 없다.
            // 이 비대칭은 의도된 것이며, 자주 들어오는 유저에 대한 보상이다.
            var light = Array.Find(Personas, x => x.Name == "라이트");
            var normal = Array.Find(Personas, x => x.Name == "일반");

            int lightDaily = EconomyCore.CalculateOffline(
                cfg, light.HoursAway, 127, 1.0, false, cfg.offlineCapHoursMax).Gems
                * light.LoginsPerDay;
            int normalDaily = EconomyCore.CalculateOffline(
                cfg, normal.HoursAway, 127, 1.0, false, cfg.offlineCapHoursMax).Gems
                * normal.LoginsPerDay;

            Assert.Less(lightDaily, normalDaily,
                "접속이 적은 유저가 더 많이 받고 있습니다.");

            // 다만 격차가 2배를 넘으면 라이트 유저가 편의 기능을 영영 못 연다
            Assert.LessOrEqual((double)normalDaily / lightDaily, 2.0,
                $"구슬 수급 격차 {(double)normalDaily / lightDaily:F2}배. 라이트 유저에게 가혹합니다.");
        }
    }
}

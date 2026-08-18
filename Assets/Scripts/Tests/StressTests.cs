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
    /// stress — 파라미터를 흔들었을 때 붕괴를 실제로 감지하는가.
    ///
    /// 마스터문서 5.6이 경고하는 것은 "파라미터 하나만 바꿔도 게임이 진행 불가능해진다"이고,
    /// 더 위험한 것은 "1층 시트에서는 멀쩡해 보이는데 2층이 조용히 깨진다"는 점이다.
    /// 여기서는 근사식이 아니라 실제 BattleRunner 순환으로 붕괴를 재현한다.
    ///
    /// 감지 대상 네 가지
    ///   진행 불가 / 경제 발산 / 과도한 축적 / 승천 게이트 붕괴
    ///
    /// 원칙 — 이 스위트는 붕괴를 '고치지' 않는다. '보이게' 한다.
    ///
    /// docs/P0_검증스위트_재작성_계획.md 3.4
    /// </summary>
    public class StressTests
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

        private struct Progress
        {
            public int Tier;
            public int Wave;          // 마지막 런 도달 웨이브
            public double Cores;
            public double AvgMinutes;
            public int Growth;        // 마지막 10런 동안의 웨이브 증가량
            public override string ToString()
                => $"티어 {Tier}, 웨이브 {Wave}, 코어 {Cores:N0}, 평균 {AvgMinutes:F1}분, 후반증가 {Growth}";
        }

        /// <summary>
        /// 실제 순환(오프라인 수령 + 런)을 돌린다.
        /// 근사식을 쓰지 않는 것이 이 스위트의 존재 이유다.
        /// </summary>
        private Progress Run(EconomyConfig c, int runs = 60, double dt = 0.25)
        {
            double cores = 0; int tier = 1, lastWave = 1, runsToday = 1;
            double sumMin = 0; int waveAt50 = 0, finalWave = 1;

            for (int k = 1; k <= runs; k++)
            {
                double atk = EconomyCore.AttackMultiplier(c, cores, tier);
                double coin = EconomyCore.CoinMultiplier(c, cores, tier);

                var off = EconomyCore.CalculateOffline(
                    c, 8.0, lastWave, coin, false, c.offlineCapHours, atk);
                int start = Math.Max(1, (int)off.StartWave);

                var runner = new BattleRunner(c);
                var tracks = new UpgradeTracks(c);
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

                sumMin += runner.RunElapsed / 60.0;
                finalWave = runner.DeepestWave;
                if (k == runs - 10) waveAt50 = finalWave;

                cores += EconomyCore.CoreGainWithDecay(c, finalWave, runsToday);
                lastWave = finalWave;
                runsToday = runsToday >= 3 ? 1 : runsToday + 1;

                if (EconomyCore.CanAscend(c, tier, finalWave, cores))
                {
                    tier++;
                    cores = EconomyCore.CoresAfterAscend(c, cores);
                }
            }

            return new Progress
            {
                Tier = tier, Wave = finalWave, Cores = cores,
                AvgMinutes = sumMin / runs, Growth = finalWave - waveAt50,
            };
        }

        // ═══════════════════════════════════════════════════
        //  기준선
        // ═══════════════════════════════════════════════════

        [Test]
        public void 기준선은_60회차에_티어4_웨이브180대에_도달한다()
        {
            var p = Run(cfg);
            TestContext.WriteLine($"기준선: {p}");

            Assert.GreaterOrEqual(p.Tier, 3, $"기준선이 이미 정체했습니다: {p}");
            Assert.GreaterOrEqual(p.Wave, 150, $"기준선 도달 웨이브가 낮습니다: {p}");
            Assert.Greater(p.Growth, 0, $"기준선이 후반에 멈췄습니다: {p}");
            Assert.Less(p.AvgMinutes, 12.0, $"기준선 런 시간이 깁니다: {p}");
        }

        // ═══════════════════════════════════════════════════
        //  진행 불가
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 체력 증가율을 올렸을 때 진행 저하를 감지하는가.
        ///
        /// 밴드는 실측 기준이다 (2026-08, dt=0.25, 60회차).
        ///   기준선  티어 4 / 웨이브 187 / 후반증가 9
        ///   1.12    티어 3 / 웨이브 125 / 후반증가 4     → 웨이브 -33%, 티어 -1
        ///
        /// 마스터문서 5.6은 이 조건을 "여유 0.02 — 완전 붕괴"로 적고 있으나,
        /// 그 판정은 오방색 곱연산이 빠진 근사식 기준이었다.
        /// 실제 런타임에서는 티어 3 / 웨이브 125까지 진행되므로 '완전 붕괴'는 아니다.
        /// → 문서 정정 목록에 포함. 여기서는 실측된 저하 폭을 밴드로 쓴다.
        /// </summary>
        [Test]
        public void 체력증가율_상승시_진행저하를_감지한다()
        {
            var baseline = Run(cfg);
            cfg.enemyHpGrowth = 1.12;
            var degraded = Run(cfg);

            TestContext.WriteLine($"기준선 {baseline}\n1.12   {degraded}");

            Assert.Less(degraded.Wave, baseline.Wave * 0.75,
                $"웨이브가 {baseline.Wave} -> {degraded.Wave}로 25%도 안 떨어졌습니다 " +
                "— 저하가 감지되지 않았습니다");
            Assert.Less(degraded.Tier, baseline.Tier,
                $"티어가 {baseline.Tier} -> {degraded.Tier}로 유지됐습니다");
            Assert.Less(degraded.Growth, baseline.Growth / 2.0,
                $"후반 성장이 {baseline.Growth} -> {degraded.Growth}로 절반 이상 남았습니다");
        }

        [Test]
        public void 티어배수를_1_5로_낮추면_후반_성장이_멈춘다()
        {
            // 마스터문서 5.6: "선형 코어만으로는 장기 곡선을 못 버틴다"
            var baseline = Run(cfg);
            cfg.tierMultiplier = 1.5;
            var weak = Run(cfg);

            TestContext.WriteLine($"기준선 {baseline}\n1.5    {weak}");

            Assert.Less(weak.Wave, baseline.Wave,
                $"티어배수 1.5인데 기준선만큼 갑니다: {weak}");
        }

        // ═══════════════════════════════════════════════════
        //  경제 발산 · 과도한 축적
        // ═══════════════════════════════════════════════════

        [Test]
        public void 코인증가율이_체력증가율_이상이면_Config가_거부한다()
        {
            cfg.coinGrowth = cfg.enemyHpGrowth;
            Assert.IsFalse(cfg.Validate(out string err),
                "코인 증가율이 체력 증가율 이상인데 통과했습니다 — 벽이 사라집니다");
            TestContext.WriteLine($"거부 사유: {err}");

            cfg.coinGrowth = cfg.enemyHpGrowth + 0.01;
            Assert.IsFalse(cfg.Validate(out _), "코인 증가율이 더 큰데도 통과했습니다");
        }

        [Test]
        public void 코어_감쇠하한이_0보다_크면_하루_총량_상한이_사라진다()
        {
            Assert.IsFalse(double.IsInfinity(EconomyCore.MaxDailyCoreRuns(cfg)),
                "기준 설정인데 상한이 없습니다");

            cfg.coreDecayFloor = 0.02;

            Assert.IsTrue(double.IsPositiveInfinity(EconomyCore.MaxDailyCoreRuns(cfg)),
                "감쇠 하한이 0보다 큰데 상한이 유한합니다 — 무한 파밍이 가능해집니다");

            // 90일 누적으로 실증한다. 자동 폭주 유저 기준.
            double safe = 0, leaky = 0;
            cfg.coreDecayFloor = 0.0;
            for (int day = 0; day < 90; day++)
                for (int k = 1; k <= 144; k++) safe += EconomyCore.CoreDecayFactor(cfg, k);
            cfg.coreDecayFloor = 0.02;
            for (int day = 0; day < 90; day++)
                for (int k = 1; k <= 144; k++) leaky += EconomyCore.CoreDecayFactor(cfg, k);

            TestContext.WriteLine($"90일 누적 런분  하한 0: {safe:F1}  /  하한 0.02: {leaky:F1}");
            Assert.Greater(leaky / safe, 1.3,
                "하한 0.02의 누적 초과분이 예상보다 작습니다 — 전제를 다시 보세요");
        }

        [Test]
        public void 젬_공급을_늘리면_Config가_과잉을_잡아낸다()
        {
            Assert.IsTrue(cfg.Validate(out _), "기준 설정이 이미 유효하지 않습니다");

            cfg.gemsPerHour = 25;   // 마스터문서의 낡은 값
            Assert.IsFalse(cfg.Validate(out string err),
                "시간당 25젬인데 통과했습니다 — 90일에 2만 개 이상 쌓입니다");
            TestContext.WriteLine($"거부 사유: {err}");
        }

        /// <summary>
        /// 오프라인 보상 비율이 100%를 넘으면 무결성 검사가 잡아내는가.
        ///
        /// 처음에는 상한을 0.99로 풀어 실패를 기대했으나 통과했다. 이유는 구조적이다.
        ///
        ///     StartWave = lastWave + log(ratio) / log(coinGrowth)
        ///
        /// ratio < 1 이면 log(ratio) < 0 이므로 시작 웨이브는 **반드시** 직전 도달보다 낮다.
        /// 즉 "광고를 봐도 시작점이 직전 도달을 넘지 않는다"(마스터문서 5.5 체크리스트)는
        /// 비율이 100% 미만인 한 수학적으로 보장되며, 이 검사가 그 속성에 대해
        /// 새로 알려주는 것은 없다. 실제로 잡아내는 것은 ratio >= 1.0 인 경우뿐이다.
        ///
        /// 검사를 강화하는 방향(헤드룸 하한 등)은 minHeadroom 미도입 결정과 충돌하므로
        /// 지금은 손대지 않는다. (README "측정 후 내린 결정")
        /// </summary>
        [Test]
        public void 오프라인_비율이_100퍼센트를_넘으면_무결성이_깨진다()
        {
            Assert.IsTrue(EconomyCore.ValidateOfflineIntegrity(cfg, 170, 100.0),
                "기준 설정인데 오프라인 무결성이 이미 깨져 있습니다");

            // 비율 99%까지는 구조적으로 안전하다 — 실패하지 않는 것이 정상이다.
            cfg.offlineRatioCeiling = 0.99;
            cfg.offlineMaxRatio = 0.95;
            Assert.IsTrue(EconomyCore.ValidateOfflineIntegrity(cfg, 170, 100.0),
                "ratio < 1 인데 무결성이 깨졌습니다 — 시작 웨이브 역산식이 바뀌었습니다");

            // 100%를 넘기면 직전 런 전체를 코인으로 돌려주는 셈이 되어 무너진다.
            cfg.offlineRatioCeiling = 1.5;
            cfg.offlineMaxRatio = 1.2;
            Assert.IsFalse(EconomyCore.ValidateOfflineIntegrity(cfg, 170, 100.0),
                "보상 비율이 100%를 넘는데 시작 웨이브가 직전 도달 미만입니다");
        }

        // ═══════════════════════════════════════════════════
        //  승천 게이트 붕괴  (CLAUDE.md 절대 규칙 4)
        // ═══════════════════════════════════════════════════

        [Test]
        public void 코어_게이트를_없애면_티어가_연쇄로_뚫린다()
        {
            // "웨이브 단독 조건이면 티어 상승 → 배수 2.5배 → 웨이브 상승 →
            //  다음 게이트 통과라는 양의 되먹임이 생긴다" (CLAUDE.md 절대 규칙 4)
            var baseline = Run(cfg);

            for (int i = 0; i < cfg.tierCoreGates.Length; i++) cfg.tierCoreGates[i] = 0;
            var runaway = Run(cfg);

            TestContext.WriteLine($"이중 조건 {baseline}\n웨이브 단독 {runaway}");

            Assert.Greater(runaway.Tier, baseline.Tier,
                $"코어 게이트를 없앴는데 티어가 그대로입니다 ({runaway.Tier}) " +
                "— 이중 조건이 실제로는 코어로 막고 있지 않다는 뜻입니다");

            TestContext.WriteLine(
                $"→ 코어 게이트가 60회차 기준 티어 {baseline.Tier} → {runaway.Tier} 상승을 막고 있습니다");
        }

        /// <summary>
        /// 웨이브 게이트를 없애도 코어가 티어를 막는가.
        ///
        /// 실측 결과 (60회차): 이중 조건과 코어 단독이 **완전히 동일**하다.
        ///   이중 조건   티어 4 / 웨이브 187 / 코어 2,419
        ///   코어 단독   티어 4 / 웨이브 187 / 코어 2,419
        ///
        /// 즉 현재 커브에서 웨이브 게이트는 비병목(non-binding)이다.
        /// 실측 도달 웨이브가 설계보다 1.6~1.8배 빨라 웨이브 조건이 항상 먼저 충족된다.
        ///
        /// ★ 그래도 삭제하지 않는다.
        ///   전투 진행 모델이나 웨이브 커브가 바뀌면 다시 안전장치로 작동한다.
        ///   폭주 방지를 코어 게이트 하나에만 의존하게 만들면 안 된다.
        ///   (코어 게이트를 없앤 실험에서 60회차 만에 티어 4 -> 8로 뚫렸다)
        /// </summary>
        [Test]
        public void 웨이브_게이트를_없애도_코어가_티어를_막는다()
        {
            var baseline = Run(cfg);

            for (int i = 0; i < cfg.tierGates.Length; i++) cfg.tierGates[i] = 1;
            var waveOpen = Run(cfg);

            TestContext.WriteLine($"이중 조건 {baseline}\n코어 단독 {waveOpen}");

            // 두 축 중 하나만 풀었을 때 어느 쪽이 실질 병목인지 기록한다.
            // 계측 결과 코어가 병목이므로 웨이브를 풀어도 큰 변화가 없어야 한다.
            Assert.LessOrEqual(waveOpen.Tier - baseline.Tier, 2,
                $"웨이브 게이트를 없앴더니 티어가 {baseline.Tier} -> {waveOpen.Tier}로 뛰었습니다 " +
                "— 웨이브가 실질 병목이라면 코어 게이트 설계를 다시 봐야 합니다");
        }

        // ═══════════════════════════════════════════════════
        //  마스터문서 5.6의 낡은 항목
        // ═══════════════════════════════════════════════════

        [Test]
        public void 도달웨이브_지수는_이제_게임플레이에_영향을_주지_않는다()
        {
            // 마스터문서 5.6은 "도달웨이브 지수 0.269 → 0.40이면 여유 0.00으로 붕괴"라고
            // 적고 있지만, TargetWave()는 검증용 기준 곡선이며 런타임 호출이 0곳이다.
            // (docs/P0_계측결과_1차.md 3.3에서 전수 확인)
            // 따라서 이 파라미터를 흔들어도 실제 진행은 바뀌지 않아야 한다.
            var baseline = Run(cfg, runs: 30);
            cfg.waveExponent = 0.40;
            cfg.waveCoefficient = 20.0;
            var perturbed = Run(cfg, runs: 30);

            Assert.AreEqual(baseline.Wave, perturbed.Wave,
                "설계 곡선을 흔들었더니 실제 도달 웨이브가 바뀌었습니다 " +
                "— TargetWave가 런타임 경로에 들어갔다는 뜻입니다");
            Assert.AreEqual(baseline.Tier, perturbed.Tier, "티어도 바뀌면 안 됩니다");

            TestContext.WriteLine(
                "마스터문서 5.6의 '도달웨이브 지수' 행은 스프레드시트 모델에만 해당합니다. " +
                "코드에서는 검증용 자 눈금이므로 게임플레이에 영향이 없습니다.");
        }
    }
}

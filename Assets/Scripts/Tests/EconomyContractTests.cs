using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// econ — 단일 함수의 계약과 경계값.
    ///
    /// EconomyTests가 스프레드시트 대조를 담당하고, 여기서는 그동안 비어 있던
    /// 계약들을 채운다. 특히 코어 일일 감쇠는 CLAUDE.md 절대 규칙 5인데도
    /// 반환값 하나 확인하는 테스트조차 없었다.
    ///
    /// docs/P0_검증스위트_재작성_계획.md 3.4
    /// </summary>
    public class EconomyContractTests
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
        //  1. 코어 일일 감쇠  (CLAUDE.md 절대 규칙 5)
        // ═══════════════════════════════════════════════════

        [Test]
        public void 감쇠_소프트캡_이하는_감쇠하지_않는다()
        {
            for (int runs = 1; runs <= cfg.coreDailySoftCap; runs++)
                Assert.AreEqual(1.0, EconomyCore.CoreDecayFactor(cfg, runs), 1e-12,
                    $"{runs}번째 런에서 감쇠가 걸렸습니다. 헤비 유저(6런/일)는 감쇠 0%여야 합니다");
        }

        [Test]
        public void 감쇠_소프트캡_초과는_지수적으로_준다()
        {
            for (int over = 1; over <= 10; over++)
            {
                int runs = cfg.coreDailySoftCap + over;
                double expected = Math.Pow(cfg.coreDecayPerRun, over);
                Assert.AreEqual(expected, EconomyCore.CoreDecayFactor(cfg, runs), 1e-12,
                    $"{runs}번째 런");
            }
        }

        [Test]
        public void 일일_코어_총량이_7_22런분으로_수렴한다()
        {
            // 등비급수 합: 소프트캡 6 + 0.55/(1-0.55) = 7.2222...
            double sum144 = 0;
            for (int k = 1; k <= 144; k++) sum144 += EconomyCore.CoreDecayFactor(cfg, k);

            double sum1000 = sum144;
            for (int k = 145; k <= 1000; k++) sum1000 += EconomyCore.CoreDecayFactor(cfg, k);

            Assert.AreEqual(7.2222, sum144, 0.001,
                $"144런/일(자동 폭주) 총량이 {sum144:F4}런분입니다");
            Assert.AreEqual(sum144, sum1000, 1e-9,
                "1000런을 돌려도 총량이 늘면 안 됩니다 — 무한 파밍이 가능해집니다");
            Assert.AreEqual(EconomyCore.MaxDailyCoreRuns(cfg), sum1000, 1e-6,
                "MaxDailyCoreRuns의 닫힌 형태가 실제 누적과 다릅니다");
        }

        [Test]
        public void 감쇠하한이_0이_아니면_총량이_발산한다()
        {
            // CLAUDE.md: "0.02만 줘도 100회 반복 시 2회분이 추가로 쌓여 커브가 무너진다"
            double safeSum = 0;
            for (int k = 1; k <= 100; k++) safeSum += EconomyCore.CoreDecayFactor(cfg, k);

            cfg.coreDecayFloor = 0.02;

            double leakySum = 0;
            for (int k = 1; k <= 100; k++) leakySum += EconomyCore.CoreDecayFactor(cfg, k);

            Assert.Greater(leakySum - safeSum, 1.5,
                $"하한 0.02에서 추가 누적이 {leakySum - safeSum:F2}런분뿐입니다 — 전제가 바뀌었습니다");
            Assert.IsTrue(double.IsPositiveInfinity(EconomyCore.MaxDailyCoreRuns(cfg)),
                "하한이 0보다 크면 하루 총량에 상한이 없어야 하고, 그 사실이 드러나야 합니다");
        }

        [Test]
        public void 자동폭주_144런이_일반유저_3런의_2_4배를_넘지_않는다()
        {
            // 실제 코어 누적으로 확인한다. 반환값만 보면 놓치는 것이 있다.
            const int wave = 150;

            double normal = 0;
            for (int k = 1; k <= 3; k++) normal += EconomyCore.CoreGainWithDecay(cfg, wave, k);

            double berserk = 0;
            for (int k = 1; k <= 144; k++) berserk += EconomyCore.CoreGainWithDecay(cfg, wave, k);

            double ratio = berserk / normal;
            Assert.Less(ratio, 2.5,
                $"144런 유저가 3런 유저의 {ratio:F2}배를 얻습니다 — 48배 플레이에 대한 보상 격차로 과합니다");
            Assert.Greater(ratio, 2.0, $"격차가 {ratio:F2}배로 너무 작으면 더 하는 유인이 사라집니다");
        }

        // ═══════════════════════════════════════════════════
        //  2. 승천 이중 조건  (CLAUDE.md 절대 규칙 4)
        // ═══════════════════════════════════════════════════

        [Test]
        public void 승천은_웨이브만_충족하면_거부된다()
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                int waveGate = EconomyCore.NextTierGate(cfg, tier);
                double coreGate = EconomyCore.NextTierCoreGate(cfg, tier);

                Assert.IsFalse(EconomyCore.CanAscend(cfg, tier, waveGate + 50, coreGate - 1),
                    $"티어 {tier}: 코어가 부족한데 승천이 허용됐습니다");
            }
        }

        [Test]
        public void 승천은_코어만_충족하면_거부된다()
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                int waveGate = EconomyCore.NextTierGate(cfg, tier);
                double coreGate = EconomyCore.NextTierCoreGate(cfg, tier);

                Assert.IsFalse(EconomyCore.CanAscend(cfg, tier, waveGate - 1, coreGate * 100),
                    $"티어 {tier}: 웨이브가 부족한데 승천이 허용됐습니다 " +
                    "— 웨이브 단독 조건이면 티어가 연쇄로 뚫립니다");
            }
        }

        [Test]
        public void 승천_경계값()
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                int w = EconomyCore.NextTierGate(cfg, tier);
                double c = EconomyCore.NextTierCoreGate(cfg, tier);

                Assert.IsFalse(EconomyCore.CanAscend(cfg, tier, w - 1, c), $"티어 {tier}: 웨이브 gate-1");
                Assert.IsTrue(EconomyCore.CanAscend(cfg, tier, w, c), $"티어 {tier}: 웨이브 gate 정확히");
                Assert.IsTrue(EconomyCore.CanAscend(cfg, tier, w + 1, c), $"티어 {tier}: 웨이브 gate+1");

                Assert.IsFalse(EconomyCore.CanAscend(cfg, tier, w, c - 1e-9), $"티어 {tier}: 코어 gate 미달");
                Assert.IsTrue(EconomyCore.CanAscend(cfg, tier, w, c), $"티어 {tier}: 코어 gate 정확히");
            }
        }

        [Test]
        public void AscendProgress가_부족한_쪽을_알려준다()
        {
            int w = EconomyCore.NextTierGate(cfg, 1);
            double c = EconomyCore.NextTierCoreGate(cfg, 1);

            Assert.AreEqual((false, false), EconomyCore.AscendProgress(cfg, 1, w - 1, c - 1));
            Assert.AreEqual((true, false), EconomyCore.AscendProgress(cfg, 1, w, c - 1));
            Assert.AreEqual((false, true), EconomyCore.AscendProgress(cfg, 1, w - 1, c));
            Assert.AreEqual((true, true), EconomyCore.AscendProgress(cfg, 1, w, c));
        }

        // ═══════════════════════════════════════════════════
        //  3. 코어 소각 · 티어 배수
        // ═══════════════════════════════════════════════════

        [Test]
        public void 승천_후_코어는_유지율만큼만_남는다()
        {
            Assert.AreEqual(0.15, cfg.coreRetainOnAscend, 1e-12, "유지율 기본값이 바뀌었습니다");

            foreach (double cores in new[] { 0.0, 1.0, 80.0, 2300.0, 6.8e6 })
                Assert.AreEqual(cores * 0.15, EconomyCore.CoresAfterAscend(cfg, cores), cores * 1e-12 + 1e-12,
                    $"코어 {cores}");
        }

        [Test]
        public void 티어배수는_2_5의_거듭제곱이다()
        {
            for (int tier = 1; tier <= 10; tier++)
                Assert.AreEqual(Math.Pow(2.5, tier - 1), EconomyCore.TierMultiplier(cfg, tier), 1e-9,
                    $"티어 {tier}");
        }

        /// <summary>
        /// 승천 시 전투력 하락률의 하한을 고정한다.
        ///
        /// ★ 이것은 게임의 절대 밸런스 규칙이 아니다.
        ///   `minHeadroom` 보정을 넣지 않기로 한 결정의 **전제조건**이다.
        ///
        /// 왜 0.375인가 —
        ///   승천은 코어를 15%만 남기고(coreRetainOnAscend) 티어를 +1 한다.
        ///   전투 배수 = (1 + 코어 x coreAttackCoeff) x tierMultiplier^(티어-1) 이므로
        ///
        ///       비율 = (1 + 0.15c x 0.02) / (1 + c x 0.02) x 2.5
        ///       c → ∞ 일 때  0.15 x 2.5 = 0.375
        ///
        ///   즉 코어가 쌓일수록 하락률이 -62.5%로 포화하고, 그 이상 나빠지지 않는다.
        ///   이 포화가 있기 때문에 승천 직후 짧은 런이 1회로 끝나고
        ///   다음 런에서 오프라인 시작점이 자동으로 내려가 회복된다.
        ///
        /// 이 값이 0.37 아래로 내려가면 —
        ///   승천 직후 전투력 하락이 자기 교정 속도를 앞질러 짧은 런이 연쇄된다.
        ///   그때는 minHeadroom 보정을 다시 검토해야 한다.
        ///
        /// 바꾸게 되는 경우 (셋 중 하나라도 건드리면 여기가 먼저 깨진다) —
        ///   coreRetainOnAscend(0.15) / tierMultiplier(2.5) / coreAttackCoeff(0.02)
        ///
        /// 근거: docs/P0_계측결과_3차_헤드룸.md 3장 (실측 1.14 → 0.57 → 0.42 → 0.375 수렴)
        /// 결정: README "측정 후 내린 결정" — 승천 직후 짧은 런은 보정하지 않음
        /// </summary>
        [Test]
        public void 승천은_전투력을_최악_37_5퍼센트까지_떨어뜨린다()
        {
            double worst = double.MaxValue;
            foreach (double cores in new[] { 100.0, 2300.0, 1e5, 1e7 })
            {
                for (int tier = 1; tier <= 8; tier++)
                {
                    double before = EconomyCore.AttackMultiplier(cfg, cores, tier);
                    double after = EconomyCore.AttackMultiplier(
                        cfg, EconomyCore.CoresAfterAscend(cfg, cores), tier + 1);
                    worst = Math.Min(worst, after / before);
                }
            }
            Assert.GreaterOrEqual(worst, 0.37,
                $"승천 시 전투력이 최악 {worst:P1}까지 떨어집니다 (전제는 37.5%). " +
                "coreRetainOnAscend / tierMultiplier / coreAttackCoeff 중 하나가 바뀌었다면 " +
                "헤드룸 자기 교정이 1회로 끝나는지 다시 계측하고, " +
                "연쇄되면 minHeadroom 보정을 도입할지 판단하세요. " +
                "docs/P0_계측결과_3차_헤드룸.md 3장");
        }

        // ═══════════════════════════════════════════════════
        //  4. 티어 게이트
        // ═══════════════════════════════════════════════════

        [Test]
        public void 티어_게이트가_모두_오름차순이다()
        {
            for (int i = 1; i < cfg.tierGates.Length; i++)
                Assert.Greater(cfg.tierGates[i], cfg.tierGates[i - 1], $"웨이브 게이트 [{i}]");

            for (int i = 1; i < cfg.tierCoreGates.Length; i++)
                Assert.Greater(cfg.tierCoreGates[i], cfg.tierCoreGates[i - 1], $"코어 게이트 [{i}]");

            Assert.AreEqual(cfg.tierGates.Length, cfg.tierCoreGates.Length,
                "웨이브 게이트와 코어 게이트의 개수가 다릅니다");
        }

        [Test]
        public void 최대티어를_넘으면_승천이_영원히_불가능하다()
        {
            int maxTier = cfg.tierGates.Length + 1;

            Assert.AreEqual(int.MaxValue, EconomyCore.NextTierGate(cfg, maxTier));
            Assert.IsTrue(double.IsPositiveInfinity(EconomyCore.NextTierCoreGate(cfg, maxTier)));
            Assert.IsFalse(EconomyCore.CanAscend(cfg, maxTier, 99999, 1e12),
                "최대 티어를 넘어 승천할 수 있으면 티어 배수가 폭주합니다");
        }

        // ═══════════════════════════════════════════════════
        //  5. BigNumber 경계
        // ═══════════════════════════════════════════════════

        [Test]
        public void BigNumber_음수_연산과_비교()
        {
            var neg = new BigNumber(-1234.5);
            var pos = new BigNumber(1234.5);

            Assert.IsTrue(neg < BigNumber.Zero);
            Assert.IsTrue(neg < pos);
            Assert.IsTrue((neg + pos).IsZero, "부호가 반대인 같은 크기의 합이 0이 아닙니다");
            Assert.AreEqual(pos.ToString(), (-neg).ToString());
            Assert.IsFalse(neg.IsPositive);
        }

        [Test]
        public void BigNumber_0으로_나누면_오류를_남기고_Zero를_돌려준다()
        {
            LogAssert.Expect(LogType.Error, "[BigNumber] 0으로 나누기 시도. Zero를 반환합니다.");
            var result = new BigNumber(100.0) / BigNumber.Zero;
            Assert.IsTrue(result.IsZero, "0으로 나눈 결과가 Zero가 아닙니다");
        }

        [Test]
        public void BigNumber_극단_지수를_견딘다()
        {
            var huge = new BigNumber(1e150) * new BigNumber(1e150);
            Assert.AreEqual(300.0, huge.Log10(), 1e-6, "1e150 x 1e150이 1e300이 아닙니다");
            Assert.IsTrue(huge.IsPositive);

            // double이라면 여기서 무한대가 된다. BigNumber는 견뎌야 한다.
            var beyond = huge * huge;
            Assert.AreEqual(600.0, beyond.Log10(), 1e-6,
                "double 한계(약 1e308)를 넘는 순간 무너지면 환생 10회에서 세이브가 손상됩니다");
        }

        [Test]
        public void BigNumber_직렬화가_극단값에서도_왕복한다()
        {
            foreach (var v in new[]
            {
                BigNumber.Zero, BigNumber.One,
                new BigNumber(-1e-8), new BigNumber(1e150) * new BigNumber(1e150),
            })
            {
                var back = BigNumber.Deserialize(v.Serialize());
                Assert.AreEqual(v.ToString(6), back.ToString(6), $"왕복 실패: {v.Serialize()}");
            }
        }

        // ═══════════════════════════════════════════════════
        //  6. 오프라인 보상 경계
        // ═══════════════════════════════════════════════════

        [Test]
        public void 오프라인_코인은_기본상한_4시간에서_멈춘다()
        {
            var at4 = EconomyCore.CalculateOffline(cfg, cfg.offlineCapHours, 150, 1.0, false);
            var at99 = EconomyCore.CalculateOffline(cfg, 99.0, 150, 1.0, false);

            Assert.AreEqual(cfg.offlineCapHours, at4.CreditedHours, 1e-12);
            Assert.AreEqual(at4.AppliedRatio, at99.AppliedRatio, 1e-12,
                "상한을 넘겨도 코인 비율이 늘면 안 됩니다");
            Assert.AreEqual(cfg.offlineMaxRatio, at4.AppliedRatio, 1e-12);
        }

        [Test]
        public void 오프라인_젬은_확장상한_12시간까지_인정된다()
        {
            var at12 = EconomyCore.CalculateOffline(cfg, cfg.offlineCapHoursMax, 150, 1.0, false);
            var at99 = EconomyCore.CalculateOffline(cfg, 99.0, 150, 1.0, false);

            Assert.AreEqual((int)(cfg.offlineCapHoursMax * cfg.gemsPerHour), at12.Gems);
            Assert.AreEqual(at12.Gems, at99.Gems, "확장 상한을 넘겨도 젬이 늘면 안 됩니다");
        }

        [Test]
        public void 오프라인_비율은_광고를_봐도_상한을_넘지_않는다()
        {
            var withAd = EconomyCore.CalculateOffline(cfg, 99.0, 150, 1.0, true);

            Assert.AreEqual(cfg.offlineRatioCeiling, withAd.AppliedRatio, 1e-12,
                "광고 배수 적용 후 비율이 절대 상한을 넘었습니다");
            Assert.LessOrEqual(withAd.AppliedRatio, 0.60,
                "비율이 0.6을 넘으면 시작 웨이브 여유가 3웨이브밖에 안 남습니다");
        }

        [Test]
        public void ValidateOfflineIntegrity가_모든_구간에서_참이다()
        {
            foreach (int wave in new[] { 10, 48, 100, 170, 250, 300 })
            {
                foreach (double coinMul in new[] { 1.0, 10.0, 300.0 })
                {
                    Assert.IsTrue(EconomyCore.ValidateOfflineIntegrity(cfg, wave, coinMul),
                        $"웨이브 {wave}, 배수 {coinMul}: 오프라인 시작점이 직전 도달을 넘습니다");
                }
            }
        }

        [Test]
        public void 오프라인은_코어를_지급하지_않는다()
        {
            // 절대 규칙 2. OfflineReward에 코어 필드가 없는 것이 설계이며,
            // 필드가 추가되는 순간 이 테스트가 컴파일되지 않아야 한다.
            var fields = typeof(EconomyCore.OfflineReward).GetFields();
            foreach (var f in fields)
                Assert.IsFalse(f.Name.ToLower().Contains("core"),
                    $"OfflineReward에 코어 관련 필드 '{f.Name}'가 생겼습니다 — 90일 커브가 붕괴합니다");
        }
    }
}

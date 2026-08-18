using System;
using NUnit.Framework;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 경제 로직이 방치형디펜스_경제시뮬레이션.xlsx와 일치하는지 검증한다.
    /// 수치를 변경했다면 이 테스트를 먼저 돌릴 것.
    /// </summary>
    public class EconomyTests
    {
        private EconomyConfig cfg;

        [SetUp]
        public void SetUp()
        {
            // 스프레드시트 '가정' 시트 기본값과 동일
            cfg = ScriptableObject.CreateInstance<EconomyConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (cfg != null) UnityEngine.Object.DestroyImmediate(cfg);
        }

        // ───────── BigNumber ─────────

        [Test]
        public void BigNumber_기본연산()
        {
            Assert.AreEqual(123.0, (new BigNumber(100) + new BigNumber(23)).ToDouble(), 1e-9);
            Assert.AreEqual(77.0, (new BigNumber(100) - new BigNumber(23)).ToDouble(), 1e-9);
            Assert.AreEqual(1e20, (new BigNumber(1e10) * new BigNumber(1e10)).ToDouble(), 1e10);
            Assert.AreEqual(1e10, (new BigNumber(1e20) / new BigNumber(1e10)).ToDouble(), 1.0);
        }

        [Test]
        public void BigNumber_double한계를_넘는다()
        {
            var huge = BigNumber.Pow10(800);
            Assert.AreEqual(800, huge.Exponent);
            Assert.AreEqual(400, (huge / BigNumber.Pow10(400)).Exponent);
        }

        [Test]
        public void BigNumber_직렬화_왕복()
        {
            foreach (double v in new[] { 0.0, 1.0, -1.0, 1234.5678, 1e15, 9.87e120 })
            {
                var orig = new BigNumber(v);
                Assert.AreEqual(orig, BigNumber.Deserialize(orig.Serialize()), $"값 {v}");
            }
            var deep = BigNumber.Pow10(999);
            Assert.AreEqual(deep, BigNumber.Deserialize(deep.Serialize()));
        }

        [Test]
        public void BigNumber_깨진_세이브는_Zero()
        {
            Assert.IsTrue(BigNumber.Deserialize("").IsZero);
            Assert.IsTrue(BigNumber.Deserialize("garbage|xx").IsZero);
        }

        [Test]
        public void BigNumber_표기()
        {
            Assert.AreEqual("999", new BigNumber(999).ToString());
            Assert.AreEqual("1K", new BigNumber(1000).ToString());
            Assert.AreEqual("1.5M", new BigNumber(1.5e6).ToString());
            Assert.AreEqual("1B", new BigNumber(1e9).ToString());
            Assert.AreEqual("1T", new BigNumber(1e12).ToString());
            Assert.AreEqual("-1.23K", new BigNumber(-1234.5).ToString());
        }

        // ───────── 설정 정합성 ─────────

        [Test]
        public void Config_기본값이_유효하다()
        {
            Assert.IsTrue(cfg.Validate(out string err), err);
        }

        [Test]
        public void Config_코인증가율이_체력증가율보다_낮다()
        {
            // 이 격차가 '벽'을 만든다. 뒤집히면 게임이 무한 진행된다.
            Assert.Less(cfg.coinGrowth, cfg.enemyHpGrowth);
        }

        [Test]
        public void Config_잘못된_설정을_잡아낸다()
        {
            cfg.coinGrowth = cfg.enemyHpGrowth + 0.01;
            Assert.IsFalse(cfg.Validate(out _));

            cfg = ScriptableObject.CreateInstance<EconomyConfig>();
            cfg.offlineMaxRatio = 0.5;
            Assert.IsFalse(cfg.Validate(out _));
        }

        // ───────── 1층: 웨이브 곡선 ─────────

        [Test]
        public void 웨이브_소요시간이_스프레드시트와_일치()
        {
            // 스프레드시트 '웨이브시뮬레이션' 시트 H열
            var expected = new (int wave, double seconds)[]
            {
                (1, 12.5), (10, 4.9), (20, 7.6), (30, 13.7),
                (40, 26.2), (50, 52.1), (60, 108.4)
            };

            foreach (var (wave, seconds) in expected)
            {
                var cum = EconomyCore.CumulativeCoin(cfg, wave);
                var dps = EconomyCore.BaseDpsAtLevel(cfg, EconomyCore.AffordableLevel(cfg, cum));
                double actual = EconomyCore.WaveClearSeconds(cfg, wave, dps);
                Assert.AreEqual(seconds, actual, seconds * 0.01, $"웨이브 {wave}");
            }
        }

        [Test]
        public void 벽은_웨이브_48에서_발생한다()
        {
            int wall = 0;
            for (int n = 1; n <= 100; n++)
            {
                var cum = EconomyCore.CumulativeCoin(cfg, n);
                var dps = EconomyCore.BaseDpsAtLevel(cfg, EconomyCore.AffordableLevel(cfg, cum));
                if (EconomyCore.IsWall(cfg, n, dps)) { wall = n; break; }
            }
            Assert.AreEqual(48, wall);
        }

        [Test]
        public void 누적코인_역산이_정확하다()
        {
            for (int w = 10; w <= 200; w += 10)
            {
                var cum = EconomyCore.CumulativeCoin(cfg, w);
                double back = EconomyCore.WaveFromCumulativeCoin(cfg, cum);
                Assert.AreEqual(w, back, 0.01, $"웨이브 {w} 역산");
            }
        }

        // ───────── 2층: 환생 메타 ─────────

        [Test]
        [Ignore("P0 재작성 대기 - 단일 트랙 DPS 모델이라 오방색 5트랙 곱연산이 빠져 있다. docs/P0_검증스위트_재작성_계획.md 3.2 참조")]
        public void 환생메타_여유가_300회차_내내_1이상()
        {
            double cores = 0; int tier = 1;
            double minSlack = double.MaxValue;
            int worstRun = 0;

            for (int k = 1; k <= 300; k++)
            {
                int wave = EconomyCore.TargetWave(cfg, k);
                double needLog = (EconomyCore.WaveTotalHp(cfg, wave)
                                / new BigNumber(cfg.waveTimeWall)).Log10();
                double coinMul = EconomyCore.CoinMultiplier(cfg, cores, tier);
                double atkMul = EconomyCore.AttackMultiplier(cfg, cores, tier);
                int lv = EconomyCore.AffordableLevel(cfg,
                            EconomyCore.CumulativeCoin(cfg, wave, coinMul));
                double haveLog = (EconomyCore.BaseDpsAtLevel(cfg, lv) * atkMul).Log10();
                double slack = Math.Pow(10, haveLog - needLog);

                if (slack < minSlack) { minSlack = slack; worstRun = k; }

                cores += EconomyCore.CoreGain(cfg, wave);
                if (EconomyCore.CanAscend(cfg, tier, wave, cores))
                {
                    tier++;
                    cores = EconomyCore.CoresAfterAscend(cfg, cores);
                }
            }

            Assert.GreaterOrEqual(minSlack, 1.0,
                $"회차 {worstRun}에서 여유 {minSlack:F3} — 진행 불가 구간이 존재합니다");
        }

        [Test]
        public void 도달웨이브가_단조증가한다()
        {
            int prev = 0;
            for (int k = 1; k <= 300; k++)
            {
                int w = EconomyCore.TargetWave(cfg, k);
                Assert.GreaterOrEqual(w, prev, $"회차 {k}");
                prev = w;
            }
        }

        [Test]
        public void _90일차에_웨이브_170_티어6()
        {
            int runs = (int)Math.Round(90 * cfg.runsPerDay);
            Assert.AreEqual(270, runs);
            Assert.AreEqual(170, EconomyCore.TargetWave(cfg, runs));
        }

        [Test]
        public void _90일_이후에도_티어가_남아있다()
        {
            // 콘텐츠 조기 소진 방어: 90일차에 최대 티어에 도달하면 안 된다
            double cores = 0; int tier = 1;
            for (int k = 1; k <= 270; k++)
            {
                int wave = EconomyCore.TargetWave(cfg, k);
                cores += EconomyCore.CoreGain(cfg, wave);
                if (EconomyCore.CanAscend(cfg, tier, wave, cores))
                {
                    tier++;
                    cores = EconomyCore.CoresAfterAscend(cfg, cores);
                }
            }
            Assert.Less(tier, cfg.tierGates.Length + 1,
                "90일차에 최대 티어 도달 — 다음 목표가 없습니다");
        }

        // ───────── 3층: 오방색 ─────────

        [Test]
        public void 오방색_곱연산()
        {
            int[] levels = { 10, 10, 10, 10, 10 };
            double expected = (1 + 10 * 0.10) * (1 + 10 * 0.06) * (1 + 10 * 0.03);
            Assert.AreEqual(expected, EconomyCore.CombatMultiplier(levels), 1e-9);
            Assert.AreEqual(1.0, EconomyCore.CombatMultiplier(new[] { 0, 0, 0, 0, 0 }), 1e-9);
        }

        [Test]
        public void 오방색_단일트랙보다_다중트랙이_강하다()
        {
            // 같은 총 레벨이면 분산 투자가 곱연산으로 더 강해야 한다
            double single = EconomyCore.CombatMultiplier(new[] { 30, 0, 0, 0, 0 });
            double spread = EconomyCore.CombatMultiplier(new[] { 10, 10, 0, 0, 10 });
            Assert.Greater(spread, single);
        }

        // ───────── 4층: 오프라인 (최중요) ─────────

        [Test]
        public void 오프라인_보상은_절대_도달웨이브를_넘지_않는다()
        {
            // 이것이 깨지면 90일 커브가 즉시 붕괴한다
            foreach (int wave in new[] { 45, 55, 95, 127, 153, 170, 200 })
            {
                foreach (double hours in new[] { 0.5, 1, 4, 12, 24, 999.0 })
                {
                    foreach (bool ad in new[] { false, true })
                    {
                        var r = EconomyCore.CalculateOffline(cfg, hours, wave, 1.0, ad);
                        Assert.Less(r.StartWave, wave,
                            $"웨이브 {wave}, {hours}시간, 광고 {ad} — 시작 웨이브가 도달 웨이브 이상");
                    }
                }
            }
        }

        [Test]
        public void 오프라인_보상에_코어가_없다()
        {
            // 코어(도깨비불)를 오프라인으로 주면 커브가 붕괴한다.
            // 필드 이름 자체를 검사해 실수로 추가되는 것을 막는다.
            var fields = typeof(EconomyCore.OfflineReward).GetFields();
            foreach (var f in fields)
            {
                StringAssert.DoesNotContain("core", f.Name.ToLower(),
                    $"OfflineReward에 코어 관련 필드가 있습니다: {f.Name}");
                StringAssert.DoesNotContain("rebirth", f.Name.ToLower());
            }
        }

        [Test]
        public void 오프라인_상한이_작동한다()
        {
            var at4 = EconomyCore.CalculateOffline(cfg, 4, 127, 1.0, false);
            var at24 = EconomyCore.CalculateOffline(cfg, 24, 127, 1.0, false);
            Assert.AreEqual(at4.AppliedRatio, at24.AppliedRatio, 1e-12,
                "상한을 넘어서도 코인 보상이 계속 늘어납니다");
            Assert.AreEqual(cfg.offlineMaxRatio, at4.AppliedRatio, 1e-12);
        }

        [Test]
        public void 오프라인_광고배수가_적용된다()
        {
            var plain = EconomyCore.CalculateOffline(cfg, 4, 127, 1.0, false);
            var withAd = EconomyCore.CalculateOffline(cfg, 4, 127, 1.0, true);
            Assert.Greater(withAd.Coin, plain.Coin);
            Assert.AreEqual(plain.AppliedRatio * cfg.offlineAdMultiplier,
                            withAd.AppliedRatio, 1e-12);
        }

        [Test]
        public void 오프라인_젬은_확장상한까지_인정()
        {
            Assert.AreEqual(4 * cfg.gemsPerHour,
                            EconomyCore.CalculateOffline(cfg, 4, 127, 1.0, false).Gems);
            Assert.AreEqual(12 * cfg.gemsPerHour,
                            EconomyCore.CalculateOffline(cfg, 12, 127, 1.0, false).Gems);
            // 확장 상한을 넘으면 더 안 준다
            Assert.AreEqual(12 * cfg.gemsPerHour,
                            EconomyCore.CalculateOffline(cfg, 48, 127, 1.0, false).Gems);
        }

        [Test]
        public void 오프라인_무결성_헬퍼가_동작한다()
        {
            foreach (int wave in new[] { 45, 100, 170, 250 })
                Assert.IsTrue(EconomyCore.ValidateOfflineIntegrity(cfg, wave, 1.0), $"웨이브 {wave}");
        }
    }
}

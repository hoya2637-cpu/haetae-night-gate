using System;
using NUnit.Framework;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Save;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 세이브 조작 방어 검증.
    ///
    /// 전제 — 공격자는 체크섬을 다시 계산할 수 있다.
    ///   체크섬은 '파일이 깨졌는가'만 알려주며, 알고리즘이 클라이언트에 있으므로
    ///   조작 후 재계산하면 정상 파일처럼 통과한다.
    ///   따라서 값 자체가 말이 되는 범위인지 별도로 검사해야 한다.
    ///
    /// 이 테스트의 목적은 '완벽한 치팅 차단'이 아니다.
    ///   루팅 기기의 메모리 조작은 클라이언트로 막을 수 없다.
    ///   목표는 (1) 캐주얼한 파일 편집 차단
    ///          (2) 게임이 수학적으로 붕괴하는 값 차단
    ///   두 가지다. 실제 치팅 방지가 필요해지면 서버 검증이 유일한 답이다.
    /// </summary>
    public class SaveTamperTests
    {
        private EconomyConfig cfg;

        [SetUp]
        public void SetUp() => cfg = ScriptableObject.CreateInstance<EconomyConfig>();

        [TearDown]
        public void TearDown()
        {
            if (cfg != null) UnityEngine.Object.DestroyImmediate(cfg);
        }

        private GameState Tampered(Action<GameState> mutate)
        {
            var s = GameState.CreateNew();
            mutate(s);
            s.EnsureIntegrity(cfg);
            return s;
        }

        [Test]
        public void 티어_조작이_차단된다()
        {
            // 가장 위험한 조작. tier 99면 티어 배수가 2.5^98이 되어
            // 게임이 즉시 붕괴하고 double 범위를 넘어간다.
            var s = Tampered(x => x.tier = 99);
            Assert.LessOrEqual(s.tier, cfg.tierGates.Length + 1);

            var neg = Tampered(x => x.tier = -3);
            Assert.GreaterOrEqual(neg.tier, 1);
        }

        [Test]
        public void 코어_조작이_차단된다()
        {
            // 90일 실측 최대치는 약 46,000 (헤비 유저)
            var s = Tampered(x => x.cores = 1e30);
            Assert.Less(s.cores, 1e13, "코어 상한이 걸리지 않았습니다");

            Assert.AreEqual(0, Tampered(x => x.cores = -100).cores);
            Assert.AreEqual(0, Tampered(x => x.cores = double.NaN).cores);
            Assert.AreEqual(0, Tampered(x => x.cores = double.PositiveInfinity).cores);
        }

        [Test]
        public void 구슬_조작이_차단된다()
        {
            // 90일 실측 최대치는 약 3,300
            var s = Tampered(x => x.gems = int.MaxValue);
            Assert.LessOrEqual(s.gems, 1000000);
            Assert.AreEqual(0, Tampered(x => x.gems = -500).gems);
        }

        [Test]
        public void 웨이브_조작이_차단된다()
        {
            // 90일 실측 최대치는 약 210
            Assert.LessOrEqual(Tampered(x => x.bestWave = int.MaxValue).bestWave, 100000);
            Assert.LessOrEqual(Tampered(x => x.lastRunWave = 999999).lastRunWave, 100000);
            Assert.GreaterOrEqual(Tampered(x => x.currentWave = 0).currentWave, 1);
        }

        [Test]
        public void 오프라인_상한_조작이_차단된다()
        {
            // 확장 상한을 넘기면 오프라인 보상이 설계 범위를 벗어난다
            var s = Tampered(x => x.offlineCapHours = 10000);
            Assert.LessOrEqual(s.offlineCapHours, cfg.offlineCapHoursMax);
            Assert.Greater(Tampered(x => x.offlineCapHours = 0).offlineCapHours, 0);
        }

        [Test]
        public void 트랙레벨_조작이_차단된다()
        {
            var s = Tampered(x => x.trackLevels = new[] { int.MaxValue, -5, 0, 0, 0 });
            foreach (int lv in s.trackLevels)
            {
                Assert.GreaterOrEqual(lv, 0);
                Assert.LessOrEqual(lv, 50000);
            }
        }

        [Test]
        public void 배열_길이_조작이_차단된다()
        {
            Assert.AreEqual(5, Tampered(x => x.trackLevels = new int[99]).trackLevels.Length);
            Assert.AreEqual(5, Tampered(x => x.trackLevels = new int[1]).trackLevels.Length);
            Assert.AreEqual(5, Tampered(x => x.trackLevels = null).trackLevels.Length);
        }

        [Test]
        public void 미래_시각_조작이_차단된다()
        {
            // 미래 시각을 넣으면 자리비움 계산이 음수가 된다
            var s = Tampered(x => x.LastSeenUtc = DateTime.UtcNow.AddYears(10));
            Assert.LessOrEqual(s.AwayHours(DateTime.UtcNow), 0.01);
        }

        [Test]
        public void 코인_조작이_BigNumber_계층에서_차단된다()
        {
            var s = GameState.CreateNew();
            s.coinSerialized = "Infinity|0";
            s.EnsureIntegrity(cfg);
            Assert.IsFalse(double.IsInfinity(s.Coin.Mantissa));

            s.coinSerialized = "1.5|2147483647";
            s.EnsureIntegrity(cfg);
            Assert.IsTrue(s.Coin.IsZero, "지수 범위 초과가 차단되지 않았습니다");
        }

        [Test]
        public void 정상값은_변형되지_않는다()
        {
            // 방어가 정상 유저를 건드리면 안 된다.
            // 90일 헤비 유저의 실측치를 그대로 넣어본다.
            var s = GameState.CreateNew();
            s.tier = 7;
            s.cores = 45422;
            s.gems = 670;
            s.bestWave = 210;
            s.lastRunWave = 210;
            s.runIndex = 540;
            s.offlineCapHours = 12;
            s.trackLevels = new[] { 62, 40, 30, 10, 25 };
            s.EnsureIntegrity(cfg);

            Assert.AreEqual(7, s.tier);
            Assert.AreEqual(45422, s.cores);
            Assert.AreEqual(670, s.gems);
            Assert.AreEqual(210, s.bestWave);
            Assert.AreEqual(540, s.runIndex);
            Assert.AreEqual(12, s.offlineCapHours);
            Assert.AreEqual(62, s.trackLevels[0]);
        }

        [Test]
        public void 광고제거는_클라이언트로_막을_수_없음을_명시한다()
        {
            // adsRemoved는 true/false 둘 다 정상값이라 범위 검증이 불가능하다.
            // 이 테스트는 '막힌다'가 아니라 '막을 수 없음을 알고 있다'는 기록이다.
            //
            // 최종 구조는 세이브를 신뢰하지 않는 것이어야 한다:
            //   스토어 구매 → 영수증 검증 → adsRemoved 활성화
            // 즉 세이브는 캐시이고 권한의 원본은 스토어 검증 결과다.
            var s = Tampered(x => x.adsRemoved = true);
            Assert.IsTrue(s.adsRemoved,
                "이 값이 false가 됐다면 정상 구매자의 광고 제거도 풀린다는 뜻입니다");
        }

        // ───────── 경계값 ─────────
        //
        // 극단값(int.MaxValue, 1e30)만 검사하면 상한 바로 앞뒤에서
        // off-by-one이 생겨도 못 잡는다.
        // 상한 -1 / 상한 / 상한 +1 세 지점을 모두 고정한다.

        [Test]
        public void 티어_경계값()
        {
            int max = cfg.tierGates.Length + 1;
            Assert.AreEqual(max - 1, Tampered(x => x.tier = max - 1).tier);
            Assert.AreEqual(max, Tampered(x => x.tier = max).tier, "상한값 자체는 허용되어야 한다");
            Assert.AreEqual(max, Tampered(x => x.tier = max + 1).tier);
            Assert.AreEqual(1, Tampered(x => x.tier = 1).tier);
            Assert.AreEqual(1, Tampered(x => x.tier = 0).tier);
        }

        [Test]
        public void 구슬_경계값()
        {
            const int Max = 1000000;
            Assert.AreEqual(Max - 1, Tampered(x => x.gems = Max - 1).gems);
            Assert.AreEqual(Max, Tampered(x => x.gems = Max).gems);
            Assert.AreEqual(Max, Tampered(x => x.gems = Max + 1).gems);
            Assert.AreEqual(0, Tampered(x => x.gems = 0).gems);
            Assert.AreEqual(0, Tampered(x => x.gems = -1).gems);
        }

        [Test]
        public void 코어_경계값()
        {
            // 1e12 부근에서 double은 정수 1 단위까지 정확히 구분한다
            // (가수 53비트 = 약 9x10^15까지 정수 표현 가능).
            // 따라서 상한 ±1 비교가 의미를 갖는다.
            const double Max = 1e12;
            Assert.IsTrue(Max + 1 != Max, "double이 상한 부근에서 1을 구분하지 못합니다");

            Assert.AreEqual(Max - 1, Tampered(x => x.cores = Max - 1).cores);
            Assert.AreEqual(Max, Tampered(x => x.cores = Max).cores);
            Assert.AreEqual(Max, Tampered(x => x.cores = Max + 1).cores);
            Assert.AreEqual(0, Tampered(x => x.cores = 0).cores);
            Assert.AreEqual(0, Tampered(x => x.cores = -0.0001).cores);
        }

        [Test]
        public void 웨이브_경계값()
        {
            const int Max = 100000;
            Assert.AreEqual(Max - 1, Tampered(x => x.bestWave = Max - 1).bestWave);
            Assert.AreEqual(Max, Tampered(x => x.bestWave = Max).bestWave);
            Assert.AreEqual(Max, Tampered(x => x.bestWave = Max + 1).bestWave);
            Assert.AreEqual(1, Tampered(x => x.currentWave = 1).currentWave);
            Assert.AreEqual(1, Tampered(x => x.currentWave = 0).currentWave);
        }

        [Test]
        public void 트랙레벨_경계값()
        {
            const int Max = 50000;
            Assert.AreEqual(Max - 1, Tampered(x => x.trackLevels = new[] { Max - 1, 0, 0, 0, 0 }).trackLevels[0]);
            Assert.AreEqual(Max, Tampered(x => x.trackLevels = new[] { Max, 0, 0, 0, 0 }).trackLevels[0]);
            Assert.AreEqual(Max, Tampered(x => x.trackLevels = new[] { Max + 1, 0, 0, 0, 0 }).trackLevels[0]);
        }

        [Test]
        public void 오프라인상한_경계값()
        {
            double max = cfg.offlineCapHoursMax;
            Assert.AreEqual(max - 0.1, Tampered(x => x.offlineCapHours = max - 0.1).offlineCapHours, 1e-9);
            Assert.AreEqual(max, Tampered(x => x.offlineCapHours = max).offlineCapHours, 1e-9);
            Assert.AreEqual(max, Tampered(x => x.offlineCapHours = max + 0.1).offlineCapHours, 1e-9);
            Assert.Greater(Tampered(x => x.offlineCapHours = 0).offlineCapHours, 0);
        }
    }
}

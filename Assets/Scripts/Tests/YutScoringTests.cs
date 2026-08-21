using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 윷 보상 계약 검증.
    ///
    /// 이 스위트가 지키는 계약은 하나다.
    ///
    ///   **미니게임은 벽 판정에 들어가지 않는 축으로만 보상한다.**
    ///
    /// 이건 취향이 아니라 90일 곡선의 전제다. 코어(도깨비불)나 코인 배율이 새면
    /// 티어 진행이 조작 실력에 종속되고, 그 순간 경제 설계가 아무것도 예측하지 못한다.
    ///
    /// ★ 그리고 그건 화면으로 안 보인다.
    ///   부적배수 x0.98이 164개 테스트를 전부 통과한 채로 살아 있었던 그 종류다.
    ///   눈으로 잡을 수 없는 계약은 테스트로 고정한다.
    /// </summary>
    public class YutScoringTests
    {
        private const int Seed = 20260820;

        // ─────────────────────────────────────────
        // 계약

        [Test]
        public void 계약_보상은_부적배수_하나뿐이다()
        {
            // Outcome이 줄 수 있는 것을 여기 못박는다.
            // 누가 Cores나 CoinMultiplier를 추가하면 이 테스트가 먼저 실패하고,
            // 그때 "이게 벽 판정에 들어가는 축인가"를 반드시 묻게 된다.
            var expected = new HashSet<string> { "TalismanMultiplier", "ConsolationCount" };
            var actual = new HashSet<string>();
            foreach (var f in typeof(YutScoring.Outcome)
                         .GetFields(BindingFlags.Public | BindingFlags.Instance))
                actual.Add(f.Name);

            CollectionAssert.AreEquivalent(expected, actual,
                "윷 보상에 새 축이 생겼다. 그것이 BattleRunner의 벽 판정(BaseDpsWithoutTalisman)에 " +
                "들어가는 축이라면 도달 웨이브가 움직이고 90일 곡선이 무너진다. " +
                "엽전 배율로 실측했을 때 최고웨이브가 233→235로 밀렸다.");
        }

        [Test]
        public void 계약_배수는_1_미만으로_내려가지_않는다()
        {
            // 마이너스 없음. 부르기 실패도 손해가 아니라 기회 상실이다.
            foreach (YutResult r in Enum.GetValues(typeof(YutResult)))
                foreach (YutCall c in Enum.GetValues(typeof(YutCall)))
                    Assert.GreaterOrEqual(YutScoring.ThrowMultiplier(r, c), 1.0,
                        $"{YutGame.DisplayName(r)} / 부름 {YutScoring.DisplayName(c)}");
        }

        [Test]
        public void 계약_최적_플레이가_상한_아래다()
        {
            // ★ 평균이 아니라 최적을 잰다.
            //   부르기가 있으면 "매번 모를 부르는" 유저가 최댓값이 되고,
            //   그 최댓값이 상한 아래여야 나머지가 자동으로 안전해진다.
            double perGame = MeasureAverage(YutCall.Mo, 200000);
            double twoGames = perGame * perGame;
            double cut = 1.0 - 1.0 / twoGames;

            // 부적 33.2% · 광고 31%. 미니게임은 그보다 확실히 작아야 한다.
            Assert.Less(cut, 0.25,
                $"하루 2판 최적 플레이 단축률 {cut:P1}. 부적·광고를 넘보면 안 된다.");
        }

        // ─────────────────────────────────────────
        // 보상 구조 — 모 아니면 도

        [Test]
        public void 도개걸은_배수를_주지_않는다()
        {
            // 확률이 이분법(84.5% vs 15.5%)이면 보상도 이분법이어야 한다.
            // 평평하게 깔면 어느 결과든 "그럭저럭"이 되고, 빠지는 지점이 사라진다.
            Assert.AreEqual(1.0, YutScoring.ThrowMultiplier(YutResult.Do), 1e-9);
            Assert.AreEqual(1.0, YutScoring.ThrowMultiplier(YutResult.Gae), 1e-9);
            Assert.AreEqual(1.0, YutScoring.ThrowMultiplier(YutResult.Geol), 1e-9);
        }

        [Test]
        public void 윷과_모만_배수를_준다()
        {
            Assert.Greater(YutScoring.ThrowMultiplier(YutResult.Yut), 1.0);
            Assert.Greater(YutScoring.ThrowMultiplier(YutResult.Mo), 1.0);
        }

        [Test]
        public void 모가_윷보다_후하다()
        {
            // 고증 — 윷놀이에서 모는 5칸, 윷은 4칸이다. 윷이 더 희귀한데도 그렇다.
            Assert.Greater(YutScoring.ThrowMultiplier(YutResult.Mo),
                           YutScoring.ThrowMultiplier(YutResult.Yut));
        }

        [Test]
        public void 눈당_값이_설계값과_같다()
        {
            Assert.AreEqual(1.24, YutScoring.ThrowMultiplier(YutResult.Yut), 1e-9, "윷 +24%");
            Assert.AreEqual(1.30, YutScoring.ThrowMultiplier(YutResult.Mo), 1e-9, "모 +30%");
        }

        // ─────────────────────────────────────────
        // 부르기

        [Test]
        public void 부르고_맞히면_두_배다()
        {
            Assert.AreEqual(1.60, YutScoring.ThrowMultiplier(YutResult.Mo, YutCall.Mo), 1e-9);
            Assert.AreEqual(1.48, YutScoring.ThrowMultiplier(YutResult.Yut, YutCall.Yut), 1e-9);
        }

        [Test]
        public void 부르고_빗나가면_위로상도_없다()
        {
            Assert.AreEqual(1.0, YutScoring.ThrowMultiplier(YutResult.Do, YutCall.Mo), 1e-9);
            Assert.IsFalse(YutScoring.Consolation(YutResult.Do, YutCall.Mo),
                "부르고 빗나갔으면 위로상도 없다. 그게 부르기의 유일한 비용이다.");
            Assert.IsTrue(YutScoring.Consolation(YutResult.Do),
                "안 부르고 도가 나오면 위로상은 받는다.");
        }

        [Test]
        public void 도를_부르는_것은_아무_의미가_없다()
        {
            // 도·개·걸은 눈이 0이라 맞혀도 배수가 1.0이다.
            // 부를 이유가 구조적으로 없다는 뜻이며, 이게 UI를 모 하나로 줄이는 근거다.
            Assert.AreEqual(1.0, YutScoring.ThrowMultiplier(YutResult.Do, YutCall.Do), 1e-9);
            Assert.AreEqual(1.0, YutScoring.ThrowMultiplier(YutResult.Gae, YutCall.Gae), 1e-9);
        }

        [Test]
        public void 제안하는_부르기는_모_하나뿐이다()
        {
            // 실측(40만 판): 모 9.2% / 안 부름 5.4% / 윷 1.5%.
            // 윷 부르기는 2.6% 확률을 맞히려다 나머지를 다 버린다 — 함정이다.
            // 다섯 개를 보여주고 넷이 함정이면 그건 선택지가 아니라 벌이다.
            CollectionAssert.AreEqual(new[] { YutCall.Mo }, YutScoring.OfferedCalls);
        }

        [Test]
        public void 모_부르기가_안_부르기보다_기댓값이_높다()
        {
            // 높아야 선택할 이유가 생긴다. 다만 87%는 빈손이라 안전을 택할 이유도 남는다.
            // 둘 다 성립해야 진짜 선택이 된다.
            double call = MeasureAverage(YutCall.Mo, 200000);
            double none = MeasureAverage(YutCall.None, 200000);
            double yut  = MeasureAverage(YutCall.Yut, 200000);

            Assert.Greater(call, none, "모 부르기가 안 부르기보다 나아야 부를 이유가 있다.");
            Assert.Less(yut, none, "윷 부르기는 안 부르는 것보다 나쁘다 — 그래서 제안하지 않는다.");
        }

        // ─────────────────────────────────────────
        // 판 단위 합산

        [Test]
        public void 판_배수는_던지기의_곱이다()
        {
            var throws = new List<YutResult> { YutResult.Mo, YutResult.Mo, YutResult.Do };
            var o = YutScoring.Score(throws);
            Assert.AreEqual(1.30 * 1.30 * 1.00, o.TalismanMultiplier, 1e-9);
            Assert.AreEqual(1, o.ConsolationCount, "도 하나만 위로상");
        }

        [Test]
        public void 연쇄로_모를_두_번_부르면_배수가_두_배를_넘는다()
        {
            var throws = new List<YutResult> { YutResult.Mo, YutResult.Mo, YutResult.Gae };
            var calls  = new List<YutCall>   { YutCall.Mo,   YutCall.Mo,   YutCall.Mo };
            var o = YutScoring.Score(throws, calls);

            Assert.AreEqual(1.60 * 1.60, o.TalismanMultiplier, 1e-9);
            Assert.Greater(o.TalismanMultiplier, 2.0,
                "이런 판이 기억에 남는 판이다. 하루 2판이면 한 달에 한 번쯤 온다.");
            Assert.AreEqual(0, o.ConsolationCount, "전부 불렀으므로 위로상은 없다");
        }

        [Test]
        public void 빈_입력은_배수_1을_준다()
        {
            Assert.AreEqual(1.0, YutScoring.Score(null).TalismanMultiplier, 1e-9);
            Assert.AreEqual(1.0, YutScoring.Score(new List<YutResult>()).TalismanMultiplier, 1e-9);
        }

        // ─────────────────────────────────────────

        /// <summary>한 판의 평균 배수. 매 던지기마다 같은 것을 부른다.</summary>
        private static double MeasureAverage(YutCall call, int games)
        {
            var game = new YutGame(Seed);
            double total = 0.0;
            for (int i = 0; i < games; i++)
            {
                var s = game.Play();
                var calls = new List<YutCall>(s.Throws.Count);
                for (int k = 0; k < s.Throws.Count; k++) calls.Add(call);
                total += YutScoring.Score(s.Throws, calls).TalismanMultiplier;
            }
            return total / games;
        }
    }
}

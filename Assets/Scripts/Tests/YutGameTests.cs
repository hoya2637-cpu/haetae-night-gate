using System;
using System.Collections.Generic;
using NUnit.Framework;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 윷놀이 검증.
    ///
    /// 이 스위트가 지키려는 계약은 둘이다.
    ///
    ///   1. **분포가 실제 윷과 같다.** 모가 윷보다 흔해야 한다.
    ///      균등 확률(동전 4개)로 바뀌면 이 관계가 뒤집히고,
    ///      가장 신나는 결과가 가장 안 나오는 게임이 된다.
    ///
    ///   2. **기대 배수가 이론값과 맞는다.** 기본 보상 계수를 이 값으로 역산하므로,
    ///      여기가 틀리면 밸런스 산정 전체가 틀린 수 위에 서게 된다.
    ///
    /// ★ 여기에 '재미'에 대한 단언은 없다. 그건 측정할 수 없다.
    ///   여기서 지키는 것은 숫자가 설계 문서와 같은 값을 말하는가뿐이다.
    /// </summary>
    public class YutGameTests
    {
        private const int Seed = 20260820;

        // ─────────────────────────────────────────
        // 이론 분포

        [Test]
        public void 확률의_합은_1이다()
        {
            double sum = 0.0;
            foreach (YutResult r in Enum.GetValues(typeof(YutResult)))
                sum += YutGame.Probability(r);

            Assert.AreEqual(1.0, sum, 1e-9,
                "다섯 결과가 표본공간 전부여야 한다. 합이 1이 아니면 경우를 빠뜨린 것이다.");
        }

        [Test]
        public void 모가_윷보다_흔하다()
        {
            double mo = YutGame.Probability(YutResult.Mo);
            double yut = YutGame.Probability(YutResult.Yut);

            Assert.Greater(mo, yut,
                "실물 윷가락은 등이 둥글어 엎어질 확률이 높다. 모(13%)가 윷(2.6%)보다 흔한 것이 " +
                "윷놀이가 재미있는 이유다. 이 단언이 깨졌다면 BackProbability가 0.5로 바뀐 것이고, " +
                "그러면 가장 신나는 결과가 가장 안 나오는 게임이 된다.");
        }

        [Test]
        public void 개가_가장_흔한_축에_속한다()
        {
            // p=0.6에서 도와 개가 정확히 같은 확률(0.3456)로 최빈이다.
            // 우연이 아니라 4·(1-p)·p³ = 6·(1-p)²·p² 가 p=0.6에서 성립하기 때문이다.
            double gae = YutGame.Probability(YutResult.Gae);
            foreach (YutResult r in Enum.GetValues(typeof(YutResult)))
                Assert.LessOrEqual(YutGame.Probability(r), gae + 1e-9,
                    $"{YutGame.DisplayName(r)}가 개보다 흔할 수 없다.");
        }

        [Test]
        public void 기대_배수가_설계값과_같다()
        {
            // 설계 문서(민속놀이_미니게임_설계 5장)가 적어둔 값.
            // 기본 보상 계수 k를 이 수로 역산하므로 여기가 정본이다.
            Assert.AreEqual(2.248, YutGame.ExpectedSingle(), 0.001,
                "한 번 던졌을 때의 기대 배수");
            Assert.AreEqual(2.661, YutGame.ExpectedSession(), 0.005,
                "연쇄를 포함한 한 판의 기대 배수. 이 값이 바뀌면 보상 계수를 다시 잡아야 한다.");
        }

        // ─────────────────────────────────────────
        // 실제 굴림이 이론과 맞는가

        [Test]
        public void 실측_분포가_이론과_맞는다()
        {
            const int N = 200000;
            var game = new YutGame(Seed);
            var count = new Dictionary<YutResult, int>();
            foreach (YutResult r in Enum.GetValues(typeof(YutResult))) count[r] = 0;

            for (int i = 0; i < N; i++) count[game.Throw()]++;

            foreach (YutResult r in Enum.GetValues(typeof(YutResult)))
            {
                double observed = (double)count[r] / N;
                double expected = YutGame.Probability(r);
                Assert.AreEqual(expected, observed, 0.01,
                    $"{YutGame.DisplayName(r)} 실측 {observed:P2} vs 이론 {expected:P2}");
            }
        }

        [Test]
        public void 실측_기대배수가_이론과_맞는다()
        {
            const int N = 100000;
            var game = new YutGame(Seed + 1);
            long total = 0;
            for (int i = 0; i < N; i++) total += game.Play().Multiplier;

            double observed = (double)total / N;
            Assert.AreEqual(YutGame.ExpectedSession(), observed, 0.03,
                $"한 판 평균 배수 실측 {observed:F3}");
        }

        // ─────────────────────────────────────────
        // 연쇄 규칙

        [Test]
        public void 윷과_모만_한_번_더_던진다()
        {
            Assert.IsTrue(YutGame.ThrowsAgain(YutResult.Yut));
            Assert.IsTrue(YutGame.ThrowsAgain(YutResult.Mo));
            Assert.IsFalse(YutGame.ThrowsAgain(YutResult.Do));
            Assert.IsFalse(YutGame.ThrowsAgain(YutResult.Gae));
            Assert.IsFalse(YutGame.ThrowsAgain(YutResult.Geol));
        }

        [Test]
        public void 한_판은_윷도_모도_아닌_결과로_끝난다()
        {
            var game = new YutGame(Seed + 2);
            for (int i = 0; i < 5000; i++)
            {
                var s = game.Play();
                Assert.IsFalse(s.ChainCapped, "정상 플레이에서 연쇄 상한에 걸리면 안 된다.");
                Assert.Greater(s.Throws.Count, 0);

                // 마지막을 뺀 전부가 윷 또는 모여야 한다.
                for (int k = 0; k < s.Throws.Count - 1; k++)
                    Assert.IsTrue(YutGame.ThrowsAgain(s.Throws[k]),
                        "중간에 윷·모가 아닌 결과가 있으면 거기서 끝났어야 한다.");

                Assert.IsFalse(YutGame.ThrowsAgain(s.Throws[s.Throws.Count - 1]),
                    "마지막 던지기는 윷도 모도 아니어야 한다.");
            }
        }

        [Test]
        public void 배수는_던진_값의_합이다()
        {
            var game = new YutGame(Seed + 3);
            for (int i = 0; i < 2000; i++)
            {
                var s = game.Play();
                int sum = 0;
                foreach (var r in s.Throws) sum += (int)r;
                Assert.AreEqual(sum, s.Multiplier);
            }
        }

        [Test]
        public void 배수는_항상_1_이상이다()
        {
            // ★ 마이너스 없음. 실제 윷놀이의 뒷도(백도)를 넣지 않은 이유다.
            //   방치형에서 벌은 이탈이다. 꽝은 있어도 손해는 없어야 한다.
            var game = new YutGame(Seed + 4);
            for (int i = 0; i < 20000; i++)
                Assert.GreaterOrEqual(game.Play().Multiplier, (int)YutResult.Do,
                    "가장 나쁜 결과인 도조차 배수 1이다. 0이나 음수가 나오면 안 된다.");
        }

        [Test]
        public void 같은_씨앗은_같은_결과를_준다()
        {
            var a = new YutGame(777);
            var b = new YutGame(777);
            for (int i = 0; i < 500; i++)
                Assert.AreEqual(a.Throw(), b.Throw(),
                    "재현 불가능하면 밸런스 회귀를 잡을 수 없다.");
        }

        [Test]
        public void 표시_이름이_다섯_개_모두_다르다()
        {
            var seen = new HashSet<string>();
            foreach (YutResult r in Enum.GetValues(typeof(YutResult)))
                Assert.IsTrue(seen.Add(YutGame.DisplayName(r)),
                    "화면에 한 글자로 띄우므로 겹치면 안 된다.");
            Assert.AreEqual(5, seen.Count);
        }
    }
}

using NUnit.Framework;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// 놀이 횟수 해금 축 검증.
    ///
    /// 이 축이 지키는 계약은 하나다.
    ///
    ///   **해금 축이 늘어도 도달점은 안 움직인다.**
    ///
    /// 해금은 '무엇을 쓸 수 있는가'이고, 부적은 56조합 전수 실측에서
    /// 도달 웨이브를 하나도 못 바꿨다(전부 77). 그래서 이 축은 안전하다.
    /// 안전하지 않게 되는 순간은 누가 이걸 AND로 바꾸거나,
    /// 놀이 횟수에 다른 보상을 얹을 때다. 그때 이 스위트가 먼저 깨져야 한다.
    /// </summary>
    public class YutUnlockTests
    {
        private const string Dokkaebi = TalismanCatalog.Dokkaebi;

        [Test]
        public void 놀이만으로도_열린다()
        {
            // 티어 1에 최고 기록 0 — 티어 문은 닫혀 있다.
            Assert.IsFalse(TalismanCatalog.IsUnlocked(Dokkaebi, tier: 1, bestWave: 0, plays: 29),
                "29판에서 열리면 안 된다");
            Assert.IsTrue(TalismanCatalog.IsUnlocked(Dokkaebi, tier: 1, bestWave: 0, plays: 30),
                "티어가 안 돼도 30판이면 열려야 한다 — 그게 이 축을 넣은 이유다");
        }

        [Test]
        public void 티어만으로도_열린다()
        {
            // 윷을 한 판도 안 논 사람이 손해를 보면 안 된다.
            Assert.IsTrue(TalismanCatalog.IsUnlocked(Dokkaebi, tier: 5, bestWave: 0, plays: 0),
                "미니게임을 안 해도 원래 길로 열려야 한다");
        }

        [Test]
        public void 계약_두_문은_OR다()
        {
            // ★ AND로 바꾸면 여기서 죽는다.
            //   AND는 축이 늘수록 문을 좁히고, 그 순간 축은 선택지가 아니라 숙제가 된다.
            Assert.IsFalse(TalismanCatalog.IsUnlocked(Dokkaebi, 1, 0, 0));
            Assert.IsTrue(TalismanCatalog.IsUnlocked(Dokkaebi, 5, 0, 0));
            Assert.IsTrue(TalismanCatalog.IsUnlocked(Dokkaebi, 1, 0, 30));
            Assert.IsTrue(TalismanCatalog.IsUnlocked(Dokkaebi, 5, 0, 30));
        }

        [Test]
        public void 계약_놀이축은_도깨비_하나뿐이다()
        {
            // ★ 놀이 횟수가 여러 부적에 붙기 시작하면 미니게임이 해금 통로가 된다.
            //   그러면 "재미있어서 논다"가 "열려고 논다"로 바뀌고,
            //   보상이 끊긴 뒤에도 던지는 비율이라는 유일한 지표가 오염된다.
            int withPlays = 0;
            foreach (var u in TalismanCatalog.Unlocks)
            {
                if (u.Plays <= 0) continue;
                withPlays++;
                Assert.AreEqual(Dokkaebi, u.Id,
                    "놀이 축은 도깨비에만 붙인다. 윷을 놀아주는 상대가 도깨비이기 때문이다.");
            }
            Assert.AreEqual(1, withPlays);
        }

        [Test]
        public void 잠긴_동안_두_문이_다_보인다()
        {
            // 하나만 보여주면 그 길이 막혔을 때 유저가 포기한다.
            string hint = TalismanCatalog.UnlockHint(Dokkaebi, tier: 1, bestWave: 0, plays: 0);
            StringAssert.Contains("티어", hint);
            StringAssert.Contains("놀이", hint);
            StringAssert.Contains("또는", hint);
        }

        [Test]
        public void 열린_뒤에는_안내가_사라진다()
        {
            Assert.IsEmpty(TalismanCatalog.UnlockHint(Dokkaebi, 1, 0, 30));
            Assert.IsEmpty(TalismanCatalog.UnlockHint(Dokkaebi, 5, 0, 0));
        }

        [Test]
        public void 놀이축은_다른_부적을_건드리지_않는다()
        {
            // 놀이 횟수를 아무리 올려도 티어·웨이브 조건 부적은 그대로여야 한다.
            foreach (var u in TalismanCatalog.Unlocks)
            {
                if (u.Id == Dokkaebi) continue;
                Assert.AreEqual(
                    TalismanCatalog.IsUnlocked(u.Id, 1, 0, 0),
                    TalismanCatalog.IsUnlocked(u.Id, 1, 0, 9999),
                    $"{u.Id}의 해금이 놀이 횟수에 반응했다");
            }
        }

        [Test]
        public void 기본값은_놀이_0판이다()
        {
            // 인자를 안 넘기는 옛 호출부가 조용히 부적을 열어주면 안 된다.
            Assert.IsFalse(TalismanCatalog.IsUnlocked(Dokkaebi, 1, 0),
                "plays 기본값이 0이 아니면 기존 호출부가 몰래 문을 연다");
        }
    }
}

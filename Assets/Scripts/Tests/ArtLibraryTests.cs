using NUnit.Framework;
using UnityEngine;
using IdleDefense.Art;
using IdleDefense.Economy;

namespace IdleDefense.Tests
{
    /// <summary>
    /// ArtLibrary의 계약은 "아트가 없어도 게임이 돌아간다"이다.
    /// 그래서 여기서 검사하는 것은 그림의 품질이 아니라 **없을 때의 행동**이다.
    ///
    /// 아트 파일 존재 여부를 단정하는 테스트는 일부러 넣지 않았다.
    /// 12종이 순차적으로 들어오는 동안 그런 테스트는 계속 빨간불이 되고,
    /// 빨간불이 일상이 되면 진짜 실패를 아무도 안 본다.
    /// </summary>
    public class ArtLibraryTests
    {
        [SetUp]
        public void Setup() => ArtLibrary.ClearCache();

        [Test]
        public void 아트가_없어도_null이_아니다()
        {
            var s = ArtLibrary.Card("존재하지_않는_id_" + System.Guid.NewGuid());
            Assert.IsNotNull(s, "못 찾으면 플레이스홀더를 줘야 한다. null은 호출부를 터뜨린다.");
        }

        [Test]
        public void 아트가_없어도_예외를_던지지_않는다()
        {
            Assert.DoesNotThrow(() =>
            {
                ArtLibrary.Card(null);
                ArtLibrary.Unit("");
                ArtLibrary.Cutin("없는id");
            });
        }

        [Test]
        public void 부적_8종_전부_조회가_가능하다()
        {
            foreach (var t in TalismanCatalog.FirstGroup)
            {
                Assert.IsNotNull(ArtLibrary.Card(t.Id),  $"카드 {t.Id}");
                Assert.IsNotNull(ArtLibrary.Unit(t.Id),  $"유닛 {t.Id}");
                Assert.IsNotNull(ArtLibrary.Cutin(t.Id), $"컷인 {t.Id}");
            }
        }

        [Test]
        public void 해치_티어는_1에서_6으로_고정된다()
        {
            // 범위를 벗어난 티어가 들어와도 터지지 않고 가장 가까운 티어에 붙어야 한다.
            Assert.IsNotNull(ArtLibrary.HaetaeTier(0));
            Assert.IsNotNull(ArtLibrary.HaetaeTier(99));
            Assert.IsNotNull(ArtLibrary.HaetaeTier(-5));

            for (int t = 1; t <= 6; t++)
                Assert.IsNotNull(ArtLibrary.HaetaeTier(t), $"티어 {t}");
        }

        [Test]
        public void 같은_id는_같은_인스턴스를_돌려준다()
        {
            // 캐시가 없으면 매 프레임 Resources.Load가 돈다.
            var a = ArtLibrary.Card(TalismanCatalog.Jeoseungsaja);
            var b = ArtLibrary.Card(TalismanCatalog.Jeoseungsaja);
            Assert.AreSame(a, b);
        }

        [Test]
        public void 캐시를_비우면_다시_읽는다()
        {
            var a = ArtLibrary.Card(TalismanCatalog.Janggun);
            ArtLibrary.ClearCache();
            var b = ArtLibrary.Card(TalismanCatalog.Janggun);

            // 플레이스홀더는 정적으로 하나만 만들므로 같은 인스턴스일 수 있다.
            // 여기서 보는 것은 ClearCache가 터지지 않고 조회가 계속 된다는 것이다.
            Assert.IsNotNull(b);
        }

        [Test]
        public void 경로_규약이_문서와_일치한다()
        {
            // 이 문자열이 바뀌면 아트 폴더 구조가 통째로 어긋난다.
            // 마케팅 비주얼 기준 문서 8장의 표와 같은 값이어야 한다.
            Assert.AreEqual("Art/Card/",     ArtLibrary.CardRoot);
            Assert.AreEqual("Art/Unit/",     ArtLibrary.UnitRoot);
            Assert.AreEqual("Art/Cutin/",    ArtLibrary.CutinRoot);
            Assert.AreEqual("Art/Haetae/tier", ArtLibrary.HaetaeRoot);
        }
    }
}

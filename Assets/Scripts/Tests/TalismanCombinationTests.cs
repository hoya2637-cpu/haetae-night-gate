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
    /// 부적 1군 8종 · C(8,5) = 56 조합 전수 검증.
    ///
    /// 이 스위트가 지키려는 계약은 하나다.
    ///   "부적은 속도를 바꾸되 도달점은 바꾸지 않는다"
    ///
    /// 이건 취향이 아니라 경제 설계의 전제다. 조작 실력이 도달 웨이브를 바꾸면
    /// 90일 커브가 유저마다 갈라지고, 코어 획득량·티어 통과 시점·수익 모델이
    /// 전부 개인차에 종속된다. 그 순간 스프레드시트는 아무것도 예측하지 못한다.
    ///
    /// 그래서 56조합 전부가 '같은 웨이브에 도달하되 걸린 시간만 달라야' 한다.
    /// 그 시간 차이가 조작의 보상이고, 그게 전부여야 한다.
    ///
    /// ★ 밴드를 임의로 두지 않는다.
    ///   시간 격차의 상/하한은 이 파일이 정하지 않는다. 먼저 재고, 분포를 보고,
    ///   그 다음에 회귀 감시선을 정한다. 지금 여기 있는 단언은
    ///   측정 없이도 참이어야 하는 구조적 계약뿐이다.
    /// </summary>
    public class TalismanCombinationTests
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

        private const double Dt = 0.1;          // 도달 웨이브는 dt와 무관 (P0 계측 1차)
        private const double TimeDt = 0.02;     // 런 시간을 잴 때만 촘촘히

        /// <summary>소환 정책. 같은 조합도 어떻게 쓰느냐에 따라 결과가 달라야 한다.</summary>
        private enum Policy
        {
            /// <summary>아무것도 안 쓴다. 모든 비교의 기준선.</summary>
            None,
            /// <summary>준비되는 대로 슬롯 순서대로 쓴다. 조작 하한선.</summary>
            Greedy,
            /// <summary>원시 부적을 먼저 깔고, 메타는 깔린 게 있을 때만 쓴다. 조작 상한선.</summary>
            Informed,
        }

        private struct RunResult
        {
            public int DeepestWave;
            public double Seconds;
            public int Summons;
        }

        /// <summary>
        /// 한 런을 벽까지 돌린다. GameController와 같은 배선을 쓴다.
        /// (부적 Tick → TalismanMultiplier → ExecuteFraction → Battle.Tick)
        /// </summary>
        private RunResult RunOnce(int[] combo, Policy policy, double dt, double atkMul = 1.0)
        {
            var runner = new BattleRunner(cfg);
            var tracks = new UpgradeTracks(cfg);
            var tal = new TalismanSystem(cfg);

            if (combo != null)
                foreach (int idx in combo)
                    tal.Equip(TalismanCatalog.FirstGroup[idx]);

            runner.BeginRun(1, BigNumber.Zero);
            runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;
            runner.CoinMultiplier = tracks.CoinMultiplier;

            int summons = 0;
            int guard = 0;

            while (runner.IsRunning && !runner.IsWalled && guard++ < 4_000_000)
            {
                // 업그레이드는 코인이 되는 대로 산다. 조합과 무관하게 동일한 정책이다.
                while (tracks.BuyBest(runner.Coin, out var cost))
                {
                    runner.SpendCoin(cost);
                    runner.AttackMultiplier = atkMul * tracks.CombatMultiplier;
                    runner.CoinMultiplier = tracks.CoinMultiplier;
                }

                if (policy != Policy.None) Summon(tal, policy, ref summons);

                tal.Tick(dt, dt);
                runner.TalismanMultiplier = tal.CurrentDamageMultiplier;

                double execute = tal.ConsumeExecuteFraction();
                if (execute > 0.0) runner.ExecuteFraction(execute);

                runner.Tick(dt, tracks.TotalLevel);
            }

            return new RunResult
            {
                DeepestWave = runner.DeepestWave,
                Seconds = runner.RunElapsed,
                Summons = summons,
            };
        }

        private static void Summon(TalismanSystem tal, Policy policy, ref int summons)
        {
            var eq = tal.Equipped;

            if (policy == Policy.Greedy)
            {
                for (int i = 0; i < eq.Count; i++)
                    if (eq[i].IsReady && tal.Summon(i, TalismanSystem.Lane.Middle)) summons++;
                return;
            }

            // Informed — 원시/즉발을 먼저 깔고, 메타는 깔린 효과가 있을 때만 쓴다.
            // 배치도 성격에 맞춘다: 지속형은 뒤(오래), 즉발은 앞(지금).
            for (int i = 0; i < eq.Count; i++)
            {
                if (!eq[i].IsReady) continue;
                var kind = eq[i].Effect;
                if (kind == TalismanEffect.Damage)
                {
                    if (tal.Summon(i, TalismanSystem.Lane.Back)) summons++;
                }
                else if (kind == TalismanEffect.Execute)
                {
                    if (tal.Summon(i, TalismanSystem.Lane.Front)) summons++;
                }
            }

            // 메타는 조건 없이 전부 소환한다.
            //
            // ★ 2026-08-20 개정 — 세 번째 정책 결함이었다.
            //   이전 정책은 두 개의 조건을 걸고 있었다:
            //     · 처용은 '다른 부적이 쿨 중일 때'만
            //     · 증폭·복제·연장은 ActiveCount > 0 일 때만
            //
            //   메타에 자체 효과(Self*)가 생긴 순간 두 조건 다 틀린 것이 됐다.
            //   이제 모든 메타는 언제 눌러도 무언가를 한다. 조건을 기다리면 그냥 손해다.
            //   실제로 이 조건을 남겨둔 채 계측했더니
            //   처용·까치호랑이·바리데기·불가사리의 단독 위력이 전부 0.0%로 나왔다 —
            //   조건이 영영 성립하지 않아 한 번도 소환되지 않았기 때문이다.
            //
            //   1군 5.1장에서 처용을 못 쓰게 만들었던 것과 완전히 같은 실수를,
            //   하이브리드 전환에서 반복했다.
            //
            // ★ 원칙: 효과를 바꾸면 소환 정책도 같이 바꿔야 한다.
            //   새 축을 넣을 때는 단독 위력이 0이 아닌지부터 확인하라. 0이면 대개 정책이 범인이다.
            for (int i = 0; i < eq.Count; i++)
            {
                if (!eq[i].IsReady) continue;
                var kind = eq[i].Effect;
                if (kind == TalismanEffect.Damage || kind == TalismanEffect.Execute) continue;
                if (tal.Summon(i, TalismanSystem.Lane.Back)) summons++;
            }
        }

        // ─────────────────────────────────────────
        // 1. 구조적 계약 — 측정 없이도 참이어야 하는 것

        [Test]
        public void 조합_전수가_56가지다()
        {
            var all = TalismanCatalog.AllCombinations(TalismanSystem.MaxSlots);
            Assert.AreEqual(56, all.Count,
                "1군이 8종이 아니거나 슬롯이 5개가 아닙니다. " +
                "전수 검증이 가능한 규모를 벗어나면 이 스위트 전체가 무의미해집니다.");
            Assert.AreEqual(8, TalismanCatalog.FirstGroup.Count);
        }

        [Test]
        public void 어떤_조합도_메타_부적만으로_채워지지_않는다()
        {
            // 메타만 5개인 조합이 가능하면 "아무 일도 안 일어나는 조합"이 존재하게 되고,
            // 유저는 그게 자기 잘못인지 설계 잘못인지 알 수 없다.
            // 1군을 원시 4 + 메타 4로 짠 이유가 이것이며, 그래서 구조적으로 불가능하다.
            //
            // ★★ 2군이 들어오면 이 테스트는 반드시 깨진다. 설계상 그렇다.
            //
            //   17종이 되면 메타가 7종(암행어사·전우치·처용·무당·산신·까치호랑이·불가사리)이라
            //   C(7,5) = 21개의 '메타만 5개' 조합이 실제로 생긴다.
            //   보증하려면 메타가 4종 이하여야 하는데 20종 규모에서는 비현실적이다.
            //
            //   그래서 보증 방식을 바꿨다. 이 테스트가 막으려던 것은
            //   "조합의 개수"가 아니라 "아무 일도 안 일어나는 것"이었고,
            //   그건 이제 메타의 자체 효과(Self*)가 막는다.
            //
            //   ▶ 2군 도입 시 이 테스트를 다음으로 교체할 것:
            //       메타만_5개인_조합도_바닥값을_넘는다
            //       — 21개 조합 전부 단축률 15% 이상. 실측 최악 18.0%.
            //
            //   근거: docs/부적2군_설계와_계측.md 2장, docs/2군_검증전략.md 4.2장
            //   ★ 이 테스트가 2군 브랜치에서 빨간불이 되면 '고장'이 아니라 '예정된 교체'다.
            //      수치를 되돌리지 말고 위 교체안을 쓸 것.
            foreach (var combo in TalismanCatalog.AllCombinations(TalismanSystem.MaxSlots))
            {
                int primary = 0;
                foreach (int i in combo)
                {
                    var e = TalismanCatalog.FirstGroup[i].Effect;
                    if (e == TalismanEffect.Damage || e == TalismanEffect.Execute) primary++;
                }
                Assert.GreaterOrEqual(primary, 1,
                    $"조합 [{TalismanCatalog.NameOf(combo)}]이 메타 부적만으로 구성됩니다. " +
                    "이 조합을 고른 유저는 부적을 5개 끼고도 아무 효과를 못 봅니다.");
            }
        }

        [Test]
        public void 즉시삭제는_웨이브_총체력을_바꾸지_않는다()
        {
            // 이 스위트에서 가장 중요한 단언이다.
            //
            // 벽 판정식은 WaveHpTotal / BaseDpsWithoutTalisman > waveTimeWall 이다.
            // 저승사자가 WaveHpTotal을 깎으면 그 즉시 부적이 벽을 밀어내게 되고,
            // "속도만 바꾼다"는 원칙이 무너진다. 잔여 체력만 깎아야 한다.
            var runner = new BattleRunner(cfg);
            runner.BeginRun(50, BigNumber.Zero);

            var totalBefore = runner.WaveHpTotal;
            var remainBefore = runner.WaveHpRemaining;

            runner.ExecuteFraction(0.5);

            Assert.AreEqual(totalBefore.Log10(), runner.WaveHpTotal.Log10(), 1e-9,
                "저승사자가 웨이브 총 체력을 깎았습니다. 벽 판정식이 바뀌어 " +
                "부적이 도달 웨이브를 옮기게 됩니다. 90일 커브가 유저마다 갈라집니다.");

            Assert.Less(runner.WaveHpRemaining.Log10(), remainBefore.Log10(),
                "즉시 삭제가 잔여 체력에 아무 영향을 주지 못했습니다.");
        }

        [Test]
        public void 아무리_강한_부적도_벽을_한_웨이브도_못_넘는다()
        {
            // 회귀 감시 — 벽 판정이 피해 적용보다 뒤에 있으면 이 테스트가 깨진다.
            //
            // 원래 코드는 이랬다:
            //     HP -= dps x dt
            //     if (HP <= 0) { ClearWave(); return; }   ← 여기서 빠져나가면
            //     if (벽) { ... }                          ← 벽 판정이 아예 실행되지 않는다
            //
            // 즉 부적 피해가 한 틱에 웨이브를 끝낼 만큼 크면 벽을 그냥 통과했다.
            // 실제 1군 수치로는 도달 못 할 세기였지만, 보증이 '수치가 작아서' 성립하는 것과
            // '구조적으로' 성립하는 것은 완전히 다른 이야기다.
            // dt가 커지거나(고배속) 부적이 세지면 조용히 무너진다.
            var runner = new BattleRunner(cfg);
            runner.BeginRun(30, BigNumber.Zero);   // 레벨 0으로는 절대 못 깨는 웨이브

            int startWave = runner.CurrentWave;
            runner.TalismanMultiplier = 1e12;      // 현실에 없는 세기
            runner.Tick(0.1, 0);

            Assert.IsTrue(runner.IsWalled,
                "부적 배수가 크다는 이유로 벽 판정이 건너뛰어졌습니다.");
            Assert.AreEqual(startWave, runner.CurrentWave,
                "부적이 벽 너머의 웨이브로 넘어갔습니다. " +
                "도달점이 조작에 종속되면 90일 커브가 유저마다 갈라집니다.");
        }

        [Test]
        public void 메타_부적은_혼자서도_작동하되_원시보다_약하다()
        {
            // ★ 2026-08-20 계약 교체.
            //
            //   옛 계약은 "메타는 혼자서는 아무것도 못 한다"였고, 이 테스트가 그걸 지켰다.
            //   그런데 그 계약이 바로 바닥값 붕괴의 원인이었다 —
            //   부적 20종에서는 '메타만 5개'인 조합이 가능해지고,
            //   그 조합의 단축률이 0.0%로 측정됐다. 부적을 골랐는데 아무 일도 안 일어난다.
            //   (1군 8종에서는 메타가 4종뿐이라 이 상황이 구조적으로 불가능했다)
            //
            //   그래서 메타마다 고유한 자체 효과(Self*)를 주었다. 계약이 둘로 나뉜다:
            //     ① 메타는 혼자서도 무언가는 한다        — 바닥을 세운다
            //     ② 그러나 가장 약한 원시보다도 약하다   — 이름값 정합성을 지킨다
            //
            //   ②가 진짜 제약이다. 실측에서 자체 효과 지속을 쿨 대비 0.45로 올리면
            //   바닥은 14.3%까지 오르지만 메타 최고 단독 위력(11.2%)이
            //   포졸(10.2%)을 넘어선다. 그래서 0.30이 상한으로 확정됐다.
            //
            //   근거: docs/부적2군_설계와_계측.md 4장
            double pojol = TalismanCatalog.Get(TalismanCatalog.Pojol).Magnitude;   // 1.25
            double jeoseungsaja =
                TalismanCatalog.Get(TalismanCatalog.Jeoseungsaja).Magnitude;       // 0.65

            foreach (var id in new[]
            {
                TalismanCatalog.Amhaengeosa, TalismanCatalog.Jeonuchi,
                TalismanCatalog.Cheoyong, TalismanCatalog.Mudang,
            })
            {
                var t = TalismanCatalog.Get(id);
                var tal = new TalismanSystem(cfg);
                tal.Equip(t);
                tal.Summon(0, TalismanSystem.Lane.Front);
                tal.Tick(0.1, 0.1);

                double mult = tal.CurrentDamageMultiplier;
                double exec = tal.ConsumeExecuteFraction();   // 한 번만 부른다 — 소모형이다

                // ① 바닥 — 혼자서도 무언가는 해야 한다
                Assert.IsTrue(mult > 1.0 || exec > 0.0,
                    $"{t.DisplayName}이 혼자서 아무 일도 하지 않습니다. " +
                    "메타만 끼운 조합의 단축률이 0%가 되고, 부적을 고른 유저가 아무것도 못 느낍니다.");

                // ② 천장 — 가장 약한 원시(포졸)를 넘으면 안 된다
                Assert.Less(mult, pojol,
                    $"{t.DisplayName}의 단독 배수 {mult:F2}가 포졸({pojol:F2})을 넘었습니다. " +
                    "메타가 전용 피해 부적보다 세면 화면의 숫자를 믿을 수 없게 됩니다.");

                Assert.Less(exec, jeoseungsaja,
                    $"{t.DisplayName}의 단독 즉시삭제 {exec:F2}가 저승사자({jeoseungsaja:F2})를 넘었습니다.");
            }
        }

        [Test]
        public void 같은_부적을_두_번_장착할_수_없다()
        {
            // 부적 배수는 곱연산이다. 장군(1.90)을 5개 끼우면 1.90^5 = 24.8배가 되고
            // "부적은 속도만 바꾼다"는 보증이 세이브 파일 한 줄로 무너진다.
            // 정규화는 GameController가 하지만, 엔진 자체도 거부해야 한다(이중 방어).
            var tal = new TalismanSystem(cfg);
            var janggun = TalismanCatalog.Get(TalismanCatalog.Janggun);

            Assert.IsTrue(tal.Equip(janggun), "첫 장착이 거부됐습니다.");
            for (int i = 0; i < 4; i++)
                Assert.IsFalse(tal.Equip(janggun),
                    "같은 부적이 중복 장착됐습니다. 배수가 거듭제곱으로 폭발합니다.");

            Assert.AreEqual(1, tal.Equipped.Count);
        }

        // ─────────────────────────────────────────
        // 쿨타임 소유권 — 익스플로잇 회귀 감시
        //
        // 발견된 결함: ApplyLoadout이 UnequilAll 후 카탈로그 원본을 다시 복제해서
        //   장착을 하나만 토글해도 전체 쿨타임이 0으로 리셋됐다.
        //   장군(쿨 70초) 소환 → 아무 부적 토글 → 즉시 재소환 → 무한.
        //
        // 도달 웨이브는 안 뚫린다(벽은 baseDps 판정). 하지만 부적이 통제하는 유일한 축인
        // '런 시간'이 통째로 무의미해지고, 쿨감이 전부인 처용은 존재 이유가 사라진다.
        //
        // ★ 아래 테스트들은 실제 ApplyLoadout 경로를 탄다. 쿨타임 필드만 따로 찔러보는
        //   테스트였다면 이 익스플로잇을 못 잡았을 것이다.

        /// <summary>부적을 소환해 쿨타임을 걸고, 남은 쿨타임을 돌려준다.</summary>
        private static double SummonAndGetCooldown(TalismanSystem tal, string id)
        {
            for (int i = 0; i < tal.Equipped.Count; i++)
            {
                if (tal.Equipped[i].Id != id) continue;
                Assert.IsTrue(tal.Summon(i, TalismanSystem.Lane.Front),
                    $"{id} 소환에 실패했습니다. 테스트 전제가 깨졌습니다.");
                return tal.Equipped[i].CooldownRemaining;
            }
            Assert.Fail($"{id}이 장착되어 있지 않습니다.");
            return 0;
        }

        [Test]
        public void 장착_해제_재장착으로_쿨타임이_초기화되지_않는다()
        {
            var tal = new TalismanSystem(cfg);
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });

            double after = SummonAndGetCooldown(tal, TalismanCatalog.Janggun);
            Assert.Greater(after, 0, "소환했는데 쿨타임이 안 걸렸습니다.");

            // 장군을 뺐다가 그대로 다시 끼운다.
            tal.ApplyLoadout(new[] { TalismanCatalog.Pojol });
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });

            Assert.AreEqual(after, tal.CooldownOf(TalismanCatalog.Janggun), 1e-9,
                "해제 후 재장착으로 쿨타임이 초기화됐습니다. " +
                "장착 토글이 쿨타임 리셋 버튼이 되어 부적을 무한히 쓸 수 있습니다.");
        }

        [Test]
        public void 다른_슬롯을_교체해도_쿨타임이_유지된다()
        {
            // 실제 익스플로잇 경로 — 장군은 그대로 두고 '다른' 부적만 갈아끼운다.
            var tal = new TalismanSystem(cfg);
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });

            double after = SummonAndGetCooldown(tal, TalismanCatalog.Janggun);

            for (int n = 0; n < 5; n++)
            {
                tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Mudang });
                tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });
            }

            Assert.AreEqual(after, tal.CooldownOf(TalismanCatalog.Janggun), 1e-9,
                "옆 슬롯을 교체하는 것만으로 장군의 쿨타임이 리셋됐습니다.");

            int slot = 0;
            for (int i = 0; i < tal.Equipped.Count; i++)
                if (tal.Equipped[i].Id == TalismanCatalog.Janggun) slot = i;

            Assert.IsFalse(tal.Summon(slot, TalismanSystem.Lane.Front),
                "쿨타임이 남았는데 재소환에 성공했습니다. 무한 소환이 가능합니다.");
        }

        [Test]
        public void 쿨타임_진행_중_재장착해도_남은_시간이_이어진다()
        {
            var tal = new TalismanSystem(cfg);
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });

            double full = SummonAndGetCooldown(tal, TalismanCatalog.Janggun);
            for (int i = 0; i < 100; i++) tal.Tick(0.1, 0.1);   // 10초 경과

            double expected = full - 10.0;
            tal.ApplyLoadout(new[] { TalismanCatalog.Pojol });
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });

            Assert.AreEqual(expected, tal.CooldownOf(TalismanCatalog.Janggun), 1e-6,
                "쿨타임이 흐르던 중 재장착하자 남은 시간이 어긋났습니다.");
        }

        [Test]
        public void 빼놓은_부적의_쿨타임도_흐른다()
        {
            // 멈춰 있게 두면 "빼두면 손해"가 되어 유저가 슬롯을 실험하지 않는다.
            // 조합이 콘텐츠인 게임에서 조합을 바꿔보는 것이 벌칙이 되면 안 된다.
            var tal = new TalismanSystem(cfg);
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });

            double full = SummonAndGetCooldown(tal, TalismanCatalog.Janggun);

            tal.ApplyLoadout(new[] { TalismanCatalog.Pojol });     // 장군을 빼둔다
            for (int i = 0; i < 100; i++) tal.Tick(0.1, 0.1);      // 10초

            Assert.AreEqual(full - 10.0, tal.CooldownOf(TalismanCatalog.Janggun), 1e-6,
                "빼놓은 부적의 쿨타임이 멈춰 있습니다. 조합 실험이 손해가 됩니다.");
        }

        [Test]
        public void 환생은_쿨타임을_전부_초기화한다()
        {
            // ResetAll은 환생 전용이다. 여기서까지 유지하면 새 런이 부담스러워진다.
            var tal = new TalismanSystem(cfg);
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun });
            SummonAndGetCooldown(tal, TalismanCatalog.Janggun);

            tal.ResetAll();

            Assert.AreEqual(0.0, tal.CooldownOf(TalismanCatalog.Janggun), 1e-9,
                "환생 후에도 쿨타임이 남아 있습니다.");
            Assert.IsTrue(tal.Equipped[0].IsReady);
        }

        [Test]
        public void 장착_정규화가_중복과_초과와_무효id를_걸러낸다()
        {
            var tal = new TalismanSystem(cfg);
            var result = tal.ApplyLoadout(new[]
            {
                TalismanCatalog.Janggun, TalismanCatalog.Janggun,   // 중복
                "없는부적",                                          // 무효
                null, "",                                           // 빈 값
                TalismanCatalog.Pojol, TalismanCatalog.Hongildong,
                TalismanCatalog.Cheoyong, TalismanCatalog.Mudang,
                TalismanCatalog.Amhaengeosa, TalismanCatalog.Jeonuchi,  // 슬롯 초과
            });

            Assert.AreEqual(TalismanSystem.MaxSlots, result.Length,
                "슬롯 상한을 넘겼습니다.");
            CollectionAssert.AllItemsAreUnique(result,
                "중복이 통과했습니다. 곱연산이라 같은 부적 5개면 배수가 5제곱이 됩니다.");
            Assert.AreEqual(result.Length, tal.Equipped.Count,
                "정규화 결과와 실제 장착 수가 다릅니다.");

            var sorted = (string[])result.Clone();
            Array.Sort(sorted, StringComparer.Ordinal);
            CollectionAssert.AreEqual(sorted, result,
                "정렬되지 않았습니다. 조합 키가 순서마다 달라져 집계가 불가능해집니다.");
        }

        [Test]
        public void 장착은_슬롯_수를_넘지_못한다()
        {
            var tal = new TalismanSystem(cfg);
            int accepted = 0;
            foreach (var t in TalismanCatalog.FirstGroup)
                if (tal.Equip(t)) accepted++;

            Assert.AreEqual(TalismanSystem.MaxSlots, accepted,
                "8종을 전부 장착할 수 있습니다. 슬롯 상한이 조합 콘텐츠의 전제입니다.");
            Assert.AreEqual(TalismanSystem.MaxSlots, tal.Equipped.Count);
        }

        // ─────────────────────────────────────────
        // 자동 소환 — 유료 기능(구슬 1,500)의 계약

        [Test]
        public void 자동_소환은_효과를_겹쳐_쓸_수_있다()
        {
            // 회귀 감시 — TryAutoSummon의 첫 줄이 이랬다:
            //     if (active.Count > 0) return;
            //
            // "효과가 돌고 있으면 아낀다"는 절약 의도였는데, 실제로는
            // 자동이 두 부적을 절대 겹쳐 쓰지 못하게 만들었다.
            // 겹쳐야만 값이 나오는 메타 4종이 자동에서 통째로 죽었고,
            // 자동화를 구매한 유저는 조합을 바꿔도 결과가 거의 안 변했다.
            // (실측: 최선 조합에서 자동 11.9% vs 중첩 허용 26.1%)
            var tal = new TalismanSystem(cfg) { AutoSummon = true };
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun, TalismanCatalog.Pojol });

            // 중앙 배치는 1초 지연이므로 그 너머까지 돌린다.
            for (int i = 0; i < 20; i++) tal.Tick(0.1, 0.1);

            Assert.GreaterOrEqual(tal.ActiveCount, 2,
                $"자동 소환이 효과를 하나만 유지합니다(활성 {tal.ActiveCount}). " +
                "메타 부적은 겹쳐야 값이 나오므로, 중첩을 막으면 유료 자동화가 " +
                "부적 조합 콘텐츠와 단절됩니다.");
        }

        [Test]
        public void 자동_소환은_수동보다_약하다()
        {
            // 자동이 수동만큼 세면 조작할 이유가 사라진다.
            // 그 불리함은 AutoEfficiency라는 '의도한 레버'가 만들어야 하며,
            // 우연한 정책 결함이 만들어서는 안 된다(위 테스트가 그 경우를 막는다).
            double manual = SummonAndMeasure(isAuto: false);
            double auto = SummonAndMeasure(isAuto: true);

            Assert.Greater(manual, auto,
                $"자동({auto:F3})이 수동({manual:F3})보다 약하지 않습니다. " +
                "조작할 이유가 사라집니다.");

            // 장군 1.90, 효율 0.75 → 1 + 0.9 x 0.75 = 1.675
            Assert.AreEqual(1.675, auto, 1e-6,
                "AutoEfficiency가 배수의 '초과분'에만 적용되지 않았습니다. " +
                "1.90 x 0.75 = 1.425처럼 전체에 곱하면 자동이 과도하게 약해집니다.");
        }

        private double SummonAndMeasure(bool isAuto)
        {
            var tal = new TalismanSystem(cfg) { AutoEfficiency = 0.75 };
            tal.ApplyLoadout(new[] { TalismanCatalog.Janggun });
            tal.Summon(0, TalismanSystem.Lane.Front, isAuto);
            tal.Tick(0.1, 0.1);
            return tal.CurrentDamageMultiplier;
        }

        [Test]
        public void 배치_지연이_실제로_적용된다()
        {
            // LaneDelay가 정의만 되고 Summon에서 안 쓰이던 죽은 코드였다. 회귀 감시.
            var tal = new TalismanSystem(cfg);
            tal.Equip(TalismanCatalog.Get(TalismanCatalog.Janggun));

            tal.Summon(0, TalismanSystem.Lane.Back);          // 지연 2.5초
            tal.Tick(1.0, 1.0);
            Assert.AreEqual(1.0, tal.CurrentDamageMultiplier, 1e-9,
                "뒤에 배치했는데 즉시 발동했습니다. LaneDelay가 적용되지 않습니다.");
            Assert.AreEqual(1, tal.PendingCount, "지연 대기 상태로 잡히지 않았습니다.");

            tal.Tick(2.0, 2.0);
            Assert.Greater(tal.CurrentDamageMultiplier, 1.0,
                "지연이 끝났는데도 효과가 발동하지 않았습니다.");
        }

        [Test]
        public void 부적_쿨타임은_배속의_영향을_받지_않는다()
        {
            // 배속권 하나로 부적을 두 배로 쓰게 되면 광고 배속이 전투력 배수가 된다.
            var real = new TalismanSystem(cfg);
            real.Equip(TalismanCatalog.Get(TalismanCatalog.Pojol));
            real.Summon(0, TalismanSystem.Lane.Front);

            var boosted = new TalismanSystem(cfg);
            boosted.Equip(TalismanCatalog.Get(TalismanCatalog.Pojol));
            boosted.Summon(0, TalismanSystem.Lane.Front);

            for (int i = 0; i < 100; i++)
            {
                real.Tick(0.1, 0.1);        // 1배속
                boosted.Tick(0.1, 0.4);     // 4배속 — 전투시간만 4배
            }

            Assert.AreEqual(real.Equipped[0].CooldownRemaining,
                            boosted.Equipped[0].CooldownRemaining, 1e-9,
                "배속이 쿨타임까지 줄이고 있습니다. 배속권으로 부적을 남발할 수 있습니다.");
        }

        // ─────────────────────────────────────────
        // 2. 56조합 전수 — 핵심 계약

        [Test]
        public void 어떤_조합도_도달_웨이브를_바꾸지_못한다()
        {
            var baseline = RunOnce(null, Policy.None, Dt);
            var combos = TalismanCatalog.AllCombinations(TalismanSystem.MaxSlots);

            var offenders = new List<string>();

            foreach (var combo in combos)
            {
                foreach (var policy in new[] { Policy.Greedy, Policy.Informed })
                {
                    var r = RunOnce(combo, policy, Dt);
                    if (r.DeepestWave != baseline.DeepestWave)
                        offenders.Add(
                            $"[{TalismanCatalog.NameOf(combo)}] {policy} → " +
                            $"{r.DeepestWave} (기준 {baseline.DeepestWave})");
                }
            }

            Assert.IsEmpty(offenders,
                "부적이 도달 웨이브를 바꿨습니다. 조작 실력이 도달점을 옮기면 " +
                "90일 커브가 유저마다 갈라져 경제 설계가 성립하지 않습니다.\n" +
                string.Join("\n", offenders));
        }

        [Test]
        public void 어떤_조합도_기준선보다_느리지_않다()
        {
            // 부적을 끼고 썼는데 더 느려지는 조합이 있으면 그건 함정이다.
            // 유저는 자기가 잘못 쓴 건지 조합이 나쁜 건지 알 수 없다.
            var baseline = RunOnce(null, Policy.None, TimeDt);
            var offenders = new List<string>();

            foreach (var combo in TalismanCatalog.AllCombinations(TalismanSystem.MaxSlots))
            {
                var r = RunOnce(combo, Policy.Informed, TimeDt);
                if (r.Seconds > baseline.Seconds + 1e-6)
                    offenders.Add($"[{TalismanCatalog.NameOf(combo)}] " +
                                  $"{r.Seconds / 60.0:F2}분 (기준 {baseline.Seconds / 60.0:F2}분)");
            }

            Assert.IsEmpty(offenders,
                "부적을 제대로 썼는데 아무것도 안 쓴 것보다 느린 조합이 있습니다.\n" +
                string.Join("\n", offenders));
        }

        [Test]
        public void 조작_숙련도가_런_시간에_반영된다()
        {
            // Greedy(준비되는 대로 난사) vs Informed(원시 먼저, 메타는 깔린 뒤).
            // 둘이 같으면 '조작'이 존재하지 않는 것이고, 부적은 자동화해도 무방한 장식이 된다.
            int better = 0, total = 0;

            foreach (var combo in TalismanCatalog.AllCombinations(TalismanSystem.MaxSlots))
            {
                var greedy = RunOnce(combo, Policy.Greedy, TimeDt);
                var informed = RunOnce(combo, Policy.Informed, TimeDt);
                total++;
                if (informed.Seconds < greedy.Seconds - 1e-6) better++;
            }

            Assert.Greater(better, 0,
                $"56조합 전부에서 소환 순서가 결과를 바꾸지 않았습니다({better}/{total}). " +
                "조작이 아무 의미도 없다면 부적은 콘텐츠가 아니라 장식입니다.");
        }

        // ─────────────────────────────────────────
        // 2.5 교차 검증 — 파이썬 복제 모델의 예측을 그대로 박아둔다

        [Test]
        public void 복제모델_예측과_일치한다()
        {
            // 부적 수치는 파이썬으로 복제한 BattleRunner 모델 위에서 정해졌다.
            // 그 모델이 진짜 이 코드와 같은지를 여기서 못 박는다.
            //
            // ★ 이 테스트가 실패하면 밸런스를 건드리지 마라.
            //   실패는 "수치가 틀렸다"가 아니라 "복제 모델의 가정이 다르다"는 뜻이다.
            //   실제로 한 번 있었다: 복제 모델이 BuyBest 후보에 백(白) 트랙을 넣어
            //   도달 웨이브가 77이 아니라 73으로 나왔다. 코드가 아니라 모델이 틀렸었다.
            //   수치를 모델에 맞춰 조정했다면 실기와 어긋난 밸런스가 확정됐을 것이다.
            //
            // 기준선 77 웨이브 / 14.21분은 docs/P0_계측결과_1차.md 1장의 독립 실측이며,
            // 복제 모델이 이 값을 재현하는 것을 확인한 뒤에야 조합 수치를 신뢰했다.
            var failures = new List<string>();

            var baseline = RunOnce(null, Policy.None, TimeDt);
            Check(failures, "기준선 도달 웨이브", baseline.DeepestWave, 77, 0);
            Check(failures, "기준선 런 시간(분)", baseline.Seconds / 60.0, 14.21, 0.05);

            string bestName = null, worstName = null;
            double bestSec = double.MaxValue, worstSec = 0.0;

            foreach (var combo in TalismanCatalog.AllCombinations(TalismanSystem.MaxSlots))
            {
                var r = RunOnce(combo, Policy.Informed, TimeDt);
                if (r.Seconds < bestSec) { bestSec = r.Seconds; bestName = TalismanCatalog.NameOf(combo); }
                if (r.Seconds > worstSec) { worstSec = r.Seconds; worstName = TalismanCatalog.NameOf(combo); }
            }

            // ★ 2026-08-20 갱신 — 메타 4종에 자체 효과(Self*)가 붙으면서 값이 바뀌었다.
            //   이전: 최선 7.78 / 최악 11.77   →   현재: 최선 6.79 / 최악 10.54
            //   메타가 기댈 대상 없이도 일을 하게 되어 양쪽이 모두 빨라졌다.
            //   근거: docs/부적2군_설계와_계측.md 4장
            Check(failures, "최선 조합 시간(분)", bestSec / 60.0, 6.79, 0.10);
            Check(failures, "최악 조합 시간(분)", worstSec / 60.0, 10.54, 0.10);

            // 최선 조합의 '이름'은 단언하지 않는다.
            // 상위 2개가 6.792분으로 동률이라 모델 오차가 없어도 순위가 흔들린다.
            // 그런 실패는 신호가 아니라 잡음이다. 대신 구조적 사실만 단언한다.
            if (bestName == null || !bestName.Contains("장군"))
                failures.Add($"최선 조합에 장군이 없습니다: [{bestName}] " +
                             "— 장군은 기여도 1위라 최선 조합에서 빠질 수 없다");

            // ★ 최악의 '이름' 단언도 걷어냈다.
            //   이전에는 1위와 2위가 0.45분 벌어져 있어 이름을 못 박아도 안전했지만,
            //   자체 효과가 붙으면서 10.538 vs 10.400 — 0.14분으로 좁아졌다.
            //   이제는 모델 오차 1.5%면 뒤집힌다.
            //   대신 흔들리지 않는 구조적 사실을 단언한다: 최악은 메타가 4개다.
            int metaCount = 0;
            foreach (var id in new[] { TalismanCatalog.Amhaengeosa, TalismanCatalog.Jeonuchi,
                                       TalismanCatalog.Cheoyong,    TalismanCatalog.Mudang })
                if (worstName != null && worstName.Contains(TalismanCatalog.Get(id).DisplayName))
                    metaCount++;
            if (metaCount < 4)
                failures.Add($"최악 조합의 메타가 {metaCount}개입니다: [{worstName}] " +
                             "— 최악은 메타 4개 + 원시 1개여야 한다");

            Assert.IsEmpty(failures,
                "파이썬 복제 모델과 실제 BattleRunner가 어긋났습니다.\n" +
                "부적 수치를 조정하지 마십시오. 두 모델의 가정 차이를 먼저 찾아야 합니다.\n" +
                "(docs/부적1군_설계와_계측.md의 모든 수치가 이 모델 위에서 정해졌습니다)\n" +
                string.Join("\n", failures));
        }

        private static void Check(List<string> failures, string label,
                                  double actual, double expected, double tolerance)
        {
            if (Math.Abs(actual - expected) > tolerance)
                failures.Add($"{label}: 예측 {expected} 실측 {actual:F2} (허용 ±{tolerance})");
        }

        // ─────────────────────────────────────────
        // 3. 계측 — 단언하지 않는다. 표를 뽑아 눈으로 본 뒤 감시선을 정한다.

        [Test, Explicit("계측 전용. 밴드를 정하기 전에 분포를 먼저 본다")]
        public void 계측_56조합_시간_분포()
        {
            var baseline = RunOnce(null, Policy.None, TimeDt);
            var sb = new StringBuilder();
            sb.AppendLine($"기준선(부적 없음)  도달 {baseline.DeepestWave}  " +
                          $"{baseline.Seconds / 60.0:F2}분");
            sb.AppendLine();
            sb.AppendLine("조합                                   | 웨이브 | Greedy | Informed | 단축률 | 소환");

            var rows = new List<(double informed, string line)>();

            foreach (var combo in TalismanCatalog.AllCombinations(TalismanSystem.MaxSlots))
            {
                var g = RunOnce(combo, Policy.Greedy, TimeDt);
                var f = RunOnce(combo, Policy.Informed, TimeDt);
                double cut = 1.0 - f.Seconds / baseline.Seconds;

                rows.Add((f.Seconds, string.Format(
                    "{0,-38} | {1,6} | {2,6:F2} | {3,8:F2} | {4,5:P1} | {5,4}",
                    TalismanCatalog.NameOf(combo), f.DeepestWave,
                    g.Seconds / 60.0, f.Seconds / 60.0, cut, f.Summons)));
            }

            rows.Sort((a, b) => a.informed.CompareTo(b.informed));
            foreach (var r in rows) sb.AppendLine(r.line);

            Debug.Log(sb.ToString());
        }
    }
}

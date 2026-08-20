using System;
using System.Collections.Generic;

namespace IdleDefense.Economy
{
    /// <summary>
    /// 부적 1군 8종.
    ///
    /// 세계관 문서 7.6의 7종에 무당을 더해 8종으로 맞췄다.
    /// 8종을 고른 이유는 C(8,5) = 56 — 슬롯 5개 조합 전수를 테스트 한 번에 돌릴 수 있는
    /// 가장 큰 수이기 때문이다. 나머지 12종은 라이브옵스 물량으로 남긴다.
    /// (20종이면 C(20,5) = 15,504라 전수 검증이 불가능하다)
    ///
    /// ★ 획득 방식 — 현재는 고정 지급이다.
    ///   마스터문서 9장의 "소프트런치에서 부적을 고정 지급으로 두고 커브만 먼저 검증"을
    ///   채택했다. 확률 뽑기를 지금 넣으면 유저마다 보유 부적이 달라
    ///   90일 커브 측정치의 해석이 불가능해진다.
    ///   확률 공시 의무(한국 2024.3~) 대응은 뽑기 도입 시점으로 미룬다.
    ///
    /// ★ 축 배분 — 이게 조합 콘텐츠의 전부다.
    ///   원시 3종(포졸·장군·홍길동)  : 자기 혼자 값을 만든다
    ///   즉발 1종(저승사자)          : 잔여 체력을 직접 깎는다
    ///   메타 4종(암행어사·전우치·처용·무당) : 다른 부적이 있어야 의미가 생긴다
    ///
    ///   메타가 절반인 것이 의도다. 메타만 5개 끼우면 아무 일도 일어나지 않고,
    ///   원시만 5개 끼우면 곱셈 하나로 수렴한다. 그 사이에 최적점이 있다.
    ///
    /// ★ 배치(Lane) 적용 범위 — 알려진 비대칭.
    ///   지속형(Damage/Amplify/Duplicate)과 저승사자(Execute)는 배치 지연을 받는다.
    ///   처용(Haste)·무당(Extend)은 소환 즉시 적용되며 배치 영향을 받지 않는다.
    ///   "쿨타임을 2.5초 뒤에 깎는다"가 유저에게 아무 의미도 없기 때문이다.
    ///
    /// ★ 수치는 측정 후 확정한다.
    ///   여기 적힌 값은 56조합 계측의 '출발점'이며 설계 확정치가 아니다.
    ///   조정은 반드시 TalismanCombinationTests의 측정 결과를 근거로 한다.
    /// </summary>
    public static class TalismanCatalog
    {
        public const string Pojol = "pojol";
        public const string Janggun = "janggun";
        public const string Hongildong = "hongildong";
        public const string Jeoseungsaja = "jeoseungsaja";
        public const string Amhaengeosa = "amhaengeosa";
        public const string Jeonuchi = "jeonuchi";
        public const string Cheoyong = "cheoyong";
        public const string Mudang = "mudang";

        // ── 2군 9종 ──
        public const string Ganggamchan = "ganggamchan";
        public const string Eoduksini   = "eoduksini";
        public const string Jangseung   = "jangseung";
        public const string Gumiho      = "gumiho";
        public const string Imugi       = "imugi";
        public const string Sansin      = "sansin";
        public const string Kkachi      = "kkachi";
        public const string Bulgasari   = "bulgasari";
        public const string Dokkaebi    = "dokkaebi";

        /// <summary>1군 8종. 순서는 고정이며 조합 인덱스의 기준이 된다.</summary>
        public static IReadOnlyList<TalismanSystem.Talisman> FirstGroup => firstGroup;

        private static readonly TalismanSystem.Talisman[] firstGroup =
        {
            // ── 원시 3종 ──

            // 포졸 — 다수 소환, 저비용. 쿨이 짧아 자주 쓰지만 한 번의 값은 작다.
            // 조합에서의 역할: 전우치·암행어사가 물어갈 '재료'를 자주 만들어 준다.
            new TalismanSystem.Talisman
            {
                Id = Pojol, DisplayName = "포졸",
                Effect = TalismanEffect.Damage,
                Magnitude = 1.25, BaseDuration = 8.0, Cooldown = 25.0,
            },

            // 장군 — 광역 공격. 한 방이 크고 쿨이 길다. 포졸의 정반대.
            //
            // ★ 수치 근거 — 쿨 70초에서 조정. 이름값과 실제 세기가 어긋나 있었다.
            //   화면에는 장군 x1.90 / 포졸 x1.25로 보이는데 단독 위력은
            //   장군 9.7% < 포졸 10.2%로 뒤집혀 있었다. 유저가 믿는 것과 반대다.
            //   원인은 세기가 아니라 노출 시간이다:
            //     장군 6초 x 11회 =  66초   (쿨 70)
            //     포졸 8초 x 31회 = 248초   (쿨 25)
            //   쿨을 50으로 낮추면 13.3%가 되어
            //   포졸(10.2) < 홍길동(11.1) < 장군(13.3) < 저승사자(15.0) 순서가 성립한다.
            //   근거: docs/부적1군_설계와_계측.md 7장
            new TalismanSystem.Talisman
            {
                Id = Janggun, DisplayName = "장군",
                Effect = TalismanEffect.Damage,
                Magnitude = 1.90, BaseDuration = 6.0, Cooldown = 50.0,
            },

            // 홍길동 — 분신. 같은 효과가 두 겹으로 걸린다.
            // 곱셈이라 1.30 x 1.30 = 1.69이고, 암행어사가 붙으면 두 겹 모두 증폭된다.
            // 메타와 가장 잘 맞는 원시 부적이다.
            new TalismanSystem.Talisman
            {
                Id = Hongildong, DisplayName = "홍길동",
                Effect = TalismanEffect.Damage, Copies = 2,
                Magnitude = 1.30, BaseDuration = 7.0, Cooldown = 55.0,
            },

            // ── 즉발 1종 ──

            // 저승사자 — 즉사/처형. 현재 웨이브의 '잔여' 체력을 비율로 삭제한다.
            // ★ 총 체력(WaveHpTotal)은 건드리지 않는다. 건드리면 벽 판정식이 바뀌어
            //   부적이 도달점을 옮기게 되고, 그 순간 90일 커브가 유저마다 갈라진다.
            // 배수형이 아니라서 DPS가 낮을수록 상대적으로 강하다 — 벽 근처에서 가치가 오른다.
            //
            // ★ 수치 근거 — 초안 0.35 / 쿨 90초에서 조정. 측정 후 확정된 유일한 값이다.
            //   초안은 56조합 계측에서 기여도 −5.0%로 8종 중 유일하게 '끼우면 손해'였다.
            //   원인은 세기가 아니라 빈도다. 웨이브 하나가 약 10초에 끝나는데 쿨이 90초라
            //   런 전체에서 8~9회밖에 못 쓴다. 한 번에 3.5초를 벌어도 총 30초, 약 3.5%뿐이다.
            //   그래서 쿨을 절반으로 줄이고 세기를 올렸다. 기여도 +1.4%,
            //   Damage 부적이 없는 최악 조합도 5.1% → 15.0%로 올라온다.
            //   근거: docs/부적1군_설계와_계측.md 5장
            new TalismanSystem.Talisman
            {
                Id = Jeoseungsaja, DisplayName = "저승사자",
                Effect = TalismanEffect.Execute,
                Magnitude = 0.65, BaseDuration = 0.0, Cooldown = 45.0,
            },

            // ── 메타 4종 ──
            //
            // ★ 자체 효과(Self*) — 2026-08-20 추가.
            //   증폭·복제·연장은 기댈 대상이 없으면 아무 일도 하지 않는다.
            //   그래서 '메타만 5개'인 조합의 바닥이 0.0%로 측정됐다.
            //   각 메타에 고유한 자체 효과를 주어 그 바닥을 세웠다.
            //   지속은 고정 초가 아니라 Cooldown x 0.30 이다 —
            //   0.45로 올리면 메타 최고 단독 위력(11.2%)이 포졸(10.2%)을 넘어
            //   이름값 정합성이 무너진다. 0.30이 측정된 상한이다.
            //   근거: docs/부적2군_설계와_계측.md 4장

            // 암행어사 — 적 약점 노출. 다른 효과의 초과분(m-1)을 60% 키운다.
            // 혼자 끼우면 배수가 1.0 그대로다. 반드시 원시 부적과 같이 써야 한다.
            new TalismanSystem.Talisman
            {
                Id = Amhaengeosa, DisplayName = "암행어사",
                Effect = TalismanEffect.Amplify,
                Magnitude = 0.60, BaseDuration = 10.0, Cooldown = 60.0,
                // 마패를 들이민다 — 증폭할 대상이 없어도 소량의 즉시삭제는 낸다.
                SelfEffect = TalismanEffect.Execute, SelfMagnitude = 0.08, SelfAlways = true,
            },

            // 전우치 — 변신/복제. 발동 중인 다른 효과 하나를 80% 세기로 복제한다.
            // 복제 대상이 없으면 아무 일도 없다. 소환 '순서'가 결과를 바꾸는 유일한 부적이다.
            new TalismanSystem.Talisman
            {
                Id = Jeonuchi, DisplayName = "전우치",
                Effect = TalismanEffect.Duplicate,
                Magnitude = 0.80, BaseDuration = 9.0, Cooldown = 50.0,
                // 환술(분신) — 복제할 대상이 없을 때만.
                SelfEffect = TalismanEffect.Damage, SelfMagnitude = 1.20, SelfAlways = false,
            },

            // 처용 — 역병을 씻어낸다. 다른 부적들의 남은 쿨타임을 35% 깎는다.
            // 직접 피해는 0이다. 장군처럼 쿨이 긴 부적과 조합할 때만 값이 나온다.
            new TalismanSystem.Talisman
            {
                Id = Cheoyong, DisplayName = "처용",
                Effect = TalismanEffect.Haste,
                Magnitude = 0.35, BaseDuration = 0.0, Cooldown = 80.0,
                // 춤 — 쿨감은 언제나 작동하므로 자체 효과도 항상 붙는다.
                SelfEffect = TalismanEffect.Damage, SelfMagnitude = 1.12, SelfAlways = true,
            },

            // 무당 — 굿으로 붙잡는다. 발동 중인 모든 효과의 지속을 5초 늘린다.
            // 여러 효과가 겹친 순간에 써야 값이 나온다. 타이밍 조작의 축이다.
            new TalismanSystem.Talisman
            {
                Id = Mudang, DisplayName = "무당",
                Effect = TalismanEffect.Extend,
                Magnitude = 5.0, BaseDuration = 0.0, Cooldown = 65.0,
                // 작두 — 연장할 대상이 없을 때만.
                SelfEffect = TalismanEffect.Damage, SelfMagnitude = 1.15, SelfAlways = false,
            },
        };

        /// <summary>
        /// 부적 2군 9종. 초안 12종에서 겹치는 3종(야차·바리데기·삼족오)을 제외했다.
        ///
        /// 제외 근거 — 같은 축에서 하는 일이 겹쳤다:
        ///   야차(Execute 40%/28초)   ← 저승사자(65%/45초)의 하위호환. 기여도 -1.12%
        ///   바리데기(되살림)          ← 전우치(복제)와 하는 일이 같다. 기여도 -6.23% (최하위)
        ///   삼족오(Extend +4.0/45초) ← 무당(+5.0/65초)과 거의 동일
        ///
        /// 이 제외가 바닥 문제를 통째로 해결했다.
        ///   바닥(원시 1개 이상) 15.1% → 18.3%
        ///   원시 0개 조합 126개(최악 10.8%) → 21개(최악 18.0%)
        ///
        /// ★ 바닥이 낮으면 메타를 강화하기 전에 겹치는 부적부터 찾아라.
        ///   문제는 메타가 약한 게 아니라 같은 일을 하는 부적이 슬롯을 채우는 것이었다.
        ///
        /// 근거: docs/부적2군_설계와_계측.md
        /// </summary>
        public static IReadOnlyList<TalismanSystem.Talisman> SecondGroup => secondGroup;

        private static readonly TalismanSystem.Talisman[] secondGroup =
        {
            // ── 새로운 결 6종 — 조합 수가 아니라 '조작의 결'을 늘린다 ──

            // 도깨비 — 변덕. 소환마다 기존 6축 중 하나를 무작위로 발동한다.
            // 1군은 전부 결정론이라 '운'이 유일하게 새로운 결이다.
            // 롤 테이블은 TalismanSystem.RollTable — 원시 가중 D3:X2:기타1.
            new TalismanSystem.Talisman
            {
                Id = Dokkaebi, DisplayName = "도깨비",
                Effect = TalismanEffect.Random,
                Magnitude = 0.0, BaseDuration = 0.0, Cooldown = 40.0,
            },

            // 구미호 — 누적. 꼬리가 아홉이라 8스택이 끝이다.
            // 오래 쓸수록 세지므로 '일찍 깔아두는' 조작을 보상한다.
            new TalismanSystem.Talisman
            {
                Id = Gumiho, DisplayName = "구미호",
                Effect = TalismanEffect.Stack,
                Magnitude = 1.15, BaseDuration = 8.0, Cooldown = 35.0,
                StackStep = 0.05, StackCap = 8,
            },

            // 이무기 — 만숙. 천 년을 기다려 용이 된다.
            // 배수 1.0에서 시작해 초당 0.06씩 자란다. 15초를 다 채우면 1.90.
            // Magnitude는 쓰이지 않는다 — 시작값이 항상 1.0이다.
            new TalismanSystem.Talisman
            {
                Id = Imugi, DisplayName = "이무기",
                Effect = TalismanEffect.Mature,
                Magnitude = 1.0, BaseDuration = 15.0, Cooldown = 60.0,
                GrowPerSecond = 0.06,
            },

            // 장승 — 자동. 쿨이 끝나면 스스로 발동한다.
            //
            // ★ 17종 중 유일하게 조작이 필요 없는 부적이다.
            //   1군 8종은 전부 '눌러야 값이 나는' 부적이라,
            //   방치형인데 방치하면 손해라는 모순이 있었다. 자동 축이 그걸 푼다.
            //
            //   가치는 '안 누를 때'만 보인다 (실측):
            //     idle     장승 포함 8.8%  vs 미포함 -0.4%   → +9.16%p
            //     greedy   34.4% vs 33.8%                    → +0.61%p
            //   즉 AutoSummon을 산 유저에게는 거의 값이 없다. 그게 의도다 —
            //   9.16%p는 AutoSummon 전체 가치의 29%이며, 전환 퍼널로 작동한다.
            new TalismanSystem.Talisman
            {
                Id = Jangseung, DisplayName = "장승",
                Effect = TalismanEffect.Auto, IsAuto = true,
                Magnitude = 1.30, BaseDuration = 10.0, Cooldown = 30.0,
            },

            // 불가사리 — 희생. 쇠를 먹고 자란다.
            // 남은 쿨이 가장 큰 다른 부적을 골라 '절반만' 먹고 그만큼 세진다.
            // ★ 전부 먹게 두면(쿨 0) 대형 단발이 무한 반복되어 최선 조합이 85%까지 튄다.
            new TalismanSystem.Talisman
            {
                Id = Bulgasari, DisplayName = "불가사리",
                Effect = TalismanEffect.Feed,
                Magnitude = 1.0, BaseDuration = 6.0, Cooldown = 70.0,
                FeedPerSecond = 60.0, FeedCap = 0.5,
            },

            // 어둑시니 — 조건부. 어두울수록 커진다.
            // 웨이브 잔여 체력이 낮을수록 배수가 오른다. 만체력 1.35, 빈사 1.70.
            // 배수는 매 틱 다시 계산된다 — 소환 시점이 아니라 '지금'의 체력을 본다.
            new TalismanSystem.Talisman
            {
                Id = Eoduksini, DisplayName = "어둑시니",
                Effect = TalismanEffect.Conditional,
                Magnitude = 1.35, BaseDuration = 7.0, Cooldown = 45.0,
                CondFactor = 2.0,
            },

            // ── 기존 축 심화 3종 ──

            // 강감찬 — 대형 단발. 장군보다 세고 느리다.
            new TalismanSystem.Talisman
            {
                Id = Ganggamchan, DisplayName = "강감찬",
                Effect = TalismanEffect.Damage,
                Magnitude = 2.20, BaseDuration = 5.0, Cooldown = 55.0,
            },

            // 산신 — 증폭. 암행어사보다 세고 짧다.
            // 축이 암행어사와 겹치지만 수치대가 달라 다른 선택지로 기능한다(수용된 겹침).
            new TalismanSystem.Talisman
            {
                Id = Sansin, DisplayName = "산신",
                Effect = TalismanEffect.Amplify,
                Magnitude = 0.75, BaseDuration = 8.0, Cooldown = 55.0,
                // 호랑이가 문다 — 증폭할 대상이 없을 때만.
                SelfEffect = TalismanEffect.Damage, SelfMagnitude = 1.18, SelfAlways = false,
            },

            // 까치호랑이 — 쿨감. 처용의 계단 문제(1군 7.1장)를 다른 수치대에서 재시도한다.
            // 17종 중 유일하게 기여도가 양수인 메타다(+0.04%).
            new TalismanSystem.Talisman
            {
                Id = Kkachi, DisplayName = "까치호랑이",
                Effect = TalismanEffect.Haste,
                Magnitude = 0.25, BaseDuration = 0.0, Cooldown = 40.0,
                // 까치가 운다 — 쿨감은 언제나 작동하므로 자체 효과도 항상 붙는다.
                SelfEffect = TalismanEffect.Damage, SelfMagnitude = 1.10, SelfAlways = true,
            },
        };

        /// <summary>전체 17종. 장착·조회는 이걸 본다.</summary>
        public static IReadOnlyList<TalismanSystem.Talisman> All => all;

        private static readonly TalismanSystem.Talisman[] all = BuildAll();

        private static TalismanSystem.Talisman[] BuildAll()
        {
            var result = new TalismanSystem.Talisman[firstGroup.Length + secondGroup.Length];
            Array.Copy(firstGroup, 0, result, 0, firstGroup.Length);
            Array.Copy(secondGroup, 0, result, firstGroup.Length, secondGroup.Length);
            return result;
        }

        /// <summary>
        /// 기본 장착 5종. 고정 지급이므로 8종 모두 보유하고 있고, 이건 '무엇을 끼고 있는가'다.
        /// 원시 3 + 즉발 1 + 메타 1 — 메타 부적이 무엇인지 배우게 하는 구성이다.
        /// </summary>
        public static string[] DefaultLoadout()
            => new[] { Pojol, Janggun, Hongildong, Jeoseungsaja, Amhaengeosa };

        /// <summary>
        /// 화면 표시 순서 — 집계용 정렬(NormalizeLoadout)과 분리한다.
        ///
        /// NormalizeLoadout의 Ordinal 정렬은 '조합 키'를 소환 순서와 무관하게 만들기 위한 것이지
        /// 사람에게 보여줄 순서가 아니다. 그걸 그대로 UI에 쓰면 내부 영문 id의 알파벳순이
        /// 그대로 노출된다 — amhaengeosa, hongildong, janggun, jeoseungsaja, pojol.
        /// 스토어 아이콘이 약속한 저승사자가 네 번째 칸에 묻힌다.
        ///
        /// 순서 근거: 원시 4종 먼저, 그 안에서 저승사자가 첫 칸이다.
        /// 즉발이라 지속 개념을 몰라도 되고, 체력 감소가 눈에 바로 보이며,
        /// 단독 위력이 15.0%로 8종 중 1위다. 첫 30초를 책임질 부적이다.
        ///
        /// 이 배열은 표시에만 쓴다. 시뮬레이션과 자동소환은 Equipped 순서를 그대로 쓰므로
        /// 여기를 바꿔도 밸런스는 변하지 않는다.
        /// </summary>
        public static readonly string[] DisplayOrder =
        {
            // 1군 — 원시 먼저, 그 안에서 저승사자가 첫 칸
            Jeoseungsaja, Janggun, Hongildong, Pojol,
            // 2군 원시
            Ganggamchan, Imugi, Eoduksini, Gumiho, Jangseung, Dokkaebi,
            // 메타
            Amhaengeosa, Sansin, Jeonuchi, Cheoyong, Kkachi, Mudang, Bulgasari,
        };

        /// <summary>표시 순서상 위치. 목록에 없으면 맨 뒤로 보낸다.</summary>
        public static int DisplayIndexOf(string id)
        {
            for (int i = 0; i < DisplayOrder.Length; i++)
                if (DisplayOrder[i] == id) return i;
            return int.MaxValue;
        }

        public static bool Exists(string id)
        {
            for (int i = 0; i < all.Length; i++)
                if (all[i].Id == id) return true;
            return false;
        }

        /// <summary>
        /// 장착 목록 정규화 — 없는 id 제거, 중복 제거, 슬롯 상한, 정렬.
        ///
        /// 중복 제거가 특히 중요하다. 부적 배수는 곱연산이라
        /// 같은 부적 5개면 배수가 그대로 5제곱이 된다(장군이면 1.90^5 = 24.8배).
        /// 정렬은 조합 키를 소환 순서와 무관하게 만들기 위한 것이다 —
        /// 순서를 남기면 같은 조합이 5! = 120개 키로 흩어져 집계가 안 된다.
        /// </summary>
        public static string[] NormalizeLoadout(string[] ids)
        {
            var list = new List<string>(TalismanSystem.MaxSlots);
            if (ids != null)
            {
                foreach (var id in ids)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!Exists(id)) continue;
                    if (list.Contains(id)) continue;
                    list.Add(id);
                    if (list.Count >= TalismanSystem.MaxSlots) break;
                }
            }
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        public static TalismanSystem.Talisman Get(string id)
        {
            for (int i = 0; i < all.Length; i++)
                if (all[i].Id == id) return all[i];
            throw new ArgumentException($"카탈로그에 없는 부적입니다: {id}", nameof(id));
        }

        /// <summary>
        /// 슬롯 수만큼 고르는 조합 전수. **1군 8종 전용이다.**
        ///
        /// ★ 여기를 17종으로 넓히지 마라.
        ///   C(17,5) = 6,188이라 이 함수를 도는 테스트 7개가 각각 110배로 늘어난다.
        ///   전수는 Unity가 아니라 파이썬(sim/tal2.py)이 돈다 — 6,188조합 x 3정책이 5분이다.
        ///   Unity는 파이썬이 찾은 극단 조합 + 고정시드 표본만 회귀 감시한다.
        ///   근거: docs/2군_검증전략.md 1장, 5장
        ///
        /// 8종 5슬롯이면 56가지다.
        /// 각 원소는 firstGroup의 인덱스 배열이다.
        /// </summary>
        public static List<int[]> AllCombinations(int slots)
        {
            var result = new List<int[]>();
            if (slots <= 0 || slots > firstGroup.Length) return result;

            Build(result, new int[slots], 0, 0, slots);
            return result;
        }

        // 로컬 함수(C# 7)를 쓰지 않는다. 컴파일러마다 지원이 갈려서
        // 문법 검증 파이프라인이 오탐을 내거나 진짜 오류를 가릴 수 있다.
        private static void Build(List<int[]> result, int[] buffer,
                                  int start, int depth, int slots)
        {
            if (depth == slots) { result.Add((int[])buffer.Clone()); return; }
            for (int i = start; i < firstGroup.Length; i++)
            {
                buffer[depth] = i;
                Build(result, buffer, i + 1, depth + 1, slots);
            }
        }

        public static string NameOf(int[] combo)
        {
            var parts = new string[combo.Length];
            for (int i = 0; i < combo.Length; i++) parts[i] = firstGroup[combo[i]].DisplayName;
            return string.Join("·", parts);
        }
    }
}

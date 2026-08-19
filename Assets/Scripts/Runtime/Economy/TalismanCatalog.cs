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

            // 암행어사 — 적 약점 노출. 다른 효과의 초과분(m-1)을 60% 키운다.
            // 혼자 끼우면 배수가 1.0 그대로다. 반드시 원시 부적과 같이 써야 한다.
            new TalismanSystem.Talisman
            {
                Id = Amhaengeosa, DisplayName = "암행어사",
                Effect = TalismanEffect.Amplify,
                Magnitude = 0.60, BaseDuration = 10.0, Cooldown = 60.0,
            },

            // 전우치 — 변신/복제. 발동 중인 다른 효과 하나를 80% 세기로 복제한다.
            // 복제 대상이 없으면 아무 일도 없다. 소환 '순서'가 결과를 바꾸는 유일한 부적이다.
            new TalismanSystem.Talisman
            {
                Id = Jeonuchi, DisplayName = "전우치",
                Effect = TalismanEffect.Duplicate,
                Magnitude = 0.80, BaseDuration = 9.0, Cooldown = 50.0,
            },

            // 처용 — 역병을 씻어낸다. 다른 부적들의 남은 쿨타임을 35% 깎는다.
            // 직접 피해는 0이다. 장군처럼 쿨이 긴 부적과 조합할 때만 값이 나온다.
            new TalismanSystem.Talisman
            {
                Id = Cheoyong, DisplayName = "처용",
                Effect = TalismanEffect.Haste,
                Magnitude = 0.35, BaseDuration = 0.0, Cooldown = 80.0,
            },

            // 무당 — 굿으로 붙잡는다. 발동 중인 모든 효과의 지속을 5초 늘린다.
            // 여러 효과가 겹친 순간에 써야 값이 나온다. 타이밍 조작의 축이다.
            new TalismanSystem.Talisman
            {
                Id = Mudang, DisplayName = "무당",
                Effect = TalismanEffect.Extend,
                Magnitude = 5.0, BaseDuration = 0.0, Cooldown = 65.0,
            },
        };

        /// <summary>
        /// 기본 장착 5종. 고정 지급이므로 8종 모두 보유하고 있고, 이건 '무엇을 끼고 있는가'다.
        /// 원시 3 + 즉발 1 + 메타 1 — 메타 부적이 무엇인지 배우게 하는 구성이다.
        /// </summary>
        public static string[] DefaultLoadout()
            => new[] { Pojol, Janggun, Hongildong, Jeoseungsaja, Amhaengeosa };

        public static bool Exists(string id)
        {
            for (int i = 0; i < firstGroup.Length; i++)
                if (firstGroup[i].Id == id) return true;
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
            for (int i = 0; i < firstGroup.Length; i++)
                if (firstGroup[i].Id == id) return firstGroup[i];
            throw new ArgumentException($"1군에 없는 부적입니다: {id}", nameof(id));
        }

        /// <summary>
        /// 슬롯 수만큼 고르는 조합 전수. 8종 5슬롯이면 56가지다.
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

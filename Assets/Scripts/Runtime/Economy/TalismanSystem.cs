using System;
using System.Collections.Generic;
using IdleDefense.Data;

namespace IdleDefense.Economy
{
    /// <summary>
    /// 부적이 전투에 개입하는 방식. 축이 여러 개인 이유가 이 게임의 방어선이다.
    ///
    /// 축이 하나(피해배수)뿐이면 모든 조합이 교환법칙이 성립하는 곱셈이라
    /// "어떤 조합이 좋은가"에 답이 하나뿐이고, C(8,5)=56 조합은 56개의 숫자에 불과하다.
    /// 마스터문서 9.1이 말한 15,504조합 방어선은 축이 여러 개여야만 성립한다.
    ///
    /// ★ 어떤 효과도 WaveHpTotal이나 baseDps를 바꾸지 않는다.
    ///   벽 판정은 WaveHpTotal / BaseDpsWithoutTalisman 이므로,
    ///   그 둘을 안 건드리는 한 부적은 속도만 바꾸고 도달점은 못 바꾼다.
    /// </summary>
    public enum TalismanEffect
    {
        /// <summary>지속시간 동안 DPS에 곱해진다. 가장 기본 축.</summary>
        Damage = 0,

        /// <summary>현재 웨이브의 '잔여' 체력을 즉시 비율만큼 삭제. 총 체력은 안 건드린다.</summary>
        Execute = 1,

        /// <summary>지속시간 동안 다른 활성 효과의 초과분(m-1)을 증폭한다.</summary>
        Amplify = 2,

        /// <summary>소환 시점에 발동 중인 다른 효과 하나를 복제한다. 혼자서는 아무 효과도 없다.</summary>
        Duplicate = 3,

        /// <summary>소환 시점에 다른 부적들의 남은 쿨타임을 비율만큼 깎는다.</summary>
        Haste = 4,

        /// <summary>소환 시점에 발동 중인 모든 효과의 남은 지속시간을 늘린다.</summary>
        Extend = 5,

        // ── 2군 6축 (2026-08-20) ──
        // 전부 '속도축'이다. 어느 것도 엽전·구슬·코어를 주지 않고
        // WaveHpTotal을 건드리지 않는다 — 그 순간 벽이 움직인다.
        // 근거: docs/부적_효과축_제약.md 1장

        /// <summary>소환할 때마다 기존 6축 중 하나를 무작위로 발동한다. 도깨비.</summary>
        Random = 6,

        /// <summary>소환할수록 배수가 쌓인다. 구미호(꼬리 아홉).</summary>
        Stack = 7,

        /// <summary>깔아둔 시간에 비례해 배수가 오른다. 이무기(천 년을 기다린다).</summary>
        Mature = 8,

        /// <summary>쿨이 끝나면 스스로 발동한다. 장승. 조작이 필요 없는 유일한 축.</summary>
        Auto = 9,

        /// <summary>다른 부적의 남은 쿨 절반을 먹고 그만큼 강해진다. 불가사리.</summary>
        Feed = 10,

        /// <summary>웨이브 체력이 낮을수록 강해진다. 어둑시니(어두울수록 커진다).</summary>
        Conditional = 11,
    }

    /// <summary>
    /// 부적 소환. 유저가 개입할 수 있는 유일한 전투 조작이다.
    ///
    /// 설계 원칙 — 이게 전부다:
    ///   조작은 '속도'를 바꾸되 '도달점'은 바꾸지 않는다.
    ///
    ///   잘 쓰면 같은 웨이브에 더 빨리 도달하고, 못 쓰면 느리게 도달한다.
    ///   하지만 어느 쪽도 벽 너머로 가지는 못한다.
    ///   그래야 90일 커브와 5분 세션이 유지된다.
    ///
    /// 커브를 지키는 세 장치:
    ///   1. 소환 유닛이 준 피해에는 코인이 붙지 않는다.
    ///      (붙으면 "부적 많이 쓰면 부자"가 되어 경제가 무너진다)
    ///   2. 쿨타임은 실시간으로만 흐른다.
    ///      (배속에 비례하면 배속권으로 부적을 남발할 수 있다)
    ///   3. Execute는 WaveHpRemaining만 깎는다. WaveHpTotal은 불변이다.
    ///      (총 체력을 깎으면 벽 판정식이 바뀌어 도달점이 움직인다)
    /// </summary>
    public class TalismanSystem
    {
        /// <summary>
        /// 소환 위치. 앞에 놓으면 즉시 개입하지만 짧고,
        /// 뒤에 놓으면 늦게 터지지만 오래 간다.
        /// </summary>
        public enum Lane
        {
            Front = 0,   // 지연 0.0초, 지속 0.7배
            Middle = 1,  // 지연 1.0초, 지속 1.0배
            Back = 2     // 지연 2.5초, 지속 1.4배
        }

        [Serializable]
        public class Talisman
        {
            public string Id;
            public string DisplayName;

            public TalismanEffect Effect = TalismanEffect.Damage;

            /// <summary>
            /// 효과 크기. 축마다 단위가 다르다.
            ///   Damage    — DPS 배수 (1.3 = 30% 증가)
            ///   Execute   — 잔여 체력 삭제 비율 (0.25 = 25%)
            ///   Amplify   — 다른 효과 초과분의 증폭률 (0.5 = 50% 더)
            ///   Duplicate — 복제본의 감쇠율 (0.8 = 원본 초과분의 80%)
            ///   Haste     — 다른 부적 쿨타임 감소 비율 (0.3 = 30%)
            ///   Extend    — 지속시간 추가 초
            /// </summary>
            public double Magnitude = 1.3;

            /// <summary>같은 효과를 몇 겹으로 거는가. 홍길동(분신)이 2다.</summary>
            public int Copies = 1;

            /// <summary>기본 지속시간(초). 배치 위치에 따라 조정된다. 즉발 효과는 0.</summary>
            public double BaseDuration = 8.0;

            /// <summary>쿨타임(초). 실시간으로만 감소한다.</summary>
            public double Cooldown = 45.0;

            /// <summary>남은 쿨타임.</summary>
            public double CooldownRemaining;

            public bool IsReady => CooldownRemaining <= 0;

            // ── 자체 효과 (하이브리드 메타)
            //
            // 증폭·복제·연장은 기댈 대상이 없으면 아무 일도 하지 않는다.
            // 그래서 메타만 5개인 조합의 바닥이 0.0%로 측정됐다.
            // 각 메타에 고유한 자체 효과를 주어 그 바닥을 세운다.
            //
            // 부적별 근거는 부적2군_설계와_계측.md 4장.

            /// <summary>자체 효과의 축. SelfMagnitude가 0이면 자체 효과 없음.</summary>
            public TalismanEffect SelfEffect = TalismanEffect.Damage;

            /// <summary>자체 효과 크기. 0이면 자체 효과가 없다.</summary>
            public double SelfMagnitude = 0.0;

            /// <summary>
            /// 자체 효과 지속 = Cooldown × 이 값.
            ///
            /// 0.30은 고른 값이 아니라 측정된 상한이다. 0.45로 올리면
            /// 메타 최고 단독 위력(11.2%)이 포졸(10.2%)을 넘어
            /// 이름값 정합성이 무너진다.
            ///
            /// 지속을 고정 초로 두면 안 된다 — 6~10초에 쿨 45~80초면
            /// 켜져 있는 시간이 12%뿐이라 세기를 올려도 값이 안 난다.
            /// </summary>
            public double SelfDurationRatio = 0.30;

            /// <summary>true면 주 효과의 성패와 무관하게 항상 발동한다.</summary>
            public bool SelfAlways = false;

            public bool HasSelf => SelfMagnitude > 0.0;

            // ── 2군 축별 파라미터 ──

            /// <summary>누적(Stack) — 소환마다 배수에 더해지는 양. 구미호.</summary>
            public double StackStep = 0.05;

            /// <summary>누적 상한. 구미호는 꼬리가 아홉이라 8스택이 끝이다.</summary>
            public int StackCap = 8;

            /// <summary>현재 쌓인 스택. 런타임 상태이며 환생 시 0으로 돌아간다.</summary>
            public int Stacks;

            /// <summary>만숙(Mature) — 초당 배수 상승분. 이무기.</summary>
            public double GrowPerSecond = 0.06;

            /// <summary>조건부(Conditional) — 웨이브 체력이 0일 때의 추가 배율. 어둑시니.</summary>
            public double CondFactor = 2.0;

            /// <summary>희생(Feed) — 흡수한 쿨 몇 초당 배수 1.0이 오르는가. 불가사리.</summary>
            public double FeedPerSecond = 60.0;

            /// <summary>희생 상한. 남의 쿨을 아무리 먹어도 이 이상은 안 오른다.</summary>
            public double FeedCap = 0.5;

            /// <summary>자동(Auto) — 쿨이 끝나면 스스로 발동한다. 장승.</summary>
            public bool IsAuto;

            public Talisman Clone() => (Talisman)MemberwiseClone();
        }

        /// <summary>발동 중이거나 지연 대기 중인 효과 하나.</summary>
        private struct ActiveEffect
        {
            public TalismanEffect Kind;
            public double DelayRemaining;   // 0이 되어야 실제로 작동한다
            public double Remaining;        // 지속 남은 시간
            public double Magnitude;
            public int SourceSlot;          // 복제 대상에서 자기 자신을 빼기 위한 표식

            /// <summary>
            /// 이미 연장된 효과인가. 한 효과는 한 번만 연장된다.
            ///
            /// 이 제한이 없으면 연장 + 복제 + 증폭이 물리면 효과가 사실상 영구화되고,
            /// 최선 조합의 단축률이 85%까지 튄다 (실측).
            /// </summary>
            public bool Extended;

            /// <summary>만숙 — 초당 배수 상승분. 0이면 고정 배수.</summary>
            public double Grow;

            /// <summary>조건부 — 웨이브 체력이 0에 가까울수록 커지는 계수. 0이면 무관.</summary>
            public double Cond;

            /// <summary>발동(지연 해제) 시각. 만숙 계산의 기준점. 전투시간 기준이다.</summary>
            public double BornAt;

            public bool IsLive => DelayRemaining <= 0.0 && Remaining > 0.0;
        }

        /// <summary>변덕(Random)이 뽑는 한 칸.</summary>
        private struct Roll
        {
            public TalismanEffect Effect;
            public double Magnitude;
            public int Copies;
            public double Duration;
        }

        /// <summary>
        /// 도깨비의 롤 테이블 — 원시 가중 D3 : X2 : 기타 각1.
        ///
        /// 균등(1/6)으로 두면 '메타만 5개' 조합의 바닥이 10.6%까지 내려간다.
        /// 원시를 가중하면 15.5%로 올라온다 (420런 실측).
        /// 근거: docs/부적_효과축_제약.md 2장
        /// </summary>
        private static readonly Roll[] RollTable =
        {
            new Roll { Effect = TalismanEffect.Damage,  Magnitude = 1.50, Copies = 1, Duration = 8.0 },
            new Roll { Effect = TalismanEffect.Damage,  Magnitude = 1.50, Copies = 1, Duration = 8.0 },
            new Roll { Effect = TalismanEffect.Damage,  Magnitude = 1.50, Copies = 1, Duration = 8.0 },
            new Roll { Effect = TalismanEffect.Execute, Magnitude = 0.40, Copies = 1, Duration = 0.0 },
            new Roll { Effect = TalismanEffect.Execute, Magnitude = 0.40, Copies = 1, Duration = 0.0 },
            new Roll { Effect = TalismanEffect.Amplify, Magnitude = 0.45, Copies = 1, Duration = 8.0 },
            new Roll { Effect = TalismanEffect.Duplicate, Magnitude = 0.70, Copies = 1, Duration = 8.0 },
            new Roll { Effect = TalismanEffect.Haste,   Magnitude = 0.30, Copies = 1, Duration = 0.0 },
            new Roll { Effect = TalismanEffect.Extend,  Magnitude = 4.00, Copies = 1, Duration = 0.0 },
        };

        /// <summary>
        /// 변덕 전용 난수. 시드를 고정할 수 있어야 한다.
        ///
        /// 시드를 못 박으면 도깨비가 낀 테스트가 매번 다른 결과를 내고,
        /// 재현되지 않는 실패는 곧 무시되기 시작한다.
        /// </summary>
        private Random rng = new Random(20260820);

        /// <summary>변덕의 난수 시드를 고정한다. 테스트와 재현용.</summary>
        public void SetRandomSeed(int seed) => rng = new Random(seed);

        public const int MaxSlots = 5;

        private readonly EconomyConfig cfg;
        private readonly List<Talisman> equipped = new List<Talisman>();
        private readonly List<ActiveEffect> active = new List<ActiveEffect>();

        /// <summary>
        /// 부적 id별 남은 쿨타임. ★ 이것이 쿨타임의 정본이다.
        ///
        /// 왜 슬롯이 아니라 부적에 귀속시키는가:
        ///   장착 목록을 갈아끼울 때 카탈로그 원본을 다시 복제하면
        ///   원본의 CooldownRemaining이 0이라 전체 쿨타임이 리셋된다.
        ///   그러면 "부적 하나 토글 → 장군 즉시 재소환"이 무한히 가능해지고,
        ///   쿨감이 유일한 가치인 처용은 존재 이유가 사라진다.
        ///   도달 웨이브는 안 뚫리지만(벽은 baseDps 판정) 런 시간 밸런스가 통째로 무의미해진다.
        ///   쿨타임은 '어느 슬롯에 꽂혀 있는가'가 아니라 '그 부적을 언제 썼는가'다.
        /// </summary>
        private readonly Dictionary<string, double> cooldowns
            = new Dictionary<string, double>();

        /// <summary>Tick에서 사전을 순회하며 수정하기 위한 스크래치 버퍼.</summary>
        private readonly List<string> cooldownKeys = new List<string>();

        /// <summary>아직 BattleRunner에 전달되지 않은 즉시 삭제 비율의 누적.</summary>
        private double pendingExecute;

        /// <summary>자동 소환. 켜면 알아서 쓰되 효율이 낮다.</summary>
        public bool AutoSummon { get; set; }

        /// <summary>자동 소환의 효율 계수. 수동보다 불리해야 조작할 이유가 생긴다.</summary>
        public double AutoEfficiency { get; set; } = 0.75;

        public event Action<Talisman, Lane> OnSummoned;

        public TalismanSystem(EconomyConfig config)
        {
            cfg = config ?? throw new ArgumentNullException(nameof(config));
        }

        public IReadOnlyList<Talisman> Equipped => equipped;

        /// <summary>
        /// 장착. 카탈로그 원본이 런타임 쿨타임에 오염되지 않도록 복제해서 넣는다.
        ///
        /// 같은 부적을 두 번 끼우는 것은 거부한다.
        /// 배수가 곱연산이라 같은 부적 5개면 배수가 그대로 5제곱이 되고,
        /// "부적은 속도만 바꾼다"는 보증이 세이브 조작 한 번으로 무너진다.
        /// GameController도 정규화하지만, 여기서 한 번 더 막는다.
        /// </summary>
        public bool Equip(Talisman t)
        {
            if (t == null || equipped.Count >= MaxSlots) return false;
            for (int i = 0; i < equipped.Count; i++)
                if (equipped[i].Id == t.Id) return false;

            var copy = t.Clone();

            // 카탈로그 원본은 CooldownRemaining = 0이다. 그대로 넣으면 장착 교체가
            // 쿨타임 리셋 버튼이 된다. 이 부적이 마지막으로 언제 쓰였는지를 되살린다.
            copy.CooldownRemaining = cooldowns.TryGetValue(copy.Id, out double rem) ? rem : 0.0;

            equipped.Add(copy);
            return true;
        }

        /// <summary>
        /// 장착 목록 교체. 카탈로그 id 배열을 받아 정규화한 뒤 그대로 끼운다.
        ///
        /// ★ 이 메서드가 장착의 유일한 정본 경로다.
        ///   GameController가 따로 UnequipAll + Equip을 조합하게 두면
        ///   쿨타임 보존 규칙이 호출자마다 갈라진다. 여기 한 곳에만 둔다.
        /// </summary>
        public string[] ApplyLoadout(string[] talismanIds)
        {
            var normalized = TalismanCatalog.NormalizeLoadout(talismanIds);

            UnequipAll();
            for (int i = 0; i < normalized.Length; i++)
                Equip(TalismanCatalog.Get(normalized[i]));

            return normalized;
        }

        public void UnequipAll()
        {
            // 버리기 전에 쿨타임을 기억해둔다. 이게 없으면 재장착이 리셋이 된다.
            for (int i = 0; i < equipped.Count; i++)
                cooldowns[equipped[i].Id] = equipped[i].CooldownRemaining;

            equipped.Clear();
            active.Clear();
            pendingExecute = 0.0;
        }

        // ─────────────────────────────────────────
        // 배치

        /// <summary>배치 위치별 지속시간 배수. 앞은 짧고 뒤는 길다.</summary>
        public static double LaneDurationScale(Lane lane)
        {
            switch (lane)
            {
                case Lane.Front: return 0.7;
                case Lane.Middle: return 1.0;
                case Lane.Back: return 1.4;
                default: return 1.0;
            }
        }

        /// <summary>배치 위치별 효과 발동 지연(초). 뒤에 놓을수록 늦게 터진다.</summary>
        public static double LaneDelay(Lane lane)
        {
            switch (lane)
            {
                case Lane.Front: return 0.0;
                case Lane.Middle: return 1.0;
                case Lane.Back: return 2.5;
                default: return 0.0;
            }
        }

        // ─────────────────────────────────────────
        // 소환

        /// <summary>
        /// 메타의 자체 효과를 건다.
        ///
        /// forced=true 는 주 효과가 기댈 대상을 찾지 못했다는 뜻이다.
        /// SelfAlways 인 부적은 주 효과의 성패와 무관하게 항상 건다.
        ///
        /// 지속은 고정 초가 아니라 Cooldown × SelfDurationRatio 다.
        /// 자체 효과는 약하므로 오래 켜져 있어야 값이 난다.
        /// </summary>
        private void ApplySelf(Talisman t, int slotIndex, Lane lane,
                               double delay, bool isAuto, bool forced)
        {
            if (!t.HasSelf) return;
            if (!t.SelfAlways && !forced) return;

            double mag = t.SelfMagnitude;
            if (isAuto) mag = Damp(t.SelfEffect, mag, AutoEfficiency);

            if (t.SelfEffect == TalismanEffect.Execute)
            {
                if (delay <= 0.0) pendingExecute = Combine(pendingExecute, mag);
                else active.Add(new ActiveEffect
                {
                    Kind = TalismanEffect.Execute,
                    DelayRemaining = delay,
                    Remaining = double.PositiveInfinity,
                    Magnitude = mag,
                    SourceSlot = slotIndex,
                });
                return;
            }

            active.Add(new ActiveEffect
            {
                Kind = t.SelfEffect,
                DelayRemaining = delay,
                Remaining = t.Cooldown * t.SelfDurationRatio * LaneDurationScale(lane),
                Magnitude = mag,
                SourceSlot = slotIndex,
            });
        }

        /// <summary>
        /// 효과 하나를 깐다. Grow(만숙)/Cond(조건부)는 여기서만 채워진다.
        /// BornAt은 지연이 풀리는 순간 Tick이 다시 찍는다 — 여기 값은 지연 0일 때만 유효하다.
        /// </summary>
        private void Place(TalismanEffect kind, double magnitude, int copies,
                           double duration, double delay, int slotIndex,
                           double grow = 0.0, double cond = 0.0)
        {
            for (int c = 0; c < Math.Max(1, copies); c++)
                active.Add(new ActiveEffect
                {
                    Kind = kind,
                    DelayRemaining = delay,
                    Remaining = duration,
                    Magnitude = magnitude,
                    SourceSlot = slotIndex,
                    Grow = grow,
                    Cond = cond,
                    BornAt = battleTime,
                });
        }

        /// <summary>
        /// 소환을 마무리한다 — 쿨타임을 걸고 이벤트를 쏜다.
        /// 새 축들이 중간에 return하므로 이 마무리를 한 곳으로 모았다.
        /// 빠뜨리면 그 부적만 쿨 없이 무한 소환된다.
        /// </summary>
        private void Consume(Talisman t, int slotIndex, Lane lane)
        {
            t.CooldownRemaining = t.Cooldown;
            cooldowns[t.Id] = t.Cooldown;
            OnSummoned?.Invoke(t, lane);
        }

        /// <summary>소환. 성공하면 true. 실패 사유: 슬롯 범위 밖, 쿨타임.</summary>
        public bool Summon(int slotIndex, Lane lane, bool isAuto = false)
        {
            if (slotIndex < 0 || slotIndex >= equipped.Count) return false;
            var t = equipped[slotIndex];
            if (!t.IsReady) return false;

            double delay = LaneDelay(lane);
            double scale = LaneDurationScale(lane);
            double duration = t.BaseDuration * scale;
            double magnitude = t.Magnitude;
            int copies = Math.Max(1, t.Copies);
            var effect = t.Effect;

            // ── 변덕(Random) — 먼저 주사위를 굴려 실제 축으로 바꾼다.
            //    뽑히는 것은 기존 6축뿐이라 벽 보증이 자동으로 유지된다.
            if (effect == TalismanEffect.Random)
            {
                var roll = RollTable[rng.Next(RollTable.Length)];
                effect = roll.Effect;
                magnitude = roll.Magnitude;
                copies = roll.Copies;
                duration = roll.Duration * scale;
            }

            // ★ 감쇠(Damp)는 여기서 걸면 안 된다.
            //   Auto/Stack/Mature/Feed/Conditional은 Damp의 default 분기
            //   (magnitude * efficiency)로 떨어져 배수가 1.0 아래로 내려간다.
            //   장승 1.30 x 0.75 = 0.975 — 부적을 켰는데 DPS가 줄었다.
            //   그리고 불가사리·이무기는 실제 배수를 '해석 이후'에 계산하므로
            //   여기서 감쇠해도 아무 효과가 없다.
            //   → 축이 해석되고 값이 확정된 자리에서 각자 감쇠한다.
            double eff = isAuto ? AutoEfficiency : 1.0;

            // ── 2군 전용 축 — 여기서 끝나거나 Damage로 환원된다.
            switch (effect)
            {
                case TalismanEffect.Stack:
                    // 소환할수록 세진다. 꼬리가 쌓이는 것이다.
                    // 스택 보너스를 더한 '최종' 배수가 감쇠 대상이어야 한다.
                    magnitude += t.StackStep * Math.Min(t.Stacks, t.StackCap);
                    t.Stacks = Math.Min(t.Stacks + 1, t.StackCap);
                    effect = TalismanEffect.Damage;
                    break;

                case TalismanEffect.Auto:
                    // 발동 '방식'만 다르고 효과는 피해다.
                    effect = TalismanEffect.Damage;
                    break;

                case TalismanEffect.Mature:
                    // 배수 1.0에서 시작해 깔려 있는 동안 자란다.
                    // 시작 배수가 1.0이라 배수를 감쇠해도 아무 일이 없다.
                    // 자동의 불리함은 '자라는 속도'로 표현한다.
                    Place(TalismanEffect.Damage, 1.0, copies, duration, delay, slotIndex,
                          grow: t.GrowPerSecond * eff);
                    Consume(t, slotIndex, lane);
                    return true;

                case TalismanEffect.Conditional:
                    // 웨이브가 죽어갈수록 세진다. 배수는 매 틱 다시 계산된다.
                    Place(TalismanEffect.Damage, Damp(TalismanEffect.Damage, magnitude, eff),
                          copies, duration, delay, slotIndex, cond: t.CondFactor);
                    Consume(t, slotIndex, lane);
                    return true;

                case TalismanEffect.Feed:
                    // 남은 쿨이 가장 큰 다른 부적을 먹는다. 전부가 아니라 절반만.
                    {
                        int victim = -1;
                        double best = 0.0;
                        for (int i = 0; i < equipped.Count; i++)
                        {
                            if (i == slotIndex) continue;
                            if (equipped[i].CooldownRemaining > best)
                            { best = equipped[i].CooldownRemaining; victim = i; }
                        }
                        // 먹을 게 없으면 쿨을 쓰지 않는다. 헛발질에 대가를 물리지 않는다.
                        if (victim < 0) return false;

                        equipped[victim].CooldownRemaining = best * 0.5;
                        cooldowns[equipped[victim].Id] = equipped[victim].CooldownRemaining;

                        // 배수 1.0은 선언값일 뿐이고 실제 세기는 여기서 정해진다.
                        // 감쇠는 '먹어서 얻은 몫'에 건다.
                        double gain = Math.Min(t.FeedCap, best / Math.Max(1e-9, t.FeedPerSecond)) * eff;
                        Place(TalismanEffect.Damage, 1.0 + gain, copies, duration, delay, slotIndex);
                        Consume(t, slotIndex, lane);
                        return true;
                    }
            }

            // 축이 확정된 뒤에 감쇠한다. Stack/Auto는 이미 Damage로 환원돼 있다.
            if (isAuto) magnitude = Damp(effect, magnitude, AutoEfficiency);

            switch (effect)
            {
                case TalismanEffect.Execute:
                    // 즉발이지만 배치 지연은 받는다. 지연이 있으면 대기 효과로 넣는다.
                    if (delay <= 0.0) pendingExecute = Combine(pendingExecute, magnitude);
                    else active.Add(new ActiveEffect
                    {
                        Kind = TalismanEffect.Execute,
                        DelayRemaining = delay,
                        Remaining = double.PositiveInfinity,   // 발동 순간 소모된다
                        Magnitude = magnitude,
                        SourceSlot = slotIndex,
                    });
                    break;

                case TalismanEffect.Haste:
                    // 다른 부적의 쿨타임만 깎는다. 자기 자신은 안 깎는다.
                    for (int i = 0; i < equipped.Count; i++)
                    {
                        if (i == slotIndex) continue;
                        equipped[i].CooldownRemaining *= (1.0 - Clamp01(magnitude));
                        cooldowns[equipped[i].Id] = equipped[i].CooldownRemaining;
                    }
                    // 쿨감은 언제나 무언가를 한다. 자체 효과는 SelfAlways일 때만 붙는다.
                    ApplySelf(t, slotIndex, lane, delay, isAuto, forced: false);
                    break;

                case TalismanEffect.Extend:
                    // 이미 발동 중이거나 지연 대기 중인 효과의 지속을 늘린다.
                    {
                        bool hasTarget = false;
                        for (int i = 0; i < active.Count; i++)
                            if (active[i].Kind != TalismanEffect.Execute) { hasTarget = true; break; }

                        // 자체 효과를 '연장하기 전에' 판정한다.
                        // 순서를 바꾸면 방금 깐 자기 효과를 자기가 연장한다.
                        ApplySelf(t, slotIndex, lane, delay, isAuto, forced: !hasTarget);

                        for (int i = 0; i < active.Count; i++)
                        {
                            var e = active[i];
                            if (e.Kind == TalismanEffect.Execute) continue;
                            if (double.IsPositiveInfinity(e.Remaining)) continue;
                            if (e.Extended) continue;      // 한 효과는 한 번만 연장된다
                            e.Remaining += magnitude;
                            e.Extended = true;
                            active[i] = e;
                        }
                    }
                    break;

                case TalismanEffect.Duplicate:
                    // 발동 중인 '다른' 효과 중 가장 최근 것을 복제한다.
                    {
                        int src = FindLatestCopyable(slotIndex);
                        if (src >= 0)
                        {
                            var e = active[src];
                            active.Add(new ActiveEffect
                            {
                                Kind = e.Kind,
                                DelayRemaining = delay,
                                Remaining = duration,
                                Magnitude = 1.0 + (e.Magnitude - 1.0) * Clamp01(magnitude),
                                SourceSlot = slotIndex,
                            });
                        }
                        ApplySelf(t, slotIndex, lane, delay, isAuto, forced: src < 0);
                    }
                    break;

                case TalismanEffect.Amplify:
                    // 증폭할 피해 효과가 하나도 없으면 증폭 자체를 깔지 않는다.
                    // 빈 증폭을 깔아두면 연장·복제의 대상이 되어 계산이 오염된다.
                    {
                        bool hasDamage = false;
                        for (int i = 0; i < active.Count; i++)
                            if (active[i].IsLive && active[i].Kind == TalismanEffect.Damage)
                            { hasDamage = true; break; }

                        if (hasDamage)
                        {
                            Place(TalismanEffect.Amplify, magnitude, copies,
                                  duration, delay, slotIndex);
                        }
                        ApplySelf(t, slotIndex, lane, delay, isAuto, forced: !hasDamage);
                    }
                    break;

                default:   // Damage
                    // ★ t.Effect가 아니라 해석된 effect를 쓴다.
                    //   변덕이 Damage를 뽑았을 때 Kind에 Random이 들어가면
                    //   배수 계산에서 통째로 무시된다.
                    Place(effect, magnitude, copies, duration, delay, slotIndex);
                    break;
            }

            Consume(t, slotIndex, lane);
            return true;
        }

        /// <summary>복제 대상 — 자기가 만든 것이 아닌, 발동 중인 배수형 효과 중 마지막.</summary>
        private int FindLatestCopyable(int slotIndex)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].SourceSlot == slotIndex) continue;
                if (active[i].Kind == TalismanEffect.Execute) continue;
                if (active[i].Remaining <= 0.0) continue;
                return i;
            }
            return -1;
        }

        /// <summary>자동 소환 감쇠. 축마다 '1이 기준'인지 '0이 기준'인지가 다르다.</summary>
        private static double Damp(TalismanEffect kind, double magnitude, double efficiency)
        {
            switch (kind)
            {
                // '1이 기준'인 축 — 감쇠는 초과분(m-1)에만 건다.
                // ★ 2군 축을 여기 빠뜨리면 default로 새서 배수가 1.0 아래로 내려간다.
                //   실제로 장승(Auto)이 그렇게 1.30 x 0.75 = 0.975가 됐었다.
                case TalismanEffect.Damage:
                case TalismanEffect.Duplicate:
                case TalismanEffect.Stack:
                case TalismanEffect.Auto:
                case TalismanEffect.Mature:
                case TalismanEffect.Conditional:
                case TalismanEffect.Feed:
                    return 1.0 + (magnitude - 1.0) * efficiency;

                // '0이 기준'인 축 — 비율이라 그대로 곱한다.
                default:
                    return magnitude * efficiency;
            }
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        /// <summary>삭제 비율의 중첩. 25% + 25%는 50%가 아니라 43.75%다(잔여 기준 연속 적용).</summary>
        private static double Combine(double a, double b)
            => 1.0 - (1.0 - Clamp01(a)) * (1.0 - Clamp01(b));

        // ─────────────────────────────────────────
        // 시간 진행

        /// <summary>
        /// 시간 진행.
        ///
        /// realDeltaTime — 실제 경과 시간. 쿨타임은 이걸로만 줄인다.
        /// battleDeltaTime — 배속이 적용된 전투 시간. 지연과 지속은 이걸로 줄인다.
        ///
        /// 둘을 나눈 이유: 쿨타임까지 배속을 받으면
        /// 배속권 하나로 부적을 두 배로 쓸 수 있게 된다.
        /// </summary>
        /// <summary>
        /// 전투 시각 누적. 만숙(Mature)이 "얼마나 오래 깔려 있었는가"를 재는 기준이다.
        /// 실시간이 아니라 전투시간인 이유: 배속을 켜면 효과도 같이 빨리 익어야 한다.
        /// </summary>
        private double battleTime;

        public void Tick(double realDeltaTime, double battleDeltaTime)
        {
            battleTime += battleDeltaTime;

            for (int i = 0; i < equipped.Count; i++)
            {
                var t = equipped[i];
                if (t.CooldownRemaining > 0)
                    t.CooldownRemaining = Math.Max(0, t.CooldownRemaining - realDeltaTime);
                cooldowns[t.Id] = t.CooldownRemaining;
            }

            // 빼놓은 부적의 쿨타임도 같이 흐른다.
            // 멈춰 있게 두면 "빼두면 손해"가 되어 유저가 슬롯을 실험하지 않게 되고,
            // 조합 콘텐츠가 실질적으로 잠긴다.
            if (cooldowns.Count > equipped.Count)
            {
                cooldownKeys.Clear();
                foreach (var kv in cooldowns)
                    if (kv.Value > 0 && !IsEquipped(kv.Key)) cooldownKeys.Add(kv.Key);

                for (int i = 0; i < cooldownKeys.Count; i++)
                    cooldowns[cooldownKeys[i]] =
                        Math.Max(0, cooldowns[cooldownKeys[i]] - realDeltaTime);
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];

                if (e.DelayRemaining > 0.0)
                {
                    e.DelayRemaining -= battleDeltaTime;
                    if (e.DelayRemaining > 0.0) { active[i] = e; continue; }

                    // 지연이 끝난 순간 즉발 효과는 여기서 소모된다.
                    if (e.Kind == TalismanEffect.Execute)
                    {
                        pendingExecute = Combine(pendingExecute, e.Magnitude);
                        active.RemoveAt(i);
                        continue;
                    }
                    e.DelayRemaining = 0.0;

                    // 만숙의 기준점은 '소환한 순간'이 아니라 '실제로 깔린 순간'이다.
                    // 여기서 다시 찍지 않으면 뒤(Back) 배치의 2.5초 대기까지
                    // 익은 시간으로 세어 배치가 공짜 이득이 된다.
                    e.BornAt = battleTime;
                }

                e.Remaining -= battleDeltaTime;
                if (e.Remaining <= 0.0) active.RemoveAt(i);
                else active[i] = e;
            }

            // 자동 축(장승)은 AutoSummon 구매 여부와 무관하게 스스로 발동한다.
            // 이게 이 부적의 존재 이유다 — 안 누르는 유저에게만 값이 나온다.
            // AutoSummon을 산 유저에게는 어차피 전부 자동이라 차이가 없다(실측 +0.61%p).
            for (int i = 0; i < equipped.Count; i++)
                if (equipped[i].IsAuto && equipped[i].IsReady)
                    Summon(i, Lane.Middle, isAuto: true);

            if (AutoSummon) TryAutoSummon();
        }

        /// <summary>
        /// 자동 소환. 준비된 것을 슬롯 순서대로 전부 쓴다.
        ///
        /// ★ 예전에는 "효과가 돌고 있으면 아낀다"며 활성 효과가 있으면 즉시 반환했다.
        ///   의도는 절약이었는데 결과는 **자동이 두 부적을 절대 겹쳐 쓰지 못하는 것**이었다.
        ///   겹쳐야만 값이 나오는 메타 4종(증폭·복제·쿨감·연장)이 통째로 죽었고,
        ///   구슬 1,500을 내고 자동화를 산 유저가 조합을 바꿔도 결과가 거의 안 변했다.
        ///   유료 기능이 이 게임의 핵심 콘텐츠와 단절돼 있었던 셈이다.
        ///
        ///   실측(56조합 모델, 기준선 14.21분):
        ///     최선 조합에서 자동 11.9% → 그 한 줄만 지워도 26.1% (수동 판단은 45.2%)
        ///     의도한 레버(AutoEfficiency 0.75)의 몫은 3.7~4.9%p뿐이었고
        ///     의도치 않은 그 한 줄의 몫이 7.4~14.2%p였다.
        ///
        /// "자동은 수동보다 불리하다"는 원칙은 유지된다. 다만 그 불리함은
        /// AutoEfficiency라는 '의도한 레버'가 만들어야 한다.
        /// 배치를 못 고르고 순서를 못 짜는 것만으로도 자동은 이미 충분히 불리하다.
        /// </summary>
        private void TryAutoSummon()
        {
            for (int i = 0; i < equipped.Count; i++)
                if (equipped[i].IsReady)
                    Summon(i, Lane.Middle, isAuto: true);
        }

        // ─────────────────────────────────────────
        // 결과

        /// <summary>
        /// 현재 전투력 배수. BattleRunner의 TalismanMultiplier에 넣는다.
        ///
        /// Amplify는 곱셈 항이 아니라 '다른 효과의 초과분을 키우는' 항이다.
        ///   Damage 1.5 + Amplify 0.5  →  1 + 0.5 x 1.5 = 1.75
        /// 이렇게 두면 Amplify 혼자서는 아무 값도 만들지 못한다.
        /// </summary>
        /// <summary>
        /// 웨이브 체력 비율을 모르는 호출자를 위한 다리.
        /// 조건부(Conditional) 효과는 비율 1.0(만체력)으로 계산된다 — 가장 약한 값이다.
        /// 표시용에는 충분하지만 전투 계산에는 DamageMultiplierAt를 쓸 것.
        /// </summary>
        public double CurrentDamageMultiplier => DamageMultiplierAt(1.0);

        /// <summary>
        /// 현재 전투력 배수. BattleRunner의 TalismanMultiplier에 넣는다.
        ///
        /// Amplify는 곱셈 항이 아니라 '다른 효과의 초과분을 키우는' 항이다.
        ///   Damage 1.5 + Amplify 0.5  →  1 + 0.5 x 1.5 = 1.75
        /// 이렇게 두면 Amplify 혼자서는 아무 값도 만들지 못한다.
        /// </summary>
        /// <param name="waveHpRatio">
        /// 현재 웨이브의 잔여 체력 비율(0~1). 조건부 효과가 이 값을 본다.
        /// </param>
        public double DamageMultiplierAt(double waveHpRatio)
        {
            double r = waveHpRatio;
            if (r < 0.0) r = 0.0;
            else if (r > 1.0) r = 1.0;

            double amplify = 0.0;
            for (int i = 0; i < active.Count; i++)
                if (active[i].IsLive && active[i].Kind == TalismanEffect.Amplify)
                    amplify += active[i].Magnitude;

            double m = 1.0;
            for (int i = 0; i < active.Count; i++)
            {
                var e = active[i];
                if (!e.IsLive) continue;
                if (e.Kind != TalismanEffect.Damage) continue;
                m *= 1.0 + (EffectiveMagnitude(e, r) - 1.0) * (1.0 + amplify);
            }
            return m;
        }

        /// <summary>
        /// 효과 하나의 '지금 이 순간' 배수.
        ///
        /// 만숙은 시간이, 조건부는 웨이브 체력이 값을 바꾼다.
        /// 둘 다 아니면 선언된 Magnitude 그대로다.
        /// </summary>
        private double EffectiveMagnitude(ActiveEffect e, double waveHpRatio)
        {
            double m = e.Magnitude;
            if (e.Grow > 0.0) m = 1.0 + e.Grow * (battleTime - e.BornAt);
            if (e.Cond > 0.0) m = 1.0 + (m - 1.0) * (1.0 + e.Cond * (1.0 - waveHpRatio));
            return m;
        }

        /// <summary>
        /// 쌓인 즉시 삭제 비율을 꺼내고 0으로 되돌린다.
        /// 호출자는 이 값을 BattleRunner.ExecuteFraction에 그대로 넘긴다.
        ///
        /// 이벤트가 아니라 폴링인 이유:
        ///   이벤트로 만들면 구독 시점과 Tick 순서에 따라 같은 삭제가
        ///   두 번 적용되거나 한 번도 적용되지 않을 수 있다.
        ///   "꺼내면 사라진다"가 중복 적용을 구조적으로 막는다.
        /// </summary>
        public double ConsumeExecuteFraction()
        {
            double v = pendingExecute;
            pendingExecute = 0.0;
            return v;
        }

        /// <summary>발동 중인(지연이 끝난) 효과 수.</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < active.Count; i++) if (active[i].IsLive) n++;
                return n;
            }
        }

        /// <summary>지연 대기 중인 효과 수. UI가 "곧 터짐"을 보여줄 때 쓴다.</summary>
        public int PendingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < active.Count; i++) if (active[i].DelayRemaining > 0.0) n++;
                return n;
            }
        }

        /// <summary>런이 끝나면 발동 중인 효과는 사라진다. 쿨타임은 유지.</summary>
        public void ClearActive()
        {
            active.Clear();
            pendingExecute = 0.0;
        }

        /// <summary>환생 시 쿨타임까지 초기화. 새 런을 부담 없이 시작하게 한다.</summary>
        public void ResetAll()
        {
            // 누적 축(구미호)의 스택도 함께 초기화한다.
            // 안 지우면 환생을 반복할수록 시작 배수가 계속 올라간다.
            for (int i = 0; i < equipped.Count; i++) equipped[i].Stacks = 0;

            ClearActive();
            cooldowns.Clear();
            foreach (var t in equipped) t.CooldownRemaining = 0;
        }

        private bool IsEquipped(string id)
        {
            for (int i = 0; i < equipped.Count; i++)
                if (equipped[i].Id == id) return true;
            return false;
        }

        /// <summary>기억 중인 쿨타임. 장착 여부와 무관하다. 테스트·UI용.</summary>
        public double CooldownOf(string id)
            => cooldowns.TryGetValue(id, out double v) ? v : 0.0;
    }
}

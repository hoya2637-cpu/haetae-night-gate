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

            public bool IsLive => DelayRemaining <= 0.0 && Remaining > 0.0;
        }

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

        /// <summary>소환. 성공하면 true. 실패 사유: 슬롯 범위 밖, 쿨타임.</summary>
        public bool Summon(int slotIndex, Lane lane, bool isAuto = false)
        {
            if (slotIndex < 0 || slotIndex >= equipped.Count) return false;
            var t = equipped[slotIndex];
            if (!t.IsReady) return false;

            double delay = LaneDelay(lane);
            double duration = t.BaseDuration * LaneDurationScale(lane);
            double magnitude = t.Magnitude;

            // 자동 소환은 위치를 못 고르므로 효율이 낮다.
            // 이 차이가 유저가 직접 조작할 이유를 만든다.
            if (isAuto) magnitude = Damp(t.Effect, magnitude, AutoEfficiency);

            switch (t.Effect)
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
                    break;

                case TalismanEffect.Extend:
                    // 이미 발동 중이거나 지연 대기 중인 효과의 지속을 늘린다.
                    // 아무것도 안 돌고 있으면 아무 일도 일어나지 않는다 — 조합 부적이다.
                    for (int i = 0; i < active.Count; i++)
                    {
                        var e = active[i];
                        if (e.Kind == TalismanEffect.Execute) continue;
                        if (double.IsPositiveInfinity(e.Remaining)) continue;
                        e.Remaining += magnitude;
                        active[i] = e;
                    }
                    break;

                case TalismanEffect.Duplicate:
                    // 발동 중인 '다른' 효과 중 가장 최근 것을 복제한다.
                    // 혼자 쓰면 아무 효과도 없다. 이 부적이 조합을 조합답게 만든다.
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
                    }
                    break;

                default:   // Damage, Amplify
                    for (int c = 0; c < Math.Max(1, t.Copies); c++)
                    {
                        active.Add(new ActiveEffect
                        {
                            Kind = t.Effect,
                            DelayRemaining = delay,
                            Remaining = duration,
                            Magnitude = magnitude,
                            SourceSlot = slotIndex,
                        });
                    }
                    break;
            }

            t.CooldownRemaining = t.Cooldown;
            cooldowns[t.Id] = t.Cooldown;
            OnSummoned?.Invoke(t, lane);
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
                case TalismanEffect.Damage:
                case TalismanEffect.Duplicate:
                    return 1.0 + (magnitude - 1.0) * efficiency;
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
        public void Tick(double realDeltaTime, double battleDeltaTime)
        {
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
                }

                e.Remaining -= battleDeltaTime;
                if (e.Remaining <= 0.0) active.RemoveAt(i);
                else active[i] = e;
            }

            if (AutoSummon) TryAutoSummon();
        }

        private void TryAutoSummon()
        {
            // 이미 효과가 돌고 있으면 아끼고, 없으면 준비된 것 중 하나를 쓴다.
            if (active.Count > 0) return;
            for (int i = 0; i < equipped.Count; i++)
            {
                if (equipped[i].IsReady)
                {
                    Summon(i, Lane.Middle, isAuto: true);
                    return;
                }
            }
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
        public double CurrentDamageMultiplier
        {
            get
            {
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
                    m *= 1.0 + (e.Magnitude - 1.0) * (1.0 + amplify);
                }
                return m;
            }
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

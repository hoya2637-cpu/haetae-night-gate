using System;
using System.Collections.Generic;
using IdleDefense.Core;
using IdleDefense.Data;

namespace IdleDefense.Economy
{
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
    /// 커브를 지키는 두 장치:
    ///   1. 소환 유닛이 준 피해에는 코인이 붙지 않는다.
    ///      (붙으면 "부적 많이 쓰면 부자"가 되어 경제가 무너진다)
    ///   2. 쿨타임은 실시간으로만 흐른다.
    ///      (배속에 비례하면 배속권으로 부적을 남발할 수 있다)
    /// </summary>
    public class TalismanSystem
    {
        /// <summary>
        /// 소환 위치. 앞에 놓으면 먼저 막지만 먼저 사라지고,
        /// 뒤에 놓으면 늦게 개입하지만 오래 버틴다.
        /// </summary>
        public enum Lane
        {
            Front = 0,   // 지속 짧음, 즉시 개입
            Middle = 1,
            Back = 2     // 지속 김, 늦게 개입
        }

        [Serializable]
        public class Talisman
        {
            public string Id;
            public string DisplayName;

            /// <summary>DPS에 곱해지는 배수. 1.0이면 효과 없음.</summary>
            public double DamageMultiplier = 1.3;

            /// <summary>기본 지속시간(초). 배치 위치에 따라 조정된다.</summary>
            public double BaseDuration = 8.0;

            /// <summary>쿨타임(초). 실시간으로만 감소한다.</summary>
            public double Cooldown = 45.0;

            /// <summary>남은 쿨타임.</summary>
            public double CooldownRemaining;

            public bool IsReady => CooldownRemaining <= 0;
        }

        private readonly EconomyConfig cfg;
        private readonly List<Talisman> equipped = new List<Talisman>();

        /// <summary>현재 발동 중인 효과들. (남은시간, 배수)</summary>
        private readonly List<(double remaining, double multiplier)> active
            = new List<(double, double)>();

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

        public void Equip(Talisman t)
        {
            if (t == null || equipped.Count >= MaxSlots) return;
            equipped.Add(t);
        }

        public const int MaxSlots = 5;

        // ─────────────────────────────────────────

        /// <summary>
        /// 배치 위치별 지속시간 배수.
        /// 앞은 짧고 뒤는 길다. 대신 앞은 즉시 효과가 나타난다.
        /// </summary>
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

        /// <summary>배치 위치별 효과 발동 지연(초).</summary>
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

        /// <summary>
        /// 소환. 성공하면 true.
        /// 실패 사유: 쿨타임, 미장착.
        /// </summary>
        public bool Summon(int slotIndex, Lane lane, bool isAuto = false)
        {
            if (slotIndex < 0 || slotIndex >= equipped.Count) return false;
            var t = equipped[slotIndex];
            if (!t.IsReady) return false;

            double duration = t.BaseDuration * LaneDurationScale(lane);
            double mult = t.DamageMultiplier;

            // 자동 소환은 위치를 못 고르므로 효율이 낮다.
            // 이 차이가 유저가 직접 조작할 이유를 만든다.
            if (isAuto) mult = 1.0 + (mult - 1.0) * AutoEfficiency;

            active.Add((duration, mult));
            t.CooldownRemaining = t.Cooldown;

            OnSummoned?.Invoke(t, lane);
            return true;
        }

        /// <summary>
        /// 시간 진행.
        ///
        /// realDeltaTime — 실제 경과 시간. 쿨타임은 이걸로만 줄인다.
        /// battleDeltaTime — 배속이 적용된 전투 시간. 지속시간은 이걸로 줄인다.
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
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                double rem = active[i].remaining - battleDeltaTime;
                if (rem <= 0) active.RemoveAt(i);
                else active[i] = (rem, active[i].multiplier);
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

        /// <summary>
        /// 현재 전투력 배수. 여러 부적이 겹치면 곱해진다.
        /// BattleRunner의 DPS에 이 값을 곱해 쓴다.
        /// </summary>
        public double CurrentDamageMultiplier
        {
            get
            {
                double m = 1.0;
                for (int i = 0; i < active.Count; i++) m *= active[i].multiplier;
                return m;
            }
        }

        public int ActiveCount => active.Count;

        /// <summary>런이 끝나면 발동 중인 효과는 사라진다. 쿨타임은 유지.</summary>
        public void ClearActive() => active.Clear();

        /// <summary>환생 시 쿨타임까지 초기화. 새 런을 부담 없이 시작하게 한다.</summary>
        public void ResetAll()
        {
            active.Clear();
            foreach (var t in equipped) t.CooldownRemaining = 0;
        }
    }
}

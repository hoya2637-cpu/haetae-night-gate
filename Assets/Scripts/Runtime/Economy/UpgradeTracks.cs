using System;
using IdleDefense.Core;
using IdleDefense.Data;

namespace IdleDefense.Economy
{
    /// <summary>
    /// 오방색 5트랙 업그레이드.
    ///
    /// 청 靑 공격력 / 적 赤 공격속도 / 황 黃 코인 / 백 白 방어 / 흑 黑 크리티컬
    ///
    /// 전투 기여분(청·적·흑)은 곱연산이다.
    /// 선형 트랙 하나로는 웨이브 70이 한계이므로 다섯을 곱해 고차 성장을 만든다.
    /// </summary>
    public class UpgradeTracks
    {
        private readonly EconomyConfig cfg;
        private readonly int[] levels = new int[EconomyCore.TrackCount];

        public UpgradeTracks(EconomyConfig config, int[] initialLevels = null)
        {
            cfg = config ?? throw new ArgumentNullException(nameof(config));
            if (initialLevels != null)
            {
                int n = Math.Min(initialLevels.Length, EconomyCore.TrackCount);
                Array.Copy(initialLevels, levels, n);
            }
        }

        public int GetLevel(EconomyCore.Track track) => levels[(int)track];

        public int[] Snapshot() => (int[])levels.Clone();

        /// <summary>전체 트랙 레벨 합. UI 표시용.</summary>
        public int TotalLevel
        {
            get
            {
                int sum = 0;
                foreach (int l in levels) sum += l;
                return sum;
            }
        }

        /// <summary>
        /// 다음 레벨 구매 비용.
        /// 트랙별로 따로 세지 않고 전체 합산 레벨을 쓴다.
        /// 이래야 "한 트랙만 몰아주기"가 자동으로 비싸져서
        /// 다섯 트랙에 분산하는 곱연산 빌드가 유리해진다.
        /// </summary>
        public BigNumber NextCost(EconomyCore.Track track)
            => EconomyCore.UpgradeCost(cfg, TotalLevel + 1);

        public bool CanAfford(EconomyCore.Track track, BigNumber coin)
            => coin >= NextCost(track);

        /// <summary>
        /// 한 레벨 구매. 성공하면 지불한 금액을 paidCost로 돌려준다.
        ///
        /// '새 잔액'이 아니라 '실제로 얼마를 썼는지'를 반환하는 이유:
        ///   잔액을 넘기면 호출부가 차액을 역산해 지갑을 덮어쓰게 되고,
        ///   그러면 BattleRunner가 코인을 통제한다는 캡슐화가 무너진다.
        ///   지불액만 넘기면 지갑은 언제나 SpendCoin() 한 곳에서만 줄어든다.
        /// </summary>
        public bool TryBuy(EconomyCore.Track track, BigNumber availableCoin, out BigNumber paidCost)
        {
            paidCost = NextCost(track);
            if (availableCoin < paidCost)
            {
                paidCost = BigNumber.Zero;
                return false;
            }
            levels[(int)track]++;
            return true;
        }

        /// <summary>
        /// 살 수 있는 만큼 반복 구매. 총 지불액을 totalCost로 돌려준다.
        /// maxCount로 한 번에 사는 양을 제한한다 (프레임 튐 방지).
        /// </summary>
        public int BuyMax(EconomyCore.Track track, BigNumber availableCoin,
                          out BigNumber totalCost, int maxCount = 100)
        {
            totalCost = BigNumber.Zero;
            int bought = 0;
            var remaining = availableCoin;

            while (bought < maxCount && TryBuy(track, remaining, out var cost))
            {
                remaining -= cost;
                totalCost += cost;
                bought++;
            }
            return bought;
        }

        /// <summary>
        /// 가장 효율이 좋은 트랙을 하나 구매한다.
        /// 자동 업그레이드의 기본 정책이며, 곱연산이므로
        /// 가장 뒤처진 전투 트랙을 올리는 것이 대체로 최적이다.
        /// </summary>
        public bool BuyBest(BigNumber availableCoin, out BigNumber paidCost)
        {
            paidCost = BigNumber.Zero;
            var candidates = new[]
            {
                EconomyCore.Track.Blue,
                EconomyCore.Track.Red,
                EconomyCore.Track.Black,
                EconomyCore.Track.Yellow,
            };

            EconomyCore.Track best = candidates[0];
            double bestGain = 0;

            foreach (var t in candidates)
            {
                // 이 트랙을 한 레벨 올렸을 때 전투력 증가율
                double before = EconomyCore.TrackMultiplier(t, levels[(int)t]);
                double after = EconomyCore.TrackMultiplier(t, levels[(int)t] + 1);
                double gain = after / before;
                if (gain > bestGain) { bestGain = gain; best = t; }
            }

            return TryBuy(best, availableCoin, out paidCost);
        }

        // ── 배수 ──

        public double CombatMultiplier => EconomyCore.CombatMultiplier(levels);
        public double CoinMultiplier => EconomyCore.CoinTrackMultiplier(levels);
        public double DefenseMultiplier => EconomyCore.DefenseMultiplier(levels);

        /// <summary>환생 시 트랙 레벨은 전부 초기화된다. 영구 성장은 코어가 담당한다.</summary>
        public void ResetForRebirth()
        {
            Array.Clear(levels, 0, levels.Length);
        }

        // ── 표시용 ──

        public static string TrackName(EconomyCore.Track t)
        {
            switch (t)
            {
                case EconomyCore.Track.Blue: return "청";
                case EconomyCore.Track.Red: return "적";
                case EconomyCore.Track.Yellow: return "황";
                case EconomyCore.Track.White: return "백";
                case EconomyCore.Track.Black: return "흑";
                default: return "?";
            }
        }

        /// <summary>단청 오방색 HEX. 아트기준문서와 일치시킬 것.</summary>
        public static string TrackColor(EconomyCore.Track t)
        {
            switch (t)
            {
                case EconomyCore.Track.Blue: return "#2E6F9E";
                case EconomyCore.Track.Red: return "#C0392B";
                case EconomyCore.Track.Yellow: return "#E4B34A";
                case EconomyCore.Track.White: return "#F0EDE4";
                case EconomyCore.Track.Black: return "#2B2B33";
                default: return "#888888";
            }
        }
    }
}

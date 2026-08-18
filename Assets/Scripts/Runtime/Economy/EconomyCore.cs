using System;
using IdleDefense.Core;
using IdleDefense.Data;

namespace IdleDefense.Economy
{
    /// <summary>
    /// 경제 계산의 순수 함수 모음. MonoBehaviour에 의존하지 않으므로
    /// 단위 테스트에서 그대로 호출해 스프레드시트와 대조할 수 있다.
    ///
    /// 4층 구조:
    ///   1층 웨이브 · 코인 곡선
    ///   2층 환생 메타 (티어 승천)
    ///   3층 다중 업그레이드 트랙 (오방색 곱연산)
    ///   4층 오프라인 보상
    /// </summary>
    public static class EconomyCore
    {
        // ─────────────────────────────────────────
        // 1층 · 웨이브와 코인
        // ─────────────────────────────────────────

        /// <summary>웨이브 n의 적 1마리 체력.</summary>
        public static BigNumber EnemyHp(EconomyConfig c, int wave)
            => new BigNumber(c.baseEnemyHp) * BigNumber.PowBase(c.enemyHpGrowth, wave - 1);

        /// <summary>웨이브 n의 총 체력 (적 수 포함).</summary>
        public static BigNumber WaveTotalHp(EconomyConfig c, int wave)
            => EnemyHp(c, wave) * c.enemiesPerWave;

        /// <summary>웨이브 n을 클리어하며 얻는 코인 (배수 미적용).</summary>
        public static BigNumber WaveCoinReward(EconomyConfig c, int wave)
            => new BigNumber(c.baseCoinReward)
             * BigNumber.PowBase(c.coinGrowth, wave - 1)
             * c.enemiesPerWave;

        /// <summary>
        /// 웨이브 1..n 누적 코인. 등비수열 합의 닫힌 형태를 쓴다.
        /// S = a(r^n - 1)/(r - 1)
        /// </summary>
        public static BigNumber CumulativeCoin(EconomyConfig c, int wave, double coinMultiplier = 1.0)
        {
            if (wave <= 0) return BigNumber.Zero;
            var numerator = BigNumber.PowBase(c.coinGrowth, wave) - BigNumber.One;
            return new BigNumber(c.baseCoinReward * c.enemiesPerWave * coinMultiplier)
                 * numerator / new BigNumber(c.coinGrowth - 1.0);
        }

        /// <summary>
        /// 누적 코인의 역산 — 이만큼의 코인이면 몇 웨이브까지의 수확에 해당하는가.
        /// 오프라인 보상을 '시작 웨이브'로 환산할 때 사용한다.
        /// </summary>
        public static double WaveFromCumulativeCoin(EconomyConfig c, BigNumber coin, double coinMultiplier = 1.0)
        {
            if (coin.IsZero || !coin.IsPositive) return 0.0;
            var inner = coin * new BigNumber(c.coinGrowth - 1.0)
                      / new BigNumber(c.baseCoinReward * c.enemiesPerWave * coinMultiplier)
                      + BigNumber.One;
            return inner.Log10() / Math.Log10(c.coinGrowth);
        }

        // ─────────────────────────────────────────
        // 업그레이드
        // ─────────────────────────────────────────

        /// <summary>레벨 L을 구매하는 비용 (L은 1부터).</summary>
        public static BigNumber UpgradeCost(EconomyConfig c, int level)
            => new BigNumber(c.upgradeBaseCost) * BigNumber.PowBase(c.upgradeCostGrowth, level - 1);

        /// <summary>레벨 1..L을 전부 사는 데 드는 누적 비용.</summary>
        public static BigNumber UpgradeCumulativeCost(EconomyConfig c, int level)
        {
            if (level <= 0) return BigNumber.Zero;
            var numerator = BigNumber.PowBase(c.upgradeCostGrowth, level) - BigNumber.One;
            return new BigNumber(c.upgradeBaseCost) * numerator
                 / new BigNumber(c.upgradeCostGrowth - 1.0);
        }

        /// <summary>
        /// 주어진 코인으로 살 수 있는 최대 업그레이드 레벨.
        /// 누적 비용의 역산이다.
        /// </summary>
        public static int AffordableLevel(EconomyConfig c, BigNumber coin)
        {
            if (coin.IsZero || !coin.IsPositive) return 0;
            var inner = coin * new BigNumber(c.upgradeCostGrowth - 1.0)
                      / new BigNumber(c.upgradeBaseCost)
                      + BigNumber.One;
            double lv = inner.Log10() / Math.Log10(c.upgradeCostGrowth);
            return lv < 0 ? 0 : (int)Math.Floor(lv);
        }

        /// <summary>업그레이드 레벨에 따른 기본 DPS (배수 미적용, 선형 증가).</summary>
        public static BigNumber BaseDpsAtLevel(EconomyConfig c, int level)
            => new BigNumber(c.baseDps + level * c.dpsPerLevel);

        // ─────────────────────────────────────────
        // 2층 · 환생 메타
        // ─────────────────────────────────────────

        /// <summary>
        /// 회차 k에서 도달하는 웨이브.
        /// 설계 곡선: W = coefficient x (k+1)^exponent
        /// </summary>
        public static int TargetWave(EconomyConfig c, int runIndex)
            => (int)Math.Round(c.waveCoefficient * Math.Pow(runIndex + 1, c.waveExponent),
                               MidpointRounding.AwayFromZero);

        /// <summary>도달 웨이브에서 얻는 코어(도깨비불) 수. 감쇠 미적용.</summary>
        public static double CoreGain(EconomyConfig c, int wave)
            => Math.Pow(wave / 10.0, c.coreGainExponent);

        /// <summary>
        /// 오늘 몇 번째 환생인지에 따른 코어 감쇠 배율.
        ///
        /// 왜 필요한가 — 이게 없으면 게임이 무너진다:
        ///   벽은 '웨이브 클리어 시간'으로 판정하므로 시간만 쓰면 계속 환생할 수 있다.
        ///   앱을 8시간 켜두면 런이 48번 돌고, 자동 환생을 켜면 하루 144번이 된다.
        ///   설계 가정은 하루 3회이므로 48배 차이이며,
        ///   실제로 90일 커브가 7일 만에 소진되는 것을 시뮬레이션으로 확인했다.
        ///
        ///   스태미나 같은 하드 제한 대신 감쇠를 쓰는 이유:
        ///   많이 하는 유저를 막지 않되 이득만 급감시킨다.
        ///   "더 해도 되지만 별 소용 없다"가 "더 못 한다"보다 덜 답답하다.
        /// </summary>
        /// <param name="runsToday">오늘 완료한 환생 횟수 (이번 것 포함, 1부터)</param>
        public static double CoreDecayFactor(EconomyConfig c, int runsToday)
        {
            if (runsToday <= c.coreDailySoftCap) return 1.0;
            int over = runsToday - c.coreDailySoftCap;
            double f = Math.Pow(c.coreDecayPerRun, over);
            return Math.Max(f, c.coreDecayFloor);
        }

        /// <summary>
        /// 하루에 얻을 수 있는 코어의 이론적 상한 (런 1회분의 배수).
        ///
        /// 등비급수 합이라 아무리 많이 돌려도 이 값을 넘지 못한다.
        ///   소프트캡 6, 감쇠 0.55 → 6 + 0.55/(1-0.55) = 약 7.2회분
        /// 하한(floor)이 0보다 크면 이 수렴이 깨지므로 반드시 0이어야 한다.
        /// </summary>
        public static double MaxDailyCoreRuns(EconomyConfig c)
        {
            if (c.coreDecayFloor > 0) return double.PositiveInfinity;
            return c.coreDailySoftCap + c.coreDecayPerRun / (1.0 - c.coreDecayPerRun);
        }

        /// <summary>감쇠가 적용된 실제 코어 획득량.</summary>
        public static double CoreGainWithDecay(EconomyConfig c, int wave, int runsToday)
            => CoreGain(c, wave) * CoreDecayFactor(c, runsToday);

        /// <summary>승천 배수. tier는 1부터 시작.</summary>
        public static double TierMultiplier(EconomyConfig c, int tier)
            => Math.Pow(c.tierMultiplier, tier - 1);

        /// <summary>공격 배수 = (1 + 코어 x 계수) x 티어 배수.</summary>
        public static double AttackMultiplier(EconomyConfig c, double cores, int tier)
            => (1.0 + cores * c.coreAttackCoeff) * TierMultiplier(c, tier);

        /// <summary>코인 배수. 공격보다 완만하게 오른다.</summary>
        public static double CoinMultiplier(EconomyConfig c, double cores, int tier)
            => (1.0 + cores * c.coreCoinCoeff) * Math.Pow(TierMultiplier(c, tier), 0.6);

        /// <summary>현재 티어에서 승천에 필요한 웨이브. 최대 티어면 int.MaxValue.</summary>
        public static int NextTierGate(EconomyConfig c, int currentTier)
        {
            int idx = currentTier - 1;
            if (c.tierGates == null || idx < 0 || idx >= c.tierGates.Length) return int.MaxValue;
            return c.tierGates[idx];
        }

        /// <summary>현재 티어에서 승천에 필요한 누적 코어.</summary>
        public static double NextTierCoreGate(EconomyConfig c, int currentTier)
        {
            int idx = currentTier - 1;
            if (c.tierCoreGates == null || idx < 0 || idx >= c.tierCoreGates.Length)
                return double.PositiveInfinity;
            return c.tierCoreGates[idx];
        }

        /// <summary>
        /// 승천 가능 여부. 웨이브와 코어를 둘 다 만족해야 한다.
        ///
        /// 웨이브 단독 조건이었을 때의 문제:
        ///   티어가 오르면 공격 배수가 2.5배 뛰고, 그게 웨이브를 밀어올려
        ///   다음 게이트를 바로 통과시킨다. 이 되먹임 때문에
        ///   자동 환생을 켜면 90일 콘텐츠가 7일 만에 소진되는 것을 확인했다.
        ///
        ///   코어에는 일일 감쇠(하루 약 7.2런분)가 걸려 있으므로,
        ///   코어를 조건에 넣으면 아무리 돌려도 승천 속도에 상한이 생긴다.
        /// </summary>
        public static bool CanAscend(EconomyConfig c, int currentTier, int reachedWave, double cores)
            => reachedWave >= NextTierGate(c, currentTier)
            && cores >= NextTierCoreGate(c, currentTier);

        /// <summary>승천까지 부족한 것이 무엇인지. UI 안내용.</summary>
        public static (bool waveOk, bool coreOk) AscendProgress(
            EconomyConfig c, int currentTier, int reachedWave, double cores)
            => (reachedWave >= NextTierGate(c, currentTier),
                cores >= NextTierCoreGate(c, currentTier));

        /// <summary>승천 후 남는 코어.</summary>
        public static double CoresAfterAscend(EconomyConfig c, double cores)
            => cores * c.coreRetainOnAscend;

        // ─────────────────────────────────────────
        // 3층 · 오방색 5트랙 (곱연산)
        // ─────────────────────────────────────────

        /// <summary>
        /// 오방색 트랙. 선형 트랙 하나로는 웨이브 70이 한계이므로
        /// 다섯을 곱해서 고차 성장을 만든다.
        /// </summary>
        public enum Track
        {
            Blue = 0,   // 청 靑 — 공격력
            Red = 1,    // 적 赤 — 공격 속도
            Yellow = 2, // 황 黃 — 코인 획득
            White = 3,  // 백 白 — 체력 · 방어
            Black = 4   // 흑 黑 — 크리티컬
        }

        public const int TrackCount = 5;

        /// <summary>트랙 레벨당 증가 계수. 트랙마다 성격이 다르다.</summary>
        public static double TrackCoefficient(Track t)
        {
            switch (t)
            {
                case Track.Blue: return 0.10;   // 공격력 - 주력
                case Track.Red: return 0.06;    // 공속
                case Track.Yellow: return 0.05; // 코인
                case Track.White: return 0.04;  // 방어
                case Track.Black: return 0.03;  // 크리 - 가장 완만
                default: return 0.05;
            }
        }

        /// <summary>단일 트랙의 배수 (1 + 레벨 x 계수).</summary>
        public static double TrackMultiplier(Track t, int level)
            => 1.0 + level * TrackCoefficient(t);

        /// <summary>
        /// 전투력에 기여하는 트랙들의 곱.
        /// 청(공격력) x 적(공속) x 흑(크리)만 DPS에 곱해진다.
        /// 황은 코인, 백은 생존이라 별도로 쓴다.
        /// </summary>
        public static double CombatMultiplier(int[] trackLevels)
        {
            if (trackLevels == null || trackLevels.Length < TrackCount) return 1.0;
            return TrackMultiplier(Track.Blue, trackLevels[(int)Track.Blue])
                 * TrackMultiplier(Track.Red, trackLevels[(int)Track.Red])
                 * TrackMultiplier(Track.Black, trackLevels[(int)Track.Black]);
        }

        public static double CoinTrackMultiplier(int[] trackLevels)
            => trackLevels == null || trackLevels.Length < TrackCount
             ? 1.0
             : TrackMultiplier(Track.Yellow, trackLevels[(int)Track.Yellow]);

        public static double DefenseMultiplier(int[] trackLevels)
            => trackLevels == null || trackLevels.Length < TrackCount
             ? 1.0
             : TrackMultiplier(Track.White, trackLevels[(int)Track.White]);

        // ─────────────────────────────────────────
        // 전투 판정
        // ─────────────────────────────────────────

        /// <summary>웨이브를 클리어하는 데 걸리는 시간(초).</summary>
        public static double WaveClearSeconds(EconomyConfig c, int wave, BigNumber dps)
        {
            if (dps.IsZero || !dps.IsPositive) return double.PositiveInfinity;
            return (WaveTotalHp(c, wave) / dps).ToDouble();
        }

        /// <summary>이 웨이브가 '벽'인가 (제한 시간 초과).</summary>
        public static bool IsWall(EconomyConfig c, int wave, BigNumber dps)
            => WaveClearSeconds(c, wave, dps) > c.waveTimeWall;

        // ─────────────────────────────────────────
        // 4층 · 오프라인 보상
        // ─────────────────────────────────────────

        /// <summary>
        /// 오프라인 보상 결과.
        /// 코인과 젬만 포함한다. 코어(도깨비불)는 절대 포함하지 않는다.
        /// </summary>
        public struct OfflineReward
        {
            public BigNumber Coin;
            public int Gems;
            public double CreditedHours;
            public double AppliedRatio;
            /// <summary>이 보상으로 즉시 도달 가능한 웨이브. 항상 직전 도달 웨이브 미만이어야 한다.</summary>
            public double StartWave;
        }

        /// <summary>
        /// 오프라인 보상 계산.
        ///
        /// 철칙: 코인과 젬만 지급한다. 코어를 주면 90일 커브가 즉시 붕괴한다.
        /// 보상 기준은 '시간'이 아니라 '직전 런에서 번 코인'이다.
        /// 시간 기준으로 주면 유저가 자기 벽보다 높은 웨이브에서 시작하게 되어
        /// 게임이 성립하지 않는다.
        /// </summary>
        /// <summary>
        /// 주어진 코인으로 업그레이드를 사고 났을 때, 벽에 걸리지 않고 깰 수 있는 최대 웨이브.
        ///
        /// 오프라인 시작 웨이브의 상한으로 쓴다. 승천 전용 예외가 아니라 일반 계약이다.
        /// 코어 소각·업그레이드 초기화 등 전투력이 떨어지는 어떤 경우에도
        /// "지금 실력으로 못 넘는 웨이브에 떨어뜨리지 않는다"를 보장한다.
        ///
        /// 구매 정책은 게임과 동일한 BuyBest다. 비용 곡선이 트랙 간 공유되므로
        /// (NextCost = UpgradeCost(TotalLevel + 1)) 총 레벨은 코인만으로 결정되고,
        /// 트랙 배분만 BuyBest가 정한다. 따라서 이 추정은 근사가 아니라 실제와 같다.
        /// </summary>
        public static int MaxClearableWave(EconomyConfig c, BigNumber coin, double attackMultiplier)
        {
            if (c == null || attackMultiplier <= 0) return 1;

            var tracks = new UpgradeTracks(c);
            var purse = coin;
            while (tracks.BuyBest(purse, out var cost))
                purse -= cost;

            var dps = BaseDpsAtLevel(c, tracks.TotalLevel)
                    * attackMultiplier * tracks.CombatMultiplier;
            if (!dps.IsPositive) return 1;

            // WaveClearSeconds는 웨이브에 대해 단조 증가하므로 이분 탐색이 성립한다.
            int lo = 1, hi = Math.Max(1, c.maxWavePerRun);
            if (WaveClearSeconds(c, lo, dps) > c.waveTimeWall) return 1;

            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (WaveClearSeconds(c, mid, dps) <= c.waveTimeWall) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        public static OfflineReward CalculateOffline(
            EconomyConfig c,
            double awayHours,
            int lastRunWave,
            double coinMultiplier,
            bool watchedAd,
            double capHoursOverride = -1.0,
            double attackMultiplier = -1.0)
        {
            double cap = capHoursOverride > 0 ? capHoursOverride : c.offlineCapHours;
            double creditedForCoin = Math.Min(Math.Max(awayHours, 0.0), cap);

            // 젬은 확장 상한까지 인정 (수면 보호)
            double creditedForGem = Math.Min(Math.Max(awayHours, 0.0), c.offlineCapHoursMax);

            double ratio = (creditedForCoin / cap) * c.offlineMaxRatio;
            if (watchedAd) ratio *= c.offlineAdMultiplier;
            ratio = Math.Min(ratio, c.offlineRatioCeiling);

            var lastRunTotal = CumulativeCoin(c, lastRunWave, coinMultiplier);
            var coin = lastRunTotal * ratio;

            var result = new OfflineReward
            {
                Coin = coin,
                Gems = (int)Math.Floor(creditedForGem * c.gemsPerHour),
                CreditedHours = creditedForCoin,
                AppliedRatio = ratio,
                StartWave = WaveFromCumulativeCoin(c, coin, coinMultiplier)
            };
            // ── 전투력 상한 ──
            // 시작 웨이브는 "직전에 도달했던 곳"이 아니라
            // "지금 실력으로 넘을 수 있는 곳"을 넘지 않아야 한다.
            // 승천으로 코어가 소각되면 전투력이 떨어지는데, 오프라인 보상은
            // 더 강했던 직전 런 기준이라 못 넘는 웨이브에서 시작하게 된다.
            // (실측: 티어 4 승천 직후 런이 1.36분 만에 종료. docs/P0_계측결과_2차.md 2장)
            //
            // attackMultiplier를 넘기지 않으면 상한을 적용하지 않는다.
            // 게임 경로(GameController)는 반드시 넘긴다.
            if (attackMultiplier > 0)
            {
                int powerCap = MaxClearableWave(c, coin, attackMultiplier);
                if (result.StartWave > powerCap) result.StartWave = powerCap;
            }

            return result;
        }

        /// <summary>
        /// 커브 무결성 검사. 오프라인 보상이 직전 도달 웨이브를 넘지 않아야 한다.
        /// 넘으면 유저가 자기 벽 너머에서 시작하게 되어 게임이 붕괴한다.
        /// </summary>
        public static bool ValidateOfflineIntegrity(EconomyConfig c, int lastRunWave, double coinMultiplier)
        {
            var worstCase = CalculateOffline(c, 999.0, lastRunWave, coinMultiplier, watchedAd: true);
            return worstCase.StartWave < lastRunWave;
        }
    }
}

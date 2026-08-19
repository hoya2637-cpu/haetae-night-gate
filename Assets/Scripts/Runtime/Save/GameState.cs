using System;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Economy;
using IdleDefense.Data;

namespace IdleDefense.Save
{
    /// <summary>
    /// 게임 진행 상태. 세이브 대상 전부를 담는다.
    /// BigNumber는 JsonUtility가 직렬화하지 못하므로 문자열로 저장한다.
    /// </summary>
    [Serializable]
    public class GameState
    {
        public int saveVersion = 1;

        [Header("현재 런")]
        public int currentWave = 1;
        public string coinSerialized = "0|0";
        public int[] trackLevels = new int[EconomyCore.TrackCount];

        [Header("영구 진행")]
        public int tier = 1;
        public int runIndex = 0;
        public double cores;          // 도깨비불 - 환생으로만 획득
        public int gems;
        public int bestWave;

        [Tooltip("오늘 완료한 환생 횟수. 코어 일일 감쇠에 쓰인다")]
        public int runsToday = 1;

        [Tooltip("runsToday를 리셋한 날짜 (UTC 기준 일련번호)")]
        public int lastRunDayStamp;

        [Header("오프라인")]
        public long lastSeenUnixSeconds;
        public int lastRunWave = 1;
        public double offlineCapHours = 4.0;

        [Header("구매")]
        // 주의 — 이 값은 클라이언트에서 보호할 수 없다.
        // 세이브를 고치면 광고 제거를 공짜로 켤 수 있고, 범위 검증으로는 막을 방법이 없다
        // (true/false 둘 다 정상값이기 때문이다).
        // 실제 보호는 스토어 영수증 검증(IAP receipt validation)으로만 가능하며,
        // 결제 연동 시 반드시 함께 구현해야 한다.
        public bool adsRemoved;

        /// <summary>
        /// 장착 중인 부적 id. 1군 8종은 고정 지급이므로 '보유'가 아니라 '장착'만 저장한다.
        /// 구버전 세이브에는 이 필드가 없다 — EnsureIntegrity가 기본 로드아웃으로 채운다.
        /// 그래서 saveVersion을 올리지 않아도 안전하다.
        /// </summary>
        public string[] equippedTalismans;

        // ── 의도적으로 저장하지 않는 것 ──
        //
        // 배속 잔여시간(BattleRunner.SpeedBoostRemaining)은 세션 한정이다.
        // 저장하면 광고 한 번으로 얻은 600초를 며칠에 걸쳐 나눠 쓸 수 있고,
        // 그러면 "전투 10분 배속"이 사실상 영구 배속이 된다.
        //
        // 도깨비방망이 지속시간도 같은 이유로 저장하지 않는다.
        // 이 둘을 GameState에 추가하지 말 것.

        // BigNumber 접근자
        public BigNumber Coin
        {
            get => BigNumber.Deserialize(coinSerialized);
            set => coinSerialized = value.Serialize();
        }

        public DateTime LastSeenUtc
        {
            get => DateTimeOffset.FromUnixTimeSeconds(lastSeenUnixSeconds).UtcDateTime;
            set => lastSeenUnixSeconds = new DateTimeOffset(
                DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }

        public static GameState CreateNew()
        {
            var s = new GameState();
            s.LastSeenUtc = DateTime.UtcNow;
            return s;
        }

        /// <summary>
        /// 자리비움 시간(시). 기기 시각 조작에 대비해 음수는 0으로 막는다.
        /// </summary>
        public double AwayHours(DateTime nowUtc)
        {
            double h = (nowUtc - LastSeenUtc).TotalHours;
            if (h < 0)
            {
                Debug.LogWarning("[GameState] 자리비움 시간이 음수입니다. 기기 시각이 되돌려졌을 수 있습니다.");
                return 0;
            }
            return h;
        }

        /// <summary>
        /// 세이브 값의 상·하한을 강제한다.
        ///
        /// 왜 상한도 필요한가:
        ///   체크섬은 '파일이 깨졌는가'만 알려준다.
        ///   체크섬을 다시 계산한 조작 파일은 정상으로 통과하므로,
        ///   값 자체가 말이 되는 범위인지 별도로 봐야 한다.
        ///   특히 tier를 99로 바꾸면 티어 배수가 2.5^98이 되어
        ///   게임이 즉시 붕괴하고 수치가 double 범위를 넘어간다.
        ///
        /// 한계 — 클라이언트 방어의 본질:
        ///   이 검사는 캐주얼한 파일 편집과 '게임이 깨지는 값'을 막는 것이 목적이다.
        ///   루팅 기기에서 메모리를 직접 고치는 것은 클라이언트만으로 막을 수 없다.
        ///   실제 치팅 차단이 필요해지면 서버 검증이 유일한 답이며,
        ///   그때는 이 메서드가 아니라 서버 쪽에 같은 규칙을 두어야 한다.
        /// </summary>
        /// <summary>날짜가 바뀌었으면 일일 환생 카운터를 리셋한다.</summary>
        public void RefreshDailyCounters(DateTime nowUtc)
        {
            int today = (int)(nowUtc.Date - new DateTime(2020, 1, 1)).TotalDays;
            if (today != lastRunDayStamp)
            {
                lastRunDayStamp = today;
                runsToday = 1;
            }
        }

        /// <summary>
        /// 장착 부적 정리. 세이브가 오래됐거나 손상됐을 때 조용히 게임이 망가지지 않게 한다.
        ///
        /// 잡아내는 것:
        ///   - 필드 자체가 없는 구버전 세이브 (null)
        ///   - 삭제된 부적 id (밸런스 패치로 부적을 빼면 실제로 생긴다)
        ///   - 같은 부적 중복 장착 (곱연산이라 중복은 그대로 배수 폭발이 된다)
        ///   - 슬롯 초과
        /// 빈 자리는 기본 로드아웃에서 아직 안 쓴 것으로 채운다.
        /// </summary>
        private void RepairTalismans()
        {
            var fixedList = new System.Collections.Generic.List<string>(TalismanSystem.MaxSlots);
            if (equippedTalismans != null)
            {
                foreach (var id in equippedTalismans)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!TalismanCatalog.Exists(id)) continue;
                    if (fixedList.Contains(id)) continue;
                    fixedList.Add(id);
                    if (fixedList.Count >= TalismanSystem.MaxSlots) break;
                }
            }

            // 빈 경우에만 기본 로드아웃으로 채운다.
            // 무조건 5개로 채우면 유저가 일부러 3개만 끼운 선택을 매 로드마다 되돌려버린다.
            if (fixedList.Count == 0)
                fixedList.AddRange(TalismanCatalog.DefaultLoadout());

            equippedTalismans = fixedList.ToArray();
        }

        public void EnsureIntegrity(EconomyConfig config = null)
        {
            if (trackLevels == null || trackLevels.Length != EconomyCore.TrackCount)
                trackLevels = new int[EconomyCore.TrackCount];

            // 트랙 레벨 — 음수와 비현실적 값 차단
            for (int i = 0; i < trackLevels.Length; i++)
                trackLevels[i] = Clamp(trackLevels[i], 0, MaxTrackLevel);

            // 티어 — 게이트 개수 + 1이 이론상 최대
            int maxTier = config?.tierGates != null ? config.tierGates.Length + 1 : DefaultMaxTier;
            tier = Clamp(tier, 1, maxTier);

            currentWave = Clamp(currentWave, 1, MaxWave);
            lastRunWave = Clamp(lastRunWave, 1, MaxWave);
            bestWave = Clamp(bestWave, 0, MaxWave);
            runIndex = Clamp(runIndex, 0, MaxRunIndex);
            gems = Clamp(gems, 0, MaxGems);
            runsToday = Clamp(runsToday, 1, 100000);

            if (double.IsNaN(cores) || double.IsInfinity(cores) || cores < 0) cores = 0;

            RepairTalismans();
            if (cores > MaxCores) cores = MaxCores;

            double capMax = config?.offlineCapHoursMax ?? DefaultMaxOfflineCap;
            if (double.IsNaN(offlineCapHours) || offlineCapHours <= 0)
                offlineCapHours = config?.offlineCapHours ?? 4.0;
            if (offlineCapHours > capMax) offlineCapHours = capMax;

            // 미래 시각은 자리비움 계산을 망가뜨린다
            long nowUnix = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
            if (lastSeenUnixSeconds <= 0 || lastSeenUnixSeconds > nowUnix)
                LastSeenUtc = DateTime.UtcNow;

            // 코인은 BigNumber 계층에서 이미 NaN/Infinity/지수범위를 막는다
            var coin = Coin;
            if (!coin.IsZero && !coin.IsPositive) Coin = BigNumber.Zero;
        }

        // 상한값의 기준:
        //   "정상 플레이로 수년을 해도 못 넘는 값"이면서
        //   "넘어가면 계산이 깨지는 값"보다는 훨씬 아래.
        //   정상 유저를 절대 건드리지 않으면서 게임이 붕괴하는 것만 막는다.
        //   (90일 기준 실측: 웨이브 210, 코어 46,000, 젬 3,300)
        private const int MaxTrackLevel = 50000;
        private const int DefaultMaxTier = 20;
        private const int MaxWave = 100000;
        private const int MaxRunIndex = 500000;
        private const int MaxGems = 1000000;
        private const double MaxCores = 1e12;
        private const double DefaultMaxOfflineCap = 24.0;

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}

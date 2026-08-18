using UnityEngine;

namespace IdleDefense.Data
{
    /// <summary>
    /// 경제 파라미터. 방치형디펜스_경제시뮬레이션.xlsx '가정' 시트와 1:1 대응한다.
    ///
    /// 중요: 코드에 상수를 직접 박지 말고 전부 여기를 거칠 것.
    /// 스프레드시트 값과 대조 가능해야 한다.
    ///
    /// 경고 - 아래 세 값은 서로 강하게 얽혀 있다. 단독으로 바꾸면 게임이 진행 불가능해진다.
    ///   enemyHpGrowth (1.09), tierMultiplier (2.5), waveExponent (0.269)
    /// 변경 시 반드시 스프레드시트 '환생메타' 시트 O40 진단 패널이 "정상"인지 확인할 것.
    /// </summary>
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "IdleDefense/Economy Config")]
    public class EconomyConfig : ScriptableObject
    {
        [Header("전투 · 적  (가정!B5~B7)")]
        [Tooltip("웨이브 1 적 한 마리의 체력")]
        public double baseEnemyHp = 10.0;

        [Tooltip("웨이브당 체력 배수. 반드시 coinGrowth보다 커야 벽이 생긴다. 위험: 단독 변경 금지")]
        public double enemyHpGrowth = 1.09;

        [Tooltip("웨이브당 적 수")]
        public int enemiesPerWave = 10;

        [Header("재화 · 코인  (가정!B9~B10)")]
        public double baseCoinReward = 2.0;

        [Tooltip("웨이브당 코인 배수. enemyHpGrowth보다 낮아야 한다. 이 격차가 벽을 만든다")]
        public double coinGrowth = 1.075;

        [Header("타워 · 업그레이드  (가정!B12~B15)")]
        public double baseDps = 5.0;
        public double upgradeBaseCost = 10.0;

        [Tooltip("레벨당 비용 배수. 비용은 지수, 파워는 선형 = 방치형의 기본 공식")]
        public double upgradeCostGrowth = 1.12;

        [Tooltip("업그레이드 1레벨당 DPS 증가량 (선형)")]
        public double dpsPerLevel = 3.0;

        [Header("판정 기준  (가정!B17~B18)")]
        [Tooltip("이보다 빠르면 성장 실감이 안 남")]
        public double waveTimeMin = 5.0;

        [Tooltip("이 시간을 넘으면 '벽'. 환생 시점이 된다")]
        public double waveTimeWall = 45.0;

        [Tooltip("한 런의 웨이브 상한. 무한 루프 방지용 안전장치이며 실제로는 벽에서 먼저 끝난다")]
        public int maxWavePerRun = 2000;

        [Header("환생 메타  (가정!B32~B39)")]
        [Tooltip("도달 웨이브 = waveCoefficient x (회차+1)^waveExponent")]
        public double waveCoefficient = 37.7;

        [Tooltip("위험: 단독 변경 금지. 0.40으로 올리면 여유가 0으로 붕괴")]
        public double waveExponent = 0.269;

        [Tooltip("승천 1회당 전체 배수. 위험: 1.5로 낮추면 진행 불가")]
        public double tierMultiplier = 2.5;

        [Tooltip("승천 시 남는 코어 비율. 85%를 소각한다")]
        public double coreRetainOnAscend = 0.15;

        [Tooltip("획득 코어 = (도달웨이브 / 10)^coreGainExponent")]
        public double coreGainExponent = 1.6;

        public double coreAttackCoeff = 0.02;
        public double coreCoinCoeff = 0.015;

        [Tooltip("평균 유저의 하루 런 횟수. 90일 커브 환산에 사용")]
        public double runsPerDay = 3.0;

        [Header("코어 일일 감쇠 (조기 소진 방지)")]
        [Tooltip("하루에 이 횟수까지는 코어를 전액 받는다")]
        public int coreDailySoftCap = 6;

        [Tooltip("소프트캡 초과 시 회차당 곱해지는 감쇠율. " +
                 "0.55면 7번째 55%, 8번째 30%, 9번째 17%... 로 급감한다")]
        public double coreDecayPerRun = 0.55;

        [Tooltip("감쇠의 하한. 0이어야 등비급수가 수렴해 하루 총량에 상한이 생긴다. " +
                 "0.02만 줘도 100회 반복 시 2회분이 추가로 쌓여 커브가 무너진다")]
        public double coreDecayFloor = 0.0;

        [Header("티어 게이트  (가정!B42~B50)")]
        [Tooltip("이 웨이브에 도달하면 승천 가능. 간격이 벌어지는 것이 정상")]
        public int[] tierGates = { 50, 80, 100, 125, 150, 180, 215, 255, 300 };

        // 승천에 필요한 누적 코어. 웨이브 조건과 함께 둘 다 만족해야 한다.
        //
        // 왜 이중 조건인가:
        //   웨이브만 보면 "티어 상승 → 배수 2.5배 → 웨이브 상승 → 다음 게이트 통과"라는
        //   양의 되먹임이 생겨 티어가 연쇄로 뚫린다.
        //   실제로 자동 환생을 켜면 90일 콘텐츠가 7일 만에 소진되는 것을 확인했다.
        //   코어에는 일일 감쇠가 걸려 있으므로, 코어를 조건에 넣으면
        //   아무리 많이 돌려도 승천 속도에 상한이 생긴다.
        [Tooltip("승천에 필요한 누적 코어. tierGates와 길이가 같아야 한다")]
        public double[] tierCoreGates = { 80, 450, 2300, 9000, 22000, 90000, 380000, 1600000, 6800000 };

        [Header("오프라인 보상  (오프라인보상!B6~B10)")]
        [Tooltip("기본 누적 상한(시간). 4시간이면 하루 6회 복귀를 유도")]
        public double offlineCapHours = 4.0;

        [Tooltip("구슬로 개방하는 확장 상한(시간). 수면 보호용")]
        public double offlineCapHoursMax = 12.0;

        [Tooltip("직전 런 총 코인 대비 최대 보상 비율. 40%를 넘기면 액티브 플레이가 죽는다")]
        public double offlineMaxRatio = 0.30;

        [Tooltip("리워드 광고 시청 시 배수")]
        public double offlineAdMultiplier = 2.0;

        [Tooltip("보상 비율 절대 상한. 광고 적용 후에도 이를 넘지 않는다. " +
                 "80%는 여유가 3웨이브밖에 안 남아 위험하다")]
        public double offlineRatioCeiling = 0.60;

        [Tooltip("시간당 구슬 지급량. 25는 과잉 공급이라 90일에 3만 4천 개가 쌓인다. " +
                 "12가 균형점 (하루 288개)")]
        public int gemsPerHour = 12;

        // 구슬은 성장이 아니라 '시간과 편의'를 사는 화폐다.
        // 코인/코어/티어를 구슬로 살 수 있게 하면 경제가 붕괴한다.
        [Header("구슬 소비처  (젬 싱크)")]
        [Tooltip("오프라인 상한 4h→8h 확장 비용")]
        public int gemCostCapTier1 = 1000;

        [Tooltip("오프라인 상한 8h→12h 확장 비용")]
        public int gemCostCapTier2 = 2500;

        [Tooltip("자동화 1종당 해금 비용. 3종을 각각 따로 구매한다 (합계 4,500). " +
                 "일괄 해금이 아닌 이유: 자동 업그레이드는 초반부터 유용하지만, " +
                 "자동 환생은 벽 개념을 이해한 뒤에야 의미가 있고, " +
                 "자동 수령은 오프라인 상한을 늘린 뒤에 가치가 생긴다. " +
                 "한 번에 열면 이 학습 순서가 사라진다.")]
        public int gemCostAutomation = 1500;

        [Tooltip("자동화 해금 종류 수. 자동 업그레이드 / 자동 환생 / 자동 수령")]
        public int automationUnlockCount = 3;

        [Tooltip("반복 소비 부스트 비용. 코인 +50% 30분 등")]
        public int gemCostBoost = 150;

        [Tooltip("리워드 광고로 지급하는 구슬. 0을 권장한다. " +
                 "검증기 기준 안전 상한은 15이며, 30이면 이미 과잉이다.")]
        public int gemsPerAd = 0;

        [Header("유저 행동 가정  (설정이 아니라 검증용 추정치)")]
        [Tooltip("리워드 광고 시청률. '일반' 페르소나 기준")]
        public double assumedAdWatchRate = 0.6;

        [Tooltip("하루 평균 접속 횟수. 구슬 공급량 계산의 전제다. " +
                 "접속 1회당 최대 offlineCapHoursMax 시간까지만 인정되므로, " +
                 "접속이 적은 유저는 하루 24시간을 다 못 받는다. " +
                 "예: 하루 1회 접속 유저는 12시간치(144젬)만 받는다.")]
        public double assumedLoginsPerDay = 3.0;

        [Tooltip("하루 평균 부스트 구매 횟수. 게임 규칙이 아니라 유저 행동 모델값이다. " +
                 "근거: '일반' 페르소나의 접속 3회 x 부스트 성향 0.6 = 1.8회/일. " +
                 "페르소나 모델을 바꾸면 이 값도 함께 맞출 것 " +
                 "(EconomySimulationTests가 불일치를 잡아낸다).")]
        public double assumedBoostsPerDay = 1.8;

        [Tooltip("구슬 잉여 허용 한계 (90일 기준). 이보다 많이 남으면 싱크가 부족한 것")]
        public double gemSurplus90Limit = 5000.0;

        /// <summary>
        /// 설정값의 정합성을 검사한다. 에디터와 부팅 시 호출할 것.
        /// </summary>
        public bool Validate(out string error)
        {
            if (coinGrowth >= enemyHpGrowth)
            {
                error = $"코인 증가율({coinGrowth})이 체력 증가율({enemyHpGrowth}) 이상입니다. " +
                        "벽이 생기지 않아 게임이 무한 진행됩니다.";
                return false;
            }
            if (upgradeCostGrowth <= 1.0)
            {
                error = "업그레이드 비용 증가율은 1.0보다 커야 합니다.";
                return false;
            }
            if (offlineMaxRatio > 0.40)
            {
                error = $"오프라인 보상 비율({offlineMaxRatio:P0})이 40%를 초과합니다. " +
                        "액티브 플레이 유인이 사라집니다.";
                return false;
            }
            // 설계 철학상의 상한은 60%다. 검증 기준도 같은 숫자를 보게 한다.
            // Epsilon을 두는 이유: 0.20 x 3.0 처럼 정확히 0.6이어야 할 곱이
            // 부동소수점에서 0.60000000000000009가 되어 정상 설정이 거부되는 일이 있다.
            const double MaxOfflineRatio = 0.60;
            const double Epsilon = 1e-9;

            if (offlineRatioCeiling > MaxOfflineRatio + Epsilon)
            {
                error = $"오프라인 상한({offlineRatioCeiling:P0})이 60%를 초과합니다. " +
                        "직전 런의 대부분을 광고 한 번으로 회수하게 되어 " +
                        "'오프라인은 액티브 플레이를 대체하지 않는다'는 원칙과 충돌합니다.";
                return false;
            }
            {
                // 광고까지 적용한 실제 최대치를 검사한다.
                // maxRatio 단독으로는 통과해도 광고 배수와 곱하면 위험해질 수 있다.
                double effective = System.Math.Min(offlineMaxRatio * offlineAdMultiplier,
                                                   offlineRatioCeiling);
                if (effective > MaxOfflineRatio + Epsilon)
                {
                    error = $"광고 적용 후 실효 보상률이 {effective:P0}입니다. " +
                            $"(비율 {offlineMaxRatio:P0} x 광고 {offlineAdMultiplier}배) " +
                            "60% 이하로 낮추세요.";
                    return false;
                }
            }
            if (tierGates == null || tierGates.Length == 0)
            {
                error = "티어 게이트가 비어 있습니다.";
                return false;
            }
            for (int i = 1; i < tierGates.Length; i++)
            {
                if (tierGates[i] <= tierGates[i - 1])
                {
                    error = $"티어 게이트가 오름차순이 아닙니다: [{i - 1}]={tierGates[i - 1]}, [{i}]={tierGates[i]}";
                    return false;
                }
            }
            if (tierCoreGates == null || tierCoreGates.Length != tierGates.Length)
            {
                error = $"코어 게이트 개수({tierCoreGates?.Length ?? 0})가 " +
                        $"웨이브 게이트 개수({tierGates.Length})와 다릅니다.";
                return false;
            }
            for (int i = 1; i < tierCoreGates.Length; i++)
            {
                if (tierCoreGates[i] <= tierCoreGates[i - 1])
                {
                    error = $"코어 게이트가 오름차순이 아닙니다: " +
                            $"[{i - 1}]={tierCoreGates[i - 1]}, [{i}]={tierCoreGates[i]}";
                    return false;
                }
            }
            {
                // 구슬 공급과 소비의 균형.
                //
                // 아래 두 값은 '게임 설정'이 아니라 '유저 행동 가정'이다.
                // 값의 근거는 EconomySimulationTests의 4인 페르소나 시뮬레이션이며,
                // 시뮬레이션 결과가 바뀌면 여기가 아니라 그 테스트에서 먼저 갱신할 것.
                // 구슬은 '경과 시간'에 비례하지만 접속 1회당 상한이 걸린다.
                // 따라서 하루 공급은 24시간이 아니라
                // min(24, 접속횟수 x 회당상한) 시간분이다.
                // 이 보정이 없으면 접속이 적은 유저의 공급을 과대평가하게 된다.
                double creditedHours = System.Math.Min(
                    24.0, assumedLoginsPerDay * offlineCapHoursMax);
                // 광고 젬은 '접속할 때마다'가 아니라 '광고를 본 접속에만' 나온다.
                // 시청률을 빼먹으면 gemsPerAd를 0이 아닌 값으로 바꾼 순간
                // Config가 실제보다 많은 공급을 가정하게 된다.
                double dailySupply = creditedHours * gemsPerHour
                                   + assumedLoginsPerDay * assumedAdWatchRate * gemsPerAd;
                double dailySink = assumedBoostsPerDay * gemCostBoost;

                double permanentSink = gemCostCapTier1 + gemCostCapTier2
                                     + gemCostAutomation * automationUnlockCount;

                // 중요 — 유저는 영구 해금을 다 살 때까지 부스트를 사지 않는다.
                // 반복 소비를 90일 전체에 균등 분배하면 초반 잉여를 놓쳐
                // 실제보다 훨씬 낙관적인 판정이 나온다.
                // (광고당 30젬 설정에서 추정 1,720 vs 실제 7,570으로 4배 이상 어긋났다)
                double daysToUnlock = dailySupply > 0
                    ? System.Math.Min(90.0, permanentSink / dailySupply)
                    : 90.0;
                double boostActiveDays = 90.0 - daysToUnlock;

                double surplus90 = dailySupply * 90.0
                                 - permanentSink
                                 - dailySink * boostActiveDays;

                if (gemCostBoost > 0 && surplus90 > gemSurplus90Limit)
                {
                    error = $"구슬 공급 과잉입니다. 90일 후 약 {surplus90:F0}개가 남습니다. " +
                            $"(하루 공급 {dailySupply:F0} / 현실적 소비 {dailySink:F0} / " +
                            $"영구 싱크 {permanentSink:F0}) " +
                            "gemsPerHour를 낮추거나 싱크를 늘리세요.";
                    return false;
                }
            }

            // ── 범위 검증 ──
            // 0이나 음수가 들어가면 나눗셈에서 무한대가 나오거나
            // 상점이 공짜가 되는 등 조용히 망가진다.
            if (gemsPerHour < 0 || gemsPerAd < 0)
            {
                error = "구슬 지급량은 음수일 수 없습니다.";
                return false;
            }
            if (gemCostCapTier1 <= 0 || gemCostCapTier2 <= 0 ||
                gemCostAutomation <= 0 || gemCostBoost <= 0)
            {
                error = "구슬 소비 가격은 0보다 커야 합니다. 0이면 상점이 공짜가 됩니다.";
                return false;
            }
            if (gemCostCapTier2 <= gemCostCapTier1)
            {
                error = $"2단계 상한 확장({gemCostCapTier2})이 1단계({gemCostCapTier1})보다 " +
                        "비싸야 합니다.";
                return false;
            }
            if (automationUnlockCount < 1)
            {
                error = "자동화 해금 종류는 최소 1개여야 합니다.";
                return false;
            }
            if (offlineCapHours <= 0)
            {
                error = "오프라인 상한은 0보다 커야 합니다. 0이면 오프라인 보상이 0으로 나눠집니다.";
                return false;
            }
            if (offlineCapHoursMax < offlineCapHours)
            {
                error = $"확장 상한({offlineCapHoursMax}h)이 기본 상한({offlineCapHours}h)보다 " +
                        "작습니다.";
                return false;
            }
            if (offlineAdMultiplier < 1.0)
            {
                error = "광고 배수는 1.0 이상이어야 합니다. 광고를 보면 손해가 됩니다.";
                return false;
            }
            if (enemiesPerWave < 1 || baseEnemyHp <= 0 || baseCoinReward <= 0 || baseDps <= 0)
            {
                error = "적 수 · 체력 · 코인 · DPS 기본값은 0보다 커야 합니다.";
                return false;
            }
            if (assumedBoostsPerDay < 0)
            {
                error = "부스트 구매 가정치는 음수일 수 없습니다.";
                return false;
            }
            if (assumedLoginsPerDay <= 0)
            {
                error = "접속 횟수 가정치는 0보다 커야 합니다.";
                return false;
            }
            if (coreDailySoftCap < 1)
            {
                error = "코어 소프트캡은 1 이상이어야 합니다.";
                return false;
            }
            if (coreDecayPerRun <= 0 || coreDecayPerRun >= 1.0)
            {
                error = $"코어 감쇠율({coreDecayPerRun})은 0과 1 사이여야 합니다. " +
                        "1 이상이면 감쇠가 일어나지 않아 앱을 켜둔 만큼 무한히 성장합니다.";
                return false;
            }

            error = null;
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Validate(out string err))
                Debug.LogWarning($"[EconomyConfig] {err}", this);
        }
#endif
    }
}

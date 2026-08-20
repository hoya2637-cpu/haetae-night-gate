using System;
using System.Collections.Generic;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Data;
using IdleDefense.Economy;
using IdleDefense.Save;
using IdleDefense.Ads;

namespace IdleDefense.Game
{
    /// <summary>
    /// 게임 전체를 묶는 컨트롤러. 씬에 하나만 둔다.
    ///
    /// 역할 분리:
    ///   BattleRunner  — 전투 계산 (순수 C#, 테스트 가능)
    ///   이 클래스     — Unity 수명주기, 세이브, 오프라인 처리
    ///   View 계층     — 화면 표시 (이 클래스를 구독만 한다)
    ///
    /// 여기에 전투 공식을 직접 쓰지 말 것. 전부 EconomyCore를 거친다.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] private EconomyConfig config;

        [Header("저장")]
        [Tooltip("자동 저장 주기(초). 너무 짧으면 모바일에서 I/O 부담이 된다")]
        [SerializeField] private float autoSaveInterval = 30f;

        [Header("광고")]
        [SerializeField] private RewardedAdService adService;

        [Header("자동화 (구슬로 해금)")]
        [SerializeField] private bool autoUpgrade;
        [SerializeField] private bool autoRebirth;

        [Tooltip("부적 자동 소환. 켜면 편하지만 효율이 낮다 — 직접 조작할 이유를 남긴다")]
        [SerializeField] private bool autoTalisman;

        public GameState State { get; private set; }
        public BattleRunner Battle { get; private set; }
        public UpgradeTracks Tracks { get; private set; }
        public TalismanSystem Talismans { get; private set; }

        /// <summary>
        /// 오프라인 보상 화면에 표시할 정보 묶음.
        ///
        /// 이 화면은 단순한 보상 팝업이 아니라 '다음 런의 시작 화면'이다.
        /// 유저가 알고 싶은 것은 "코인 얼마 받았나"가 아니라
        /// "내가 얼마나 앞서갔나"이므로, 웨이브 점프를 크게 보여줘야 한다.
        /// </summary>
        public struct OfflineSummary
        {
            public double AwayHours;
            public BigNumber Coin;
            public int Gems;

            /// <summary>직전 런에서 도달했던 웨이브</summary>
            public int PreviousWave;
            /// <summary>이번 런을 시작할 웨이브 — 이게 화면의 주인공</summary>
            public int StartWave;

            /// <summary>광고를 보면 받게 될 코인</summary>
            public BigNumber CoinWithAd;
            /// <summary>광고를 보면 시작할 웨이브</summary>
            public int StartWaveWithAd;

            /// <summary>"3시간 42분" 형식</summary>
            public string AwayText
            {
                get
                {
                    int total = (int)(AwayHours * 60);
                    int h = total / 60, m = total % 60;
                    if (h <= 0) return $"{m}분";
                    return m > 0 ? $"{h}시간 {m}분" : $"{h}시간";
                }
            }
        }

        /// <summary>
        /// 오프라인 보상이 '실제로 지급된' 결과.
        ///
        /// ★ 제안(OnOfflineRewardReady)과 수령(OnOfflineClaimed)은 다른 사건이다.
        ///   제안만 보고 껐는데 수령으로 집계되면 오프라인 배치의 수익 분석이 통째로 틀어진다.
        ///
        /// ★ RewardMultiplier는 '광고를 봤는가'가 아니라 '실제로 몇 배가 지급됐는가'다.
        ///   토큰 검증에 실패해 일반 수령으로 떨어지면 광고를 봤어도 1.0이다.
        ///   광고 SDK를 교체해도 이 숫자의 의미는 변하지 않는다.
        /// </summary>
        public struct OfflineClaim
        {
            public double AwayHours;
            public BigNumber Coin;
            public int Gems;
            public int PreviousWave;
            public int StartWave;
            /// <summary>기본 제안 대비 실제 지급 배수. 1.0 = 일반, 2.0 = 광고 2배</summary>
            public double RewardMultiplier;
        }

        /// <summary>오프라인 보상이 준비됨. UI가 이걸 받아 수령 화면을 띄운다.</summary>
        public event Action<OfflineSummary> OnOfflineRewardReady;

        /// <summary>오프라인 보상이 실제로 지급됨. 계측은 이 시점만 수령으로 센다.</summary>
        public event Action<OfflineClaim> OnOfflineClaimed;

        /// <summary>
        /// 부적 장착 변경 결과. 계측이 조합별 분포를 뽑는 근거다.
        ///
        /// LoadoutKey를 '정렬해서 이어붙인 문자열'로 주는 이유:
        ///   분석에서 알고 싶은 것은 '어떤 조합을 쓰는가'이지 '몇 번 슬롯에 뭘 뒀는가'가 아니다.
        ///   순서를 남기면 같은 조합이 120가지(5!) 키로 흩어져 집계가 안 된다.
        /// </summary>
        public struct TalismanChange
        {
            /// <summary>정렬된 장착 부적 id를 '|'로 이은 정본 키.</summary>
            public string LoadoutKey;
            /// <summary>이번에 추가된 부적 id들. 없으면 빈 문자열.</summary>
            public string Added;
            /// <summary>이번에 빠진 부적 id들. 없으면 빈 문자열.</summary>
            public string Removed;
            public int SlotCount;
        }

        /// <summary>부적 장착이 '실제로 바뀐' 경우에만 발화한다. 같은 구성 재적용은 침묵한다.</summary>
        public event Action<TalismanChange> OnTalismanChanged;

        /// <summary>
        /// 런 시작. 계측이 게임 상태를 추측하지 않도록 실제 값을 그대로 넘긴다.
        /// runIndex는 State.runIndex이며, session_id와 조합해 런을 식별한다.
        /// </summary>
        public event Action<int, int, bool> OnRunStarted;   // runIndex, startWave, fromOffline

        public event Action OnRebirth;

        /// <summary>런 종료. 계측이 BattleRunner에 직접 붙지 않도록 여기서 중계한다.</summary>
        public event Action<int, bool> OnRunEnded;          // deepestWave, walled

        /// <summary>
        /// 승천. 승천 전후 상태를 함께 넘긴다.
        /// 승천 후 기록 회복은 P0에서 발견한 최우선 UX 지표이며(1/3/7/20/40런),
        /// 이 값들이 없으면 계측 쪽에서 추측해야 한다.
        /// </summary>
        public event Action<int, int, double, double> OnAscend;  // tierBefore, tierAfter, coresBefore, coresAfter

        private float saveTimer;
        private EconomyCore.OfflineReward pendingOffline;
        private bool hasPendingOffline;

        /// <summary>오프라인 보상이 수령 대기 중인가. 수령 전에는 런이 시작되지 않는다.</summary>
        public bool HasPendingOffline => hasPendingOffline;

        /// <summary>
        /// 오프라인 보상 계산의 기준이 된 자리비움 시간.
        ///
        /// 화면을 연 시점에 고정하는 이유:
        ///   광고 시청 시 AwayHours를 다시 계산하면, 유저가 보상 화면을
        ///   열어두고 있는 동안 자리비움이 계속 늘어난다.
        ///   화면을 30분 켜두고 광고를 보면 30분치를 더 받게 되는데,
        ///   이건 '접속하지 않은 시간'이라는 정의와 어긋난다.
        /// </summary>
        private double pendingAwayHours;

        // ─────────────────────────────────────────

        private void Awake()
        {
            // 같은 GameObject에 붙어 있으면 인스펙터 배선 없이도 잡는다.
            if (adService == null) adService = GetComponent<RewardedAdService>();

            if (config == null)
            {
                Debug.LogError("[GameController] EconomyConfig가 비어 있습니다. " +
                               "인스펙터에서 지정하세요.");
                enabled = false;
                return;
            }

            if (!config.Validate(out string error))
            {
                Debug.LogError($"[GameController] 경제 설정 오류: {error}");
                // 설정이 깨진 채로 진행하면 유저 데이터가 오염된다
                enabled = false;
                return;
            }

            State = SaveSystem.Load(config);
            Tracks = new UpgradeTracks(config, State.trackLevels);
            Battle = new BattleRunner(config);
            Talismans = new TalismanSystem(config) { AutoSummon = autoTalisman };
            ApplyLoadout(State.equippedTalismans);

            Battle.OnRunEnded += HandleRunEnded;

            if (adService != null)
            {
#if UNITY_EDITOR
                // 에디터 확인용 더미. 빌드에는 절대 들어가지 않는다.
                adService.Initialize(new EditorFakeAdProvider());
#elif ALLOW_FAKE_ADS_IN_BUILD
                // 내부 테스트 빌드 전용 (오프라인 누적 실기 검증 등).
                // 이 경로로 만든 빌드는 광고 없이 보상이 나간다. 스토어 제출 금지.
                adService.Initialize(new EditorFakeAdProvider());
                Debug.LogError("[AD] 더미 광고 프로바이더로 빌드되었습니다. " +
                               "광고 없이 보상이 지급됩니다. 스토어 제출 금지.");
#else
#error 광고 프로바이더가 주입되지 않았습니다. AppLovinAdProvider로 교체하세요.
#error 내부 테스트 빌드라면 Scripting Define Symbols에 ALLOW_FAKE_ADS_IN_BUILD 를 추가하세요.
#endif
            }
            else
            {
                Debug.LogWarning("[GameController] 광고 서비스가 비어 있습니다. " +
                                 "리워드 보상이 동작하지 않습니다.");
            }

            Application.targetFrameRate = 60;
        }

        private void Start()
        {
            // ★ PrepareOfflineReward를 Awake가 아니라 Start에서 부른다.
            //   OnOfflineRewardReady를 Awake에서 쏘면, DefaultExecutionOrder가
            //   이 클래스보다 큰 구독자(예: DebugHud=100)는 아직 구독 전이라
            //   그 이벤트를 구조적으로 놓친다. 구독은 Awake, 발화는 Start가 원칙이다.
            PrepareOfflineReward();

            // 오프라인 보상을 수령하기 전에는 런을 시작하지 않는다.
            // UI가 ClaimOffline()을 호출하면 그때 시작한다.
            if (!hasPendingOffline) BeginNewRun(startWave: 1, startCoin: BigNumber.Zero);
        }

        // ─────────────────────────────────────────
        // 오프라인

        private void PrepareOfflineReward()
        {
            double away = State.AwayHours(DateTime.UtcNow);
            if (away < 0.02) return;   // 1분 미만은 무시

            pendingAwayHours = away;   // 이 값을 광고 수령 때도 그대로 쓴다

            double coinMul = CurrentCoinMultiplier();
            pendingOffline = EconomyCore.CalculateOffline(
                config, pendingAwayHours, State.lastRunWave, coinMul,
                watchedAd: false, capHoursOverride: State.offlineCapHours,
                attackMultiplier: CurrentAttackMultiplier());

            // 광고 시청 시 받게 될 값도 미리 계산해 화면에 함께 보여준다.
            // "광고를 보면 얼마나 더 앞서가는가"가 시청률을 좌우한다.
            var withAd = EconomyCore.CalculateOffline(
                config, pendingAwayHours, State.lastRunWave, coinMul,
                watchedAd: true, capHoursOverride: State.offlineCapHours,
                attackMultiplier: CurrentAttackMultiplier());

            hasPendingOffline = true;
            OnOfflineRewardReady?.Invoke(new OfflineSummary
            {
                AwayHours = pendingAwayHours,
                Coin = pendingOffline.Coin,
                Gems = pendingOffline.Gems,
                PreviousWave = State.lastRunWave,
                StartWave = Math.Max(1, (int)pendingOffline.StartWave),
                CoinWithAd = withAd.Coin,
                StartWaveWithAd = Math.Max(1, (int)withAd.StartWave),
            });
        }

        /// <summary>
        /// 오프라인 보상 일반 수령. 이 호출 이후 런이 시작된다.
        /// </summary>
        public void ClaimOffline() => ClaimOfflineInternal(watchedAd: false);

        /// <summary>
        /// 오프라인 보상 2배 수령. 광고 시청이 검증된 토큰이 있어야 한다.
        ///
        /// bool 인자를 외부에 열어두지 않는 이유:
        ///   UI가 true를 넘기는 것만으로 광고 보상을 받을 수 있게 되기 때문이다.
        ///   토큰은 RewardedAdService만 발급하고 1회용이라 그 경로가 막힌다.
        /// </summary>
        public bool ClaimOfflineWithRewardedAd(RewardedAdService.RewardToken token)
        {
            if (adService == null || !adService.ConsumeToken(token, RewardType.OfflineDouble))
            {
                Debug.LogWarning("[GameController] 광고 보상 검증 실패. 일반 수령으로 처리합니다.");
                ClaimOfflineInternal(watchedAd: false);
                return false;
            }
            ClaimOfflineInternal(watchedAd: true);
            return true;
        }

        private void ClaimOfflineInternal(bool watchedAd)
        {
            if (!hasPendingOffline)
            {
                BeginNewRun(1, BigNumber.Zero);
                return;
            }

            // 광고 보상도 화면을 연 시점의 자리비움 시간으로 계산한다.
            // 지금 시각을 다시 재면 화면을 오래 열어둔 만큼 보상이 늘어난다.
            var reward = watchedAd
                ? EconomyCore.CalculateOffline(
                    config, pendingAwayHours, State.lastRunWave,
                    CurrentCoinMultiplier(), true, State.offlineCapHours,
                    CurrentAttackMultiplier())
                : pendingOffline;

            State.gems += reward.Gems;
            hasPendingOffline = false;

            // 실제 지급 배수. 광고 시청 여부가 아니라 지급 결과로 잰다.
            // AppliedRatio를 쓴다. 코인 나눗셈보다 정확하고,
            // 천장(offlineRatioCeiling)에 잘렸을 때 2가 아닌 실제 값이 그대로 남는다.
            double appliedMultiplier = 1.0;
            if (watchedAd && pendingOffline.AppliedRatio > 0.0)
                appliedMultiplier = reward.AppliedRatio / pendingOffline.AppliedRatio;

            OnOfflineClaimed?.Invoke(new OfflineClaim
            {
                AwayHours = pendingAwayHours,
                Coin = reward.Coin,
                Gems = reward.Gems,
                PreviousWave = State.lastRunWave,
                StartWave = Math.Max(1, (int)reward.StartWave),
                RewardMultiplier = appliedMultiplier,
            });

            // 철칙 — 코어(도깨비불)는 오프라인으로 지급하지 않는다.
            // 지급하면 90일 성장 곡선이 즉시 붕괴한다.

            BeginNewRun(Math.Max(1, (int)reward.StartWave), reward.Coin, fromOffline: true);
            SaveNow();
        }

        // ─────────────────────────────────────────
        // 런

        private void BeginNewRun(int startWave, BigNumber startCoin, bool fromOffline = false)
        {
            Tracks.ResetForRebirth();
            State.trackLevels = Tracks.Snapshot();

            Battle.AttackMultiplier =
                EconomyCore.AttackMultiplier(config, State.cores, State.tier)
                * Tracks.CombatMultiplier;
            Battle.CoinMultiplier = CurrentCoinMultiplier();

            Talismans.ClearActive();
            Battle.TalismanMultiplier = 1.0;

            Battle.BeginRun(startWave, startCoin);
            State.currentWave = startWave;

            OnRunStarted?.Invoke(State.runIndex, startWave, fromOffline);
        }

        private double CurrentCoinMultiplier()
            => EconomyCore.CoinMultiplier(config, State.cores, State.tier)
             * (Tracks?.CoinMultiplier ?? 1.0);

        /// <summary>
        /// 오프라인 시작 웨이브 상한 계산에 쓰는 전투 배수.
        ///
        /// Tracks.CombatMultiplier를 곱하지 않는 이유: 오프라인 수령 직후
        /// BeginNewRun이 Tracks.ResetForRebirth()를 호출해 트랙이 0으로 돌아간다.
        /// 즉 런 시작 시점의 곱연산 배수는 1.0이며, 오프라인 코인으로 새로 사게 된다.
        /// EconomyCore.MaxClearableWave가 그 구매를 내부에서 재현한다.
        /// </summary>
        private double CurrentAttackMultiplier()
            => EconomyCore.AttackMultiplier(config, State.cores, State.tier);

        private void HandleRunEnded(int deepestWave)
        {
            State.lastRunWave = deepestWave;
            if (deepestWave > State.bestWave) State.bestWave = deepestWave;
            SaveNow();

            OnRunEnded?.Invoke(deepestWave, Battle.IsWalled);

            if (autoRebirth) DoRebirth();
        }

        /// <summary>환생 가능 여부. UI 버튼 활성화 판단에 쓴다.</summary>
        public bool CanRebirth => Battle != null && Battle.IsWalled;

        /// <summary>
        /// 환생. 코어(도깨비불)를 얻고 런을 초기화한다.
        ///
        /// 벽에 부딪혀야만 가능하다.
        /// UI에서만 막으면 안 되는 이유:
        ///   웨이브 1에서 환생을 반복하면 코어를 조금씩이지만 계속 벌 수 있고,
        ///   자동 환생(autoRebirth)이 켜져 있으면 그게 무한 루프가 된다.
        ///   조건은 게임 로직에서 강제해야 한다.
        /// </summary>
        public bool DoRebirth()
        {
            if (!CanRebirth)
            {
                Debug.LogWarning("[GameController] 벽에 도달하지 않아 환생할 수 없습니다.");
                return false;
            }

            int wave = Battle.DeepestWave;
            State.cores += EconomyCore.CoreGainWithDecay(config, wave, State.runsToday);
            State.runsToday++;
            State.runIndex++;
            State.lastRunWave = wave;

            if (EconomyCore.CanAscend(config, State.tier, wave, State.cores))
            {
                int tierBefore = State.tier;
                double coresBefore = State.cores;

                State.tier++;
                State.cores = EconomyCore.CoresAfterAscend(config, State.cores);

                OnAscend?.Invoke(tierBefore, State.tier, coresBefore, State.cores);
            }

            Talismans.ResetAll();

            OnRebirth?.Invoke();
            BeginNewRun(1, BigNumber.Zero);
            SaveNow();
            return true;
        }

        public bool CanAscendNow()
            => EconomyCore.CanAscend(config, State.tier, Battle.DeepestWave, State.cores);

        /// <summary>승천 진행 상황. UI가 "웨이브는 됐는데 코어가 부족"을 표시하는 데 쓴다.</summary>
        public (bool waveOk, bool coreOk) AscendProgress()
            => EconomyCore.AscendProgress(config, State.tier, Battle.DeepestWave, State.cores);

        // ─────────────────────────────────────────
        // 업그레이드

        /// <summary>
        /// 부적 소환. 유저가 슬롯과 배치 위치를 고른다.
        /// 벽을 넘게 해주지는 않고 이미 넘을 수 있는 구간을 빨리 지나가게 한다.
        /// </summary>
        public bool SummonTalisman(int slot, TalismanSystem.Lane lane)
            => Talismans.Summon(slot, lane);

        /// <summary>
        /// 장착 교체. 세이브의 id 목록을 실제 부적 인스턴스로 바꿔 끼운다.
        /// 카탈로그 원본이 아니라 복제본이 들어가므로(Equip이 Clone한다)
        /// 런타임 쿨타임이 정적 카탈로그를 오염시키지 않는다.
        /// </summary>
        public void ApplyLoadout(string[] talismanIds)
        {
            var before = State.equippedTalismans ?? new string[0];

            // 정규화와 쿨타임 보존은 전부 TalismanSystem이 한다.
            // 여기서 UnequipAll + Equip을 직접 조합하면 쿨타임 보존 규칙이
            // 호출자마다 갈라지고, 그게 정확히 이번 익스플로잇의 원인이었다.
            var after = Talismans.ApplyLoadout(talismanIds);
            State.equippedTalismans = after;

            // 같은 구성을 다시 적용한 것은 사건이 아니다.
            // 부팅 때마다 talisman_change가 찍히면 조합 분포가 '장착 시점'이 아니라
            // '실행 횟수'를 세게 되어 지표가 의미를 잃는다.
            string keyBefore = string.Join("|", TalismanCatalog.NormalizeLoadout(before));
            string keyAfter = string.Join("|", after);
            if (keyBefore == keyAfter) return;

            SaveNow();   // 장착은 유저의 명시적 선택이다. 자동저장을 기다리지 않는다.

            OnTalismanChanged?.Invoke(new TalismanChange
            {
                LoadoutKey = keyAfter,
                Added = string.Join("|", Difference(after, TalismanCatalog.NormalizeLoadout(before))),
                Removed = string.Join("|", Difference(TalismanCatalog.NormalizeLoadout(before), after)),
                SlotCount = after.Length,
            });
        }

        private static List<string> Difference(string[] a, string[] b)
        {
            var result = new List<string>();
            foreach (var id in a) if (Array.IndexOf(b, id) < 0) result.Add(id);
            return result;
        }

        public bool TryUpgrade(EconomyCore.Track track)
        {
            if (!Tracks.TryBuy(track, Battle.Coin, out var cost)) return false;

            // 지갑은 오직 여기 한 곳에서만 줄어든다.
            Battle.SpendCoin(cost);
            RefreshMultipliers();
            State.trackLevels = Tracks.Snapshot();
            return true;
        }

        private void RefreshMultipliers()
        {
            Battle.AttackMultiplier =
                EconomyCore.AttackMultiplier(config, State.cores, State.tier)
                * Tracks.CombatMultiplier;
            Battle.CoinMultiplier = CurrentCoinMultiplier();

            // 흑(黑) 트랙을 크리티컬 '연출 빈도'에 반영한다.
            // 실제 피해량은 이미 CombatMultiplier에 곱해져 있으므로
            // 여기서 바꾸는 것은 화면에 크리티컬이 얼마나 자주 보이는가뿐이다.
            // 상한을 두는 이유: 100%가 되면 크리티컬이 평범해져 타격감이 죽는다.
            int blackLevel = Tracks.GetLevel(EconomyCore.Track.Black);
            Battle.CriticalChance = Math.Min(0.05 + blackLevel * 0.004, 0.45);
        }

        // ─────────────────────────────────────────
        // 광고 보상

        // 아래 메서드는 전부 검증된 토큰을 요구한다.
        // 토큰 없이 보상을 주는 경로를 만들지 말 것.

        /// <summary>도깨비 재소환. 코인 2배 10초.</summary>
        public bool GrantDokkaebi(RewardedAdService.RewardToken token)
        {
            if (!Verify(token, RewardType.DokkaebiSummon)) return false;
            Battle.SummonDokkaebi();
            return true;
        }

        /// <summary>
        /// 전투 배속. 이미 배속 중이면 남은 시간에 더한다(상한까지).
        /// 코루틴을 쓰지 않으므로 광고를 연속으로 봐도 서로 간섭하지 않는다.
        /// </summary>
        public bool GrantSpeedBoost(RewardedAdService.RewardToken token,
                                    double multiplier = 2.0, double durationSeconds = 600.0)
        {
            if (!Verify(token, RewardType.SpeedBoost)) return false;
            Battle.ApplySpeedBoost(multiplier, durationSeconds);
            return true;
        }

        /// <summary>부활. 런당 1회만 가능하다.</summary>
        public bool GrantRevive(RewardedAdService.RewardToken token)
        {
            if (!Battle.CanRevive) return false;
            if (!Verify(token, RewardType.Revive)) return false;
            return Battle.Revive();
        }

        private bool Verify(RewardedAdService.RewardToken token, RewardType expected)
        {
            if (adService == null)
            {
                Debug.LogError("[GameController] 광고 서비스가 연결되지 않았습니다.");
                return false;
            }
            return adService.ConsumeToken(token, expected);
        }

        // ─────────────────────────────────────────
        // 수명주기

        private void Update()
        {
            if (Battle == null || !Battle.IsRunning) return;

            // 부적: 쿨타임은 실시간, 지속시간은 전투시간(배속 반영)
            float real = Time.deltaTime;
            double battle = real * Math.Max(0.1, Battle.SpeedMultiplier);
            Talismans.Tick(real, battle);
            // 조건부 부적(어둑시니)이 웨이브 잔여 체력을 봐야 하므로 비율을 넘긴다.
            // CurrentDamageMultiplier(무인자)를 쓰면 항상 만체력으로 계산돼
            // 어둑시니가 가장 약한 값만 낸다.
            Battle.TalismanMultiplier = Talismans.DamageMultiplierAt(Battle.WaveHpRatio);

            // 저승사자류 즉시 삭제. 꺼내면 사라지므로 두 번 적용될 수 없다.
            // Battle.Tick보다 먼저 넘겨야 같은 프레임에 클리어 판정을 받는다.
            double execute = Talismans.ConsumeExecuteFraction();
            if (execute > 0.0) Battle.ExecuteFraction(execute);

            Battle.Tick(real, Tracks.TotalLevel);

            if (autoUpgrade)
            {
                // 한 프레임에 한 레벨만 산다. 몰아 사면 프레임이 튄다.
                if (Tracks.BuyBest(Battle.Coin, out var cost))
                {
                    Battle.SpendCoin(cost);
                    RefreshMultipliers();
                }
            }

            State.currentWave = Battle.CurrentWave;

            saveTimer += Time.deltaTime;
            if (saveTimer >= autoSaveInterval)
            {
                saveTimer = 0f;
                SaveNow();
            }
        }

        private void SaveNow()
        {
            if (State == null) return;
            State.Coin = Battle?.Coin ?? BigNumber.Zero;
            State.trackLevels = Tracks?.Snapshot() ?? State.trackLevels;
            SaveSystem.Save(State);
        }

        private void OnApplicationPause(bool paused)
        {
            // 모바일에서는 OnApplicationQuit이 호출되지 않을 수 있다.
            // 백그라운드 전환 시점이 사실상 마지막 저장 기회다.
            if (paused) SaveNow();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) SaveNow();
        }

        private void OnApplicationQuit() => SaveNow();
    }
}

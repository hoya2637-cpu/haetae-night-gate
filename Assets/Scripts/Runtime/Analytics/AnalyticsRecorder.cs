using System;
using UnityEngine;
using IdleDefense.Ads;
using IdleDefense.Core;
using IdleDefense.Economy;
using IdleDefense.Game;

namespace IdleDefense.Analytics
{
    /// <summary>
    /// 게임 도메인 이벤트를 계측 이벤트로 옮기는 얇은 접착층.
    ///
    /// ★ 여기에는 로직을 두지 않는다.
    ///   판단·집계·직렬화는 전부 순수 계층(AnalyticsEvents / AnalyticsBuffer)에 있다.
    ///   MonoBehaviour에 로직을 넣으면 EditMode 테스트가 못 타는 사각지대가 생긴다.
    ///   그게 P0의 근본 원인이었다.
    ///
    /// ★ 게임 상태를 읽는 곳은 BuildContext() 한 곳뿐이다.
    ///   런 식별자처럼 게임이 아는 값은 이벤트 인자로 받아 추측하지 않는다.
    ///
    /// 실행 순서 — GameController보다 먼저 Awake해야 첫 이벤트를 놓치지 않는다.
    /// 인스펙터 설정 없이 보장하려고 DefaultExecutionOrder를 쓴다.
    ///
    /// docs/Analytics_v0.1_설계.md
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AnalyticsRecorder : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameController controller;
        [SerializeField] private RewardedAdService adService;

        [Header("전송")]
        [Tooltip("에디터 콘솔에 이벤트를 출력한다")]
        [SerializeField] private bool logToConsole = true;

        [Tooltip("콘솔에 JSON 전문을 찍는다. 끄면 요약만")]
        [SerializeField] private bool verboseConsole = false;

        [Tooltip("persistentDataPath에 analytics.jsonl로 남긴다")]
        [SerializeField] private bool writeToFile = true;

        [Tooltip("이 건수마다 전송")]
        [SerializeField] private int batchSize = 20;

        [Tooltip("이 시간마다 전송(초). 건수가 안 차도 보낸다")]
        [SerializeField] private float flushIntervalSec = 30f;

        [Header("세션")]
        [Tooltip("백그라운드에 이 시간 이하로 머물면 같은 세션으로 잇는다(초). " +
                 "알림을 잠깐 확인하고 돌아오는 것을 새 세션으로 세지 않기 위한 유예다.")]
        [SerializeField] private float sessionResumeGraceSec = 30f;

        private AnalyticsBuffer buffer;
        private string sessionId;
        private float sessionStartTime;
        private float lastFlush;
        private int runsInSession;

        /// <summary>
        /// 세션이 열려 있는가. 이 플래그가 없으면 session_end가 중복 발화한다.
        ///
        /// 실측 — 첫 통합 테스트에서 세션 하나에 session_end가 20건 찍혔다.
        ///   에디터에서는 Run In Background가 꺼져 있으면 다른 창을 클릭할 때마다
        ///   OnApplicationPause(true)가 오고, 그때마다 종료 이벤트가 나갔다.
        ///   모바일에서도 알림을 확인하고 돌아올 때마다 같은 일이 벌어진다.
        ///   세션 수·세션 길이·세션당 런 수가 전부 부풀려져 리텐션 지표가 무의미해진다.
        /// </summary>
        private bool sessionActive;

        /// <summary>백그라운드로 나간 시각. 복귀 시 공백 길이를 재는 기준이다.</summary>
        private DateTime backgroundedAtUtc;
        private bool inBackground;

        // 계측이 자체적으로 들고 있는 상태 — 게임 로직에 넣지 않기 위한 최소한
        private int bestWaveSeen;
        private int runsSinceAscend = -1;   // -1 = 아직 승천 없음
        private int pendingAscendBestWave = -1;

        public string FilePath { get; private set; }

        /// <summary>
        /// 이 세션에서 계측 큐에 넣은 이벤트 총수.
        /// Debug HUD가 analytics.jsonl 줄 수와 대조하는 기준값이다.
        /// (플레이 중에는 버퍼에 남은 만큼 파일이 적다. 종료 후 같아야 한다)
        /// </summary>
        public int EnqueuedCount { get; private set; }

        /// <summary>큐 넘침으로 버려진 이벤트 수. 0이 아니면 계측이 손실된 것이다.</summary>
        public int DroppedCount => buffer?.DroppedCount ?? 0;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (adService == null) adService = GetComponent<RewardedAdService>();
            if (controller == null)
            {
                Debug.LogError("[Analytics] GameController가 비어 있습니다. 계측이 비활성화됩니다.");
                enabled = false;
                return;
            }

            IAnalyticsSink sink = BuildSink();
            buffer = new AnalyticsBuffer(sink, Mathf.Max(1, batchSize));

            // 세션 식별자·시작 시각은 BeginSession이 정본이다. 여기서 또 만들지 않는다.
            lastFlush = Time.realtimeSinceStartup;

            controller.OnRunStarted += HandleRunStarted;
            controller.OnRunEnded += HandleRunEnded;
            controller.OnRebirth += HandleRebirth;
            controller.OnAscend += HandleAscend;
            controller.OnOfflineClaimed += HandleOfflineClaimed;
            controller.OnTalismanChanged += HandleTalismanChanged;

            if (adService != null)
            {
                adService.OnAdCompleted += HandleAdCompleted;
                adService.OnAdFailed += HandleAdFailed;
            }
        }

        private IAnalyticsSink BuildSink()
        {
            var log = logToConsole ? new EditorLogSink(verboseConsole) : null;
            FileSink file = writeToFile ? new FileSink() : null;
            if (file != null) FilePath = file.Path_;

            if (log != null && file != null) return new CompositeSink(log, file);
            if (log != null) return log;
            if (file != null) return file;
            return new NullAnalyticsSink();
        }

        private void Start() => BeginSession();

        /// <summary>
        /// 세션 시작.
        /// 백그라운드에 sessionResumeGraceSec보다 오래 머물다 돌아오면 새 세션으로 본다.
        /// 짧은 이탈까지 새 세션으로 세면 세션 수가 부풀려지고,
        /// 반대로 몇 시간 뒤 복귀를 같은 세션으로 두면 세션 길이가 망가진다.
        /// </summary>
        private void BeginSession()
        {
            if (sessionActive || buffer == null) return;
            sessionActive = true;
            inBackground = false;

            sessionId = Guid.NewGuid().ToString("N").Substring(0, 16);
            sessionStartTime = Time.realtimeSinceStartup;
            lastFlush = sessionStartTime;
            runsInSession = 0;

            Enqueue(Create(AnalyticsSchema.SessionStart)
                .Set("away_hours", controller.State?.AwayHours(DateTime.UtcNow) ?? 0.0)
                .Set("is_first_launch", (controller.State?.runIndex ?? 0) == 0));
        }

        private void Update()
        {
            if (buffer == null) return;

            // 시간 기반 플러시. 매 프레임 하는 일은 비교 하나뿐이다.
            if (buffer.ShouldFlush ||
                (buffer.PendingCount > 0 && Time.realtimeSinceStartup - lastFlush >= flushIntervalSec))
            {
                buffer.Flush();
                lastFlush = Time.realtimeSinceStartup;
            }
        }

        /// <summary>
        /// 백그라운드 전환/복귀.
        ///
        /// ★ 세션 경계는 '나갈 때'가 아니라 '돌아올 때' 판단한다.
        ///   나가는 순간에는 얼마나 나가 있을지 알 수 없다.
        ///   나갈 때 바로 session_end를 쏘면 알림을 3초 확인하고 돌아온 것도
        ///   세션 하나로 세어져 DAU 대비 세션 수가 크게 부풀려진다.
        ///   (실측: 첫 통합 테스트에서 0.1초짜리 세션이 생겼다)
        ///
        ///   그래서 나갈 때는 시각만 적고 버퍼만 비운다.
        ///   앱이 백그라운드에서 죽어도 데이터는 이미 파일에 있다.
        ///   session_end만 유실되는데, 그건 어느 계측 도구에서나 최선노력 항목이다.
        ///
        /// ★ 유예 30초는 출발점이지 확정치가 아니다.
        ///   소프트런치에서 백그라운드 공백 분포를 보고 정한다.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                if (inBackground) return;
                inBackground = true;
                backgroundedAtUtc = DateTime.UtcNow;
                buffer?.FlushAll();   // 이벤트는 쏘지 않는다. 유실만 막는다.
                return;
            }

            if (!inBackground) return;
            inBackground = false;

            double gapSec = (DateTime.UtcNow - backgroundedAtUtc).TotalSeconds;
            if (gapSec < sessionResumeGraceSec) return;   // 같은 세션으로 잇는다

            EndSessionAndFlush();
            BeginSession();
        }

        private void OnApplicationQuit() => EndSessionAndFlush();

        private void OnDestroy()
        {
            if (controller == null) return;
            controller.OnRunStarted -= HandleRunStarted;
            controller.OnRunEnded -= HandleRunEnded;
            controller.OnRebirth -= HandleRebirth;
            controller.OnAscend -= HandleAscend;
            controller.OnOfflineClaimed -= HandleOfflineClaimed;
            controller.OnTalismanChanged -= HandleTalismanChanged;
            if (adService != null)
            {
                adService.OnAdCompleted -= HandleAdCompleted;
                adService.OnAdFailed -= HandleAdFailed;
            }
        }

        // ── 게임 상태를 읽는 유일한 지점 ──

        private AnalyticsContext BuildContext()
        {
            var s = controller.State;
            return new AnalyticsContext
            {
                SessionId = sessionId,
                RunId = s?.runIndex ?? 0,
                UserDay = 0,   // TODO: 설치일 저장 후 계산. v0.1에서는 0
                Tier = s?.tier ?? 1,
                Wave = controller.Battle?.CurrentWave ?? 0,
                BestWave = s?.bestWave ?? 0,
                CoresLog10 = s == null || s.cores <= 0 ? 0.0 : Math.Log10(s.cores),
                Gems = s?.gems ?? 0,
                AdsRemoved = s?.adsRemoved ?? false,
                RunsSinceAscend = runsSinceAscend,
            };
        }

        private AnalyticsEvent Create(string name)
            => AnalyticsEvents.Create(name, BuildContext(), NowMs());

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void Enqueue(AnalyticsEvent e)
        {
            if (buffer == null) return;
            buffer.Enqueue(e);
            EnqueuedCount++;
        }

        // ── 도메인 이벤트 → 계측 이벤트 ──

        private void HandleRunStarted(int runIndex, int startWave, bool fromOffline)
        {
            runsInSession++;
            if (runsSinceAscend >= 0) runsSinceAscend++;

            Enqueue(Create(AnalyticsSchema.RunStart)
                .Set("run_index", runIndex)
                .Set("start_wave", startWave)
                .Set("from_offline", fromOffline));
        }

        private void HandleRunEnded(int deepestWave, bool walled)
        {
            var tracks = controller.Tracks;
            int[] byTrack = tracks?.Snapshot() ?? new int[EconomyCore.TrackCount];
            int total = tracks?.TotalLevel ?? 0;

            var e = Create(AnalyticsSchema.RunEnd)
                .Set("reached_wave", deepestWave)
                .Set("duration_sec", controller.Battle?.RunElapsed ?? 0.0)
                .Set("walled", walled)
                .Set("upgrades_by_track", byTrack)
                .Set("total_upgrade_levels", total);

            if (controller.Battle != null) e.SetBig("coin", controller.Battle.Coin);
            Enqueue(e);

            if (walled)
                Enqueue(Create(AnalyticsSchema.WallHit)
                    .Set("wave", deepestWave + 1)
                    .Set("total_upgrade_levels", total));

            // 최고 기록 갱신 — 승천 후 기록 회복 지표의 종료 시점이다.
            if (deepestWave > bestWaveSeen)
            {
                int prev = bestWaveSeen;
                bestWaveSeen = deepestWave;

                if (pendingAscendBestWave >= 0 && deepestWave >= pendingAscendBestWave)
                    pendingAscendBestWave = -1;   // 회복 완료

                Enqueue(Create(AnalyticsSchema.RecordWave)
                    .Set("new_best", deepestWave)
                    .Set("prev_best", prev));
            }
        }

        private void HandleRebirth()
        {
            Enqueue(Create(AnalyticsSchema.Rebirth)
                .Set("runs_today", controller.State?.runsToday ?? 0));
        }

        private void HandleAscend(int tierBefore, int tierAfter, double coresBefore, double coresAfter)
        {
            // 승천 시점부터 기록 회복 카운트를 다시 시작한다.
            runsSinceAscend = 0;
            pendingAscendBestWave = bestWaveSeen;

            Enqueue(Create(AnalyticsSchema.Ascend)
                .Set("tier_before", tierBefore)
                .Set("tier_after", tierAfter)
                .Set("cores_before", coresBefore)
                .Set("cores_after", coresAfter)
                .Set("best_wave_before", bestWaveSeen));
        }

        /// <summary>
        /// ★ 제안(OnOfflineRewardReady)이 아니라 수령(OnOfflineClaimed)에 붙는다.
        ///   제안 시점에 찍으면 보상 화면을 보기만 하고 끈 유저도 수령으로 집계돼
        ///   오프라인 배치의 광고 부착률이 부풀려진다.
        ///
        /// ★ 한계(문서화된 것) — 12종 스키마를 유지하므로 제안 이벤트가 없다.
        ///   따라서 '제안 → 수령' 전환율은 이 스키마로 직접 측정되지 않는다.
        ///   session_start는 오프라인이 제안되지 않은 세션까지 포함하므로 분모가 아니다.
        ///   소프트런치에서 이 퍼널이 실제로 필요하다고 확인되면
        ///   offline_offer를 13번째 이벤트로 추가한다.
        /// </summary>
        private void HandleOfflineClaimed(GameController.OfflineClaim c)
        {
            Enqueue(Create(AnalyticsSchema.OfflineClaim)
                .Set("away_hours", c.AwayHours)
                .Set("prev_wave", c.PreviousWave)
                .Set("start_wave", c.StartWave)
                // '광고를 봤는가'가 아니라 '몇 배가 지급됐는가'. SDK 교체와 무관해진다.
                .Set("reward_multiplier", c.RewardMultiplier)
                .Set("reward_gems", c.Gems)
                .SetBig("reward_coins", c.Coin));
        }

        /// <summary>
        /// 부적 조합 변경. 4대 KPI 축 중 '진행'을 읽는 핵심 이벤트다.
        ///
        /// loadout이 정렬된 정본 키인 덕분에 분석에서 GROUP BY 한 번으로
        ///   - 어떤 조합이 실제로 많이 쓰이는가 (설계 의도와 일치하는가)
        ///   - 특정 조합을 쓴 유저의 웨이브 진행이 느린가 (함정 조합 탐지)
        /// 를 바로 뽑을 수 있다. 순서를 남겼으면 같은 조합이 120개 키로 흩어진다.
        /// </summary>
        private void HandleTalismanChanged(GameController.TalismanChange c)
            => Enqueue(Create(AnalyticsSchema.TalismanChange)
                .Set("loadout", c.LoadoutKey)
                .Set("added", c.Added)
                .Set("removed", c.Removed)
                .Set("slot_count", c.SlotCount));

        private void HandleAdCompleted(RewardType type)
            => Enqueue(Create(AnalyticsSchema.AdRequest)
                .Set("reward_type", type.ToString())
                .Set("result", "rewarded"));

        private void HandleAdFailed(RewardType type, string reason)
            => Enqueue(Create(AnalyticsSchema.AdRequest)
                .Set("reward_type", type.ToString())
                .Set("result", "failed")
                .Set("fail_reason", reason));

        private void EndSessionAndFlush()
        {
            if (buffer == null) return;
            if (!sessionActive) return;   // 이미 닫힌 세션을 또 닫지 않는다
            sessionActive = false;

            Enqueue(Create(AnalyticsSchema.SessionEnd)
                .Set("duration_sec", Time.realtimeSinceStartup - sessionStartTime)
                .Set("runs_in_session", runsInSession));

            buffer.FlushAll();

            if (buffer.DroppedCount > 0)
                Debug.LogWarning($"[Analytics] 큐 상한으로 {buffer.DroppedCount}건을 버렸습니다. " +
                                 "전송이 막혀 있는지 확인하세요.");
        }
    }
}

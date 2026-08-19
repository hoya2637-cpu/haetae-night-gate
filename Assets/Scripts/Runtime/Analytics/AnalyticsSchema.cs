using System;
using System.Collections.Generic;

namespace IdleDefense.Analytics
{
    /// <summary>
    /// 이벤트 스키마의 단일 출처.
    ///
    /// 이름과 버전을 코드 곳곳에 문자열로 흩뿌리면 오타 하나가 조용히 데이터를 버린다.
    /// 여기에 모아두고 테스트가 완결성을 검사한다.
    ///
    /// 버전은 이벤트별로 관리한다. 전역 버전을 쓰면 한 이벤트를 고칠 때
    /// 나머지 11종의 버전까지 올라가 구·신 데이터 구분이 무의미해진다.
    ///
    /// docs/Analytics_v0.1_설계.md
    /// </summary>
    public static class AnalyticsSchema
    {
        // ── 이벤트 이름 ──
        public const string SessionStart   = "session_start";
        public const string SessionEnd     = "session_end";
        public const string RunStart       = "run_start";
        public const string RunEnd         = "run_end";
        public const string Rebirth        = "rebirth";
        public const string Ascend         = "ascend";
        public const string RecordWave     = "record_wave";
        public const string OfflineClaim   = "offline_claim";
        public const string AdRequest      = "ad_request";
        public const string IapPurchase    = "iap_purchase";
        public const string TalismanChange = "talisman_change";
        public const string WallHit        = "wall_hit";

        /// <summary>이벤트별 스키마 버전. payload를 바꾸면 해당 항목만 올린다.</summary>
        public static readonly IReadOnlyDictionary<string, int> Versions =
            new Dictionary<string, int>
            {
                { SessionStart,   1 },
                { SessionEnd,     1 },
                { RunStart,       1 },
                { RunEnd,         1 },
                { Rebirth,        1 },
                { Ascend,         1 },
                { RecordWave,     1 },
                // v2 — 제안 시점이 아니라 실제 수령 시점으로 이동.
                //      start_wave_with_ad/gems_gained/coin 제거,
                //      reward_multiplier/reward_gems/reward_coins 추가.
                { OfflineClaim,   2 },
                { AdRequest,      1 },
                { IapPurchase,    1 },
                { TalismanChange, 1 },
                { WallHit,        1 },
            };

        /// <summary>모든 이벤트가 반드시 갖는 필드.</summary>
        public static readonly string[] RequiredCommonFields =
        {
            "event_name", "event_version", "event_time",
            "session_id", "run_id", "user_day",
            "tier", "wave", "best_wave",
        };

        public static readonly string[] AllEvents =
        {
            SessionStart, SessionEnd, RunStart, RunEnd, Rebirth, Ascend,
            RecordWave, OfflineClaim, AdRequest, IapPurchase, TalismanChange, WallHit,
        };

        public static int VersionOf(string eventName)
            => Versions.TryGetValue(eventName, out int v)
                ? v
                : throw new ArgumentException($"스키마에 없는 이벤트입니다: {eventName}");
    }

    /// <summary>
    /// 모든 이벤트에 공통으로 붙는 상태. 이벤트를 만들 때마다 현재 값을 채운다.
    /// </summary>
    public struct AnalyticsContext
    {
        public string SessionId;

        /// <summary>
        /// 해당 세이브 내 런의 단조 증가 번호 (State.runIndex).
        ///
        /// ★ 전역 유일 ID가 아니다. 세이브가 초기화되면 0부터 다시 시작한다.
        ///   분석상 런 식별자는 <c>session_id + run_id</c> 조합이다.
        ///   UUID나 별도 영속 카운터를 만들지 않는 이유 — 이미 존재하는 값을
        ///   재사용하는 편이 단순하고 실패 지점이 적다.
        ///
        /// 이 값은 게임 코드(GameController)가 실제 State.runIndex를 넘겨준다.
        /// 계측 계층이 게임 상태를 읽어 추측하지 않는다.
        /// </summary>
        public int RunId;
        public int UserDay;
        public int Tier;
        public int Wave;
        public int BestWave;
        public double CoresLog10;
        public int Gems;
        public bool AdsRemoved;
        /// <summary>마지막 승천 이후 경과한 런 수. 승천 직후 지표의 핵심 축이다.</summary>
        public int RunsSinceAscend;
    }

    /// <summary>
    /// 이벤트 생성기. 순수 함수이며 여기서 공통 필드가 빠지는 일이 없도록 강제한다.
    /// </summary>
    public static class AnalyticsEvents
    {
        public static AnalyticsEvent Create(string name, in AnalyticsContext ctx, long nowMs)
        {
            var e = new AnalyticsEvent(name, AnalyticsSchema.VersionOf(name), nowMs);
            e.Set("session_id", ctx.SessionId)
             .Set("run_id", ctx.RunId)
             .Set("user_day", ctx.UserDay)
             .Set("tier", ctx.Tier)
             .Set("wave", ctx.Wave)
             .Set("best_wave", ctx.BestWave)
             .Set("cores_log10", ctx.CoresLog10)
             .Set("gems", ctx.Gems)
             .Set("ads_removed", ctx.AdsRemoved)
             .Set("runs_since_ascend", ctx.RunsSinceAscend);
            return e;
        }
    }
}

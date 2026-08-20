using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using IdleDefense.Ads;
using IdleDefense.Analytics;
using IdleDefense.Economy;

namespace IdleDefense.Game
{
    /// <summary>
    /// 개발자용 디버그 패널. 첫 통합 검증을 위한 것이며 게임 UI가 아니다.
    ///
    /// ★ 이 스크립트는 아무것도 계산하지 않는다.
    ///   GameController가 정본이고 HUD는 그 값을 그대로 읽어 표시만 한다.
    ///   HUD가 자체적으로 wave나 coin을 계산하면
    ///   "화면은 48인데 Analytics는 47"인 상황이 생겨도 원인을 못 찾는다.
    ///   그래서 여기에는 EconomyCore 호출이나 산술이 없다.
    ///
    /// 목적 — 한 번의 Play로 세 계층이 일치하는지 확인한다.
    ///   GameController 실제 상태  →  이 화면  →  Console / analytics.jsonl
    ///
    /// OnGUI를 쓰는 이유는 Canvas·Text 배치 없이 컴포넌트 하나로 끝내기 위해서다.
    /// 성능은 개발용이라 신경 쓰지 않는다. 빌드에는 들어가지 않는다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] private GameController controller;
        [SerializeField] private AnalyticsRecorder recorder;
        [SerializeField] private RewardedAdService adService;

        [Header("표시")]
        [SerializeField] private int fontSize = 18;
        [SerializeField] private bool showButtons = true;

        private string lastEvent = "-";
        private int eventCount;
        private int runsSinceAscend = -1;
        private bool lastFromOffline;
        private string offlineLine = "-";

        private GUIStyle labelStyle;
        private readonly StringBuilder sb = new StringBuilder(512);

        private void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (recorder == null) recorder = GetComponent<AnalyticsRecorder>();
            if (adService == null) adService = GetComponent<RewardedAdService>();

            if (controller == null)
            {
                Debug.LogError("[DebugHud] GameController가 없습니다.");
                enabled = false;
                return;
            }

            // 이벤트 이름과 개수만 받는다. 상태 값은 여기서 만들지 않는다.
            controller.OnRunStarted += (idx, wave, fromOffline) =>
            {
                lastFromOffline = fromOffline;
                if (runsSinceAscend >= 0) runsSinceAscend++;
                Mark("run_start");
            };
            controller.OnRunEnded += (wave, walled) => Mark(walled ? "run_end (wall)" : "run_end");
            controller.OnRebirth += () => Mark("rebirth");
            controller.OnAscend += (before, after, cb, ca) =>
            {
                runsSinceAscend = 0;
                Mark($"ascend T{before}→T{after}");
            };
            controller.OnOfflineRewardReady += s =>
            {
                // 값은 컨트롤러가 계산해서 넘겨준 것을 그대로 찍는다.
                offlineLine = $"{s.AwayHours:F2}h  W{s.StartWave} (광고 W{s.StartWaveWithAd})";
                Mark("offline_ready");
            };
        }

        private void Mark(string name)
        {
            lastEvent = name;
            eventCount++;
        }

        private void OnGUI()
        {
            if (controller == null) return;

            if (labelStyle == null)
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    richText = false,
                    alignment = TextAnchor.UpperLeft,
                };

            var s = controller.State;
            var b = controller.Battle;

            sb.Clear();
            Row("TIER", s == null ? "-" : s.tier.ToString());
            Row("WAVE", b == null ? "-" : b.CurrentWave.ToString());
            Row("BEST", s == null ? "-" : s.bestWave.ToString());
            Row("RUN", s == null ? "-" : "#" + s.runIndex);
            Row("RUN TIME", b == null ? "-" : Clock(b.RunElapsed));
            Row("COINS", b == null ? "-" : b.Coin.ToString());
            Row("CORES", s == null ? "-" : s.cores.ToString("N0"));
            Row("GEMS", s == null ? "-" : s.gems.ToString());
            sb.AppendLine();
            Row("SINCE ASCEND", runsSinceAscend < 0 ? "-" : runsSinceAscend.ToString());
            Row("FROM OFFLINE", lastFromOffline ? "YES" : "NO");
            Row("WALLED", b != null && b.IsWalled ? "YES" : "NO");
            Row("OFFLINE", controller.HasPendingOffline ? "PENDING  " + offlineLine : offlineLine);
            sb.AppendLine();
            Row("LAST EVENT", lastEvent);
            // HUD MARKS는 이 화면이 구독한 컨트롤러 이벤트 수(5종)일 뿐이고,
            // EVENTS는 계측이 실제로 큐에 넣은 총수(12종)다. 두 값은 원래 다르다.
            // analytics.jsonl 줄 수와 비교할 대상은 EVENTS 쪽이다.
            Row("HUD MARKS", eventCount.ToString());
            Row("EVENTS", recorder == null ? "-" : recorder.EnqueuedCount.ToString());
            Row("DROPPED", recorder == null ? "-" : recorder.DroppedCount.ToString());
            if (recorder != null && !string.IsNullOrEmpty(recorder.FilePath))
            {
                sb.AppendLine();
                sb.AppendLine("LOG " + recorder.FilePath);
            }

            GUI.Label(new Rect(12, 12, 900, 620), sb.ToString(), labelStyle);

            if (showButtons) { DrawButtons(); DrawLoadout(); DrawSummons(); }
        }

        private void Row(string key, string value)
            => sb.Append(key.PadRight(14)).AppendLine(value);

        private static string Clock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }

        /// <summary>
        /// 광고 이벤트 배선 확인용. 실제 보상(토큰 소비)은 태우지 않는다.
        /// 여기서 확인하려는 것은 오직 ad_request 이벤트가 성공/실패 양쪽으로
        /// 생성되는가이며, 보상 지급 계약은 단위 테스트가 담당한다.
        /// </summary>
        /// <summary>
        /// 오프라인 2배 수령. 광고 → 토큰 발급 → ConsumeToken 까지 실제 경로를 태운다.
        /// 실패하면 GameController가 일반 수령으로 떨어뜨린다(런은 어쨌든 시작된다).
        /// </summary>
        private void ClaimOfflineWithAd()
        {
            if (adService == null) { controller.ClaimOffline(); return; }
            adService.RequestReward(RewardType.OfflineDouble,
                token => controller.ClaimOfflineWithRewardedAd(token),
                _ => controller.ClaimOffline());
        }

        private void FireAd(bool forceFail)
        {
            if (adService == null) return;
#if UNITY_EDITOR || ALLOW_FAKE_ADS_IN_BUILD
            EditorFakeAdProvider.ForceFailOnce = forceFail;
#endif
            adService.RequestReward(RewardType.SpeedBoost, _ => { }, _ => { });
        }

        /// <summary>
        /// 부적 장착 토글 8종. 게임 UI가 아니라 조합을 갈아끼우며 검증하기 위한 것이다.
        /// 실제 장착 규칙(중복 금지·슬롯 상한·정렬)은 전부 GameController가 판정한다.
        /// 여기서는 목록만 만들어 넘긴다 — HUD는 아무것도 계산하지 않는다.
        /// </summary>
        private void DrawLoadout()
        {
            float y = 12f;
            float x = Screen.width - 380f;

            var equipped = controller.State?.equippedTalismans ?? EmptyIds;
            GUI.Label(new Rect(x, y, 180, 26),
                $"부적 {equipped.Length}/{TalismanSystem.MaxSlots}", labelStyle);
            y += 30f;

            // 카탈로그 선언 순서가 아니라 표시 순서를 쓴다. TalismanCatalog.DisplayOrder 주석 참고.
            foreach (var id in TalismanCatalog.DisplayOrder)
            {
                var t = TalismanCatalog.Get(id);
                bool on = Array.IndexOf(equipped, t.Id) >= 0;
                if (GUI.Button(new Rect(x, y, 180, 30), (on ? "■ " : "□ ") + t.DisplayName))
                    ToggleTalisman(t.Id, on);
                y += 33f;
            }
        }

        private static readonly string[] EmptyIds = new string[0];

        /// <summary>소환 버튼 표시 순서. 매 프레임 new를 피하려고 재사용한다.</summary>
        private readonly List<int> summonOrder = new List<int>(TalismanSystem.MaxSlots);

        private void ToggleTalisman(string id, bool currentlyOn)
        {
            var list = new List<string>(controller.State?.equippedTalismans ?? EmptyIds);
            if (currentlyOn) list.Remove(id);
            else
            {
                if (list.Count >= TalismanSystem.MaxSlots) return;
                list.Add(id);
            }
            controller.ApplyLoadout(list.ToArray());
        }

        /// <summary>장착된 부적 소환. 쿨타임이 남아 있으면 남은 초를 표시한다.</summary>
        private void DrawSummons()
        {
            var eq = controller.Talismans?.Equipped;
            if (eq == null || eq.Count == 0) return;

            float y = Screen.height - 46f;
            float x = 12f;

            // 버튼을 늘어놓는 순서만 표시 순서로 바꾼다.
            // SummonTalisman에 넘기는 인덱스는 반드시 Equipped 안의 원래 슬롯 번호여야 한다 —
            // 화면 순서를 그대로 넘기면 다른 부적이 소환된다.
            summonOrder.Clear();
            for (int i = 0; i < eq.Count; i++) summonOrder.Add(i);
            summonOrder.Sort((a, b) =>
                TalismanCatalog.DisplayIndexOf(eq[a].Id)
                    .CompareTo(TalismanCatalog.DisplayIndexOf(eq[b].Id)));

            for (int k = 0; k < summonOrder.Count; k++)
            {
                int i = summonOrder[k];
                string label = eq[i].IsReady
                    ? eq[i].DisplayName
                    : $"{eq[i].DisplayName} {eq[i].CooldownRemaining:F0}";

                GUI.enabled = eq[i].IsReady;
                if (GUI.Button(new Rect(x, y, 120, 34), label))
                    controller.SummonTalisman(i, TalismanSystem.Lane.Middle);
                GUI.enabled = true;
                x += 126f;
            }

            GUI.Label(new Rect(x + 8f, y + 4f, 260, 26),
                $"부적배수 x{controller.Talismans.CurrentDamageMultiplier:F2}" +
                $"  활성 {controller.Talismans.ActiveCount}" +
                $"  대기 {controller.Talismans.PendingCount}", labelStyle);
        }

        private void DrawButtons()
        {
            float y = 12f;
            float x = Screen.width - 190f;

            // 오방색 5트랙. 색 이름은 UpgradeTracks가 정본이다.
            foreach (EconomyCore.Track track in Enum.GetValues(typeof(EconomyCore.Track)))
            {
                string name = UpgradeTracks.TrackName(track);
                int lv = controller.Tracks?.GetLevel(track) ?? 0;
                if (GUI.Button(new Rect(x, y, 170, 34), $"{name}  Lv{lv}"))
                    controller.TryUpgrade(track);
                y += 38f;
            }

            y += 12f;
            GUI.enabled = controller.CanRebirth;
            if (GUI.Button(new Rect(x, y, 170, 34), "환생")) controller.DoRebirth();
            GUI.enabled = true;
            y += 44f;

            // 오프라인 수령. 대기 중에는 런이 시작되지 않으므로 이 버튼이 없으면 게임이 멈춘다.
            GUI.enabled = controller.HasPendingOffline;
            if (GUI.Button(new Rect(x, y, 82, 34), "수령")) controller.ClaimOffline();
            if (GUI.Button(new Rect(x + 88, y, 82, 34), "광고2배")) ClaimOfflineWithAd();
            GUI.enabled = true;
            y += 44f;

            // 광고 → 계측 배선 확인용. 보상 지급 경로가 아니라 이벤트 발생 경로만 태운다.
            GUI.enabled = adService != null && !adService.IsRequestInFlight;
            if (GUI.Button(new Rect(x, y, 82, 34), "광고 O")) FireAd(false);
            if (GUI.Button(new Rect(x + 88, y, 82, 34), "광고 X")) FireAd(true);
            GUI.enabled = true;
            y += 44f;

            var p = controller.AscendProgress();
            GUI.Label(new Rect(x, y, 200, 26),
                $"승천 웨이브 {(p.waveOk ? "O" : "X")} / 코어 {(p.coreOk ? "O" : "X")}", labelStyle);
        }
    }
}

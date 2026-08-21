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

        [Tooltip("켜면 부팅 직후 펼쳐진 채로 시작한다. 기본은 접힘.")]
        [SerializeField] private bool showOnStart = false;

        /// <summary>
        /// 펼쳐져 있는가. **기본은 접힘이다.**
        ///
        /// ★ 2026-08-21 기본값을 뒤집었다.
        ///   이 HUD는 화면 상단 1/3과 오른쪽 세로줄을 통째로 덮는다.
        ///   그 상태로는 **실제 UI가 어떻게 보이는지 판단할 방법이 없다** —
        ///   글자가 작은 건지, 카드 이름이 잘리는 건지, 여백이 빈 건지
        ///   전부 이 회색 글자 뒤에 가려진다. 실제로 그래서 세 가지를 늦게 발견했다.
        ///
        ///   Suppressed와는 다른 물건이다.
        ///     Suppressed — 다른 **화면이** 열려 있으니 잠깐 비켜라 (남이 끈다)
        ///     expanded   — **내가** 지금 값을 보고 싶은가 (내가 켠다)
        ///   둘을 하나로 합치면 부적 화면을 닫을 때 디버그가 멋대로 켜진다.
        /// </summary>
        private bool expanded;

        /// <summary>접혔을 때 남는 손잡이. 이것마저 없으면 다시 켤 방법이 사라진다.</summary>
        private const float HandleSize = 40f;

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

            expanded = showOnStart;

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

        /// <summary>
        /// 게임 UI가 열려 있는 동안 디버그 HUD를 통째로 숨긴다.
        ///
        /// ★ IMGUI(OnGUI)는 Canvas ScreenSpaceOverlay보다 **항상 위에** 그려진다.
        ///   그래서 uGUI 쪽에서 정렬 순서로는 이걸 못 가린다. 끄는 수밖에 없다.
        ///   static인 이유는 화면이 여럿 생겨도 HUD는 하나이기 때문이다.
        /// </summary>
        public static bool Suppressed;

        private void OnGUI()
        {
            if (controller == null || Suppressed) return;

            if (labelStyle == null)
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    richText = false,
                    alignment = TextAnchor.UpperLeft,
                };

            // 손잡이는 항상 그린다. 접혀 있으면 여기서 끝난다.
            if (DrawHandle()) return;

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
            // 조건부 부적(어둑시니)이 이 값을 보고 배수를 바꾼다.
            // 배수와 나란히 보이지 않으면 그 축이 실제로 도는지 눈으로 확인할 수가 없다.
            Row("WAVE HP", b == null ? "-" : $"{b.WaveHpRatio * 100f:F0}%");
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

            GUI.Label(new Rect(12, 12 + HandleSize + 6f, 900, 620), sb.ToString(), labelStyle);

            if (showButtons) { DrawButtons(); DrawLoadout(); DrawSummons(); }
        }

        /// <summary>
        /// 접기/펴기 손잡이. 왼쪽 위 구석 40×40.
        ///
        /// ★ 키보드 단축키를 쓰지 않는 이유 —
        ///   이 프로젝트는 새 Input System을 쓰므로 구형 Input.GetKeyDown이
        ///   설정에 따라 예외를 던진다. 그리고 실기에는 키보드가 없다.
        ///   IMGUI 버튼은 입력 시스템 설정과 무관하게 항상 동작한다.
        /// </summary>
        /// <returns>접혀 있으면 true — 호출부는 나머지를 그리지 않는다.</returns>
        private bool DrawHandle()
        {
            var prev = GUI.color;
            // 접혀 있을 때는 존재만 알린다. 진하면 그것부터 UI를 가린다.
            GUI.color = expanded ? Color.white : new Color(1f, 1f, 1f, 0.30f);
            if (GUI.Button(new Rect(6f, 6f, HandleSize, HandleSize), expanded ? "×" : "·"))
                expanded = !expanded;
            GUI.color = prev;
            return !expanded;
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

        /// <summary>효과 축을 한 글자로. 17종이 되면 이름만으로는 무엇이 무엇인지 안 보인다.</summary>
        private static string AxisTag(TalismanEffect e)
        {
            switch (e)
            {
                case TalismanEffect.Damage:      return "피";
                case TalismanEffect.Execute:     return "삭";
                case TalismanEffect.Amplify:     return "증";
                case TalismanEffect.Duplicate:   return "복";
                case TalismanEffect.Haste:       return "쿨";
                case TalismanEffect.Extend:      return "연";
                case TalismanEffect.Random:      return "변";
                case TalismanEffect.Stack:       return "누";
                case TalismanEffect.Mature:      return "만";
                case TalismanEffect.Auto:        return "자";
                case TalismanEffect.Feed:        return "희";
                case TalismanEffect.Conditional: return "조";
                default:                         return "?";
            }
        }

        /// <summary>
        /// 부적 장착 토글 17종. 게임 UI가 아니라 조합을 갈아끼우며 검증하기 위한 것이다.
        /// 실제 장착 규칙(중복 금지·슬롯 상한·정렬)은 전부 GameController가 판정한다.
        /// 여기서는 목록만 만들어 넘긴다 — HUD는 아무것도 계산하지 않는다.
        /// </summary>
        private void DrawLoadout()
        {
            // 17종이라 한 줄로 세우면 화면을 넘는다. 두 칸으로 접는다.
            // 오른쪽 강화 버튼(Screen.width - 190)과 겹치지 않도록 왼쪽으로 밀었다.
            const float colW = 180f, rowH = 30f, rowGap = 3f, colGap = 10f;
            float x0 = Screen.width - 580f;
            float y0 = 12f;

            var equipped = controller.State?.equippedTalismans ?? EmptyIds;
            GUI.Label(new Rect(x0, y0, 240, 26),
                $"부적 {equipped.Length}/{TalismanSystem.MaxSlots}   (표시: 축)", labelStyle);
            y0 += 30f;

            var order = TalismanCatalog.DisplayOrder;
            int perCol = (order.Length + 1) / 2;

            // 카탈로그 선언 순서가 아니라 표시 순서를 쓴다. TalismanCatalog.DisplayOrder 주석 참고.
            for (int i = 0; i < order.Length; i++)
            {
                var t = TalismanCatalog.Get(order[i]);
                bool on = Array.IndexOf(equipped, t.Id) >= 0;

                float bx = x0 + (i / perCol) * (colW + colGap);
                float by = y0 + (i % perCol) * (rowH + rowGap);

                string label = (on ? "■ " : "□ ") + t.DisplayName + "  " + AxisTag(t.Effect);
                if (GUI.Button(new Rect(bx, by, colW, rowH), label))
                    ToggleTalisman(t.Id, on);
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
                // 무인자 버전은 항상 만체력으로 계산해 어둑시니가 가장 약하게 보인다.
                // 화면과 실제 전투 계산이 어긋나면 그 순간 이 HUD는 쓸모가 없다.
                $"부적배수 x{controller.Talismans.DamageMultiplierAt(controller.Battle != null ? controller.Battle.WaveHpRatio : 1f):F2}" +
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

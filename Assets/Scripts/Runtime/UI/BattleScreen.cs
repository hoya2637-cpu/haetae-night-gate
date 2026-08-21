using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleDefense.Art;
using IdleDefense.Economy;
using IdleDefense.Game;

namespace IdleDefense.UI
{
    /// <summary>
    /// 전투 화면. 플레이어가 실제로 보는 유일한 화면이다.
    ///
    /// ★ DebugHud와 정반대의 물건이다.
    ///   DebugHud는 **전부 보여주는 것**이 목적이고(우리 둘이 값을 대조해야 하니까),
    ///   이 화면은 **안 보여주는 것**이 목적이다.
    ///   그래서 여기 있는 것은 다섯 개뿐이다 — 진행 · 엽전 · 부적 5칸 · 오방색 5버튼 · 전장.
    ///
    ///   TIER/BEST/RUN/CORES/GEMS/SINCE ASCEND/LOG는 **일부러 뺐다.**
    ///   빠뜨린 게 아니라 플레이어의 판단에 쓰이지 않는 값이라 뺀 것이다.
    ///   여기에 뭔가 더 넣고 싶어지면 먼저 "이 값을 보고 유저가 무엇을 다르게 하는가"를 답할 것.
    ///
    /// ★ 조건부 버튼은 조건일 때만 존재한다.
    ///   환생 버튼이 '떠 있는 것' 자체가 "지금이 그때다"라는 신호다.
    ///   항상 떠 있으면 그 신호가 사라지고, 유저는 언제 눌러야 하는지 배울 기회를 잃는다.
    ///
    /// ★ 이 화면도 아무것도 판정하지 않는다.
    ///   구매 가능 여부·소환 성공 여부·승천 조건은 전부 GameController가 판정한다.
    ///   화면이 자체 계산을 하면 "화면은 되는데 실제로는 안 되는" 상태를 아무도 못 잡는다.
    ///   부적배수 x0.98을 164개 테스트가 전부 놓쳤던 게 정확히 그 반대 사례였다 —
    ///   그때는 화면에 진짜 값이 떠 있어서 눈으로 잡혔다. 그 원칙을 그대로 지킨다.
    ///
    /// ★ 프리팹도 씬 편집도 필요 없다. 컴포넌트 하나만 붙이면 된다.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class BattleScreen : MonoBehaviour
    {
        [SerializeField] private GameController controller;

        [Header("글꼴")]
        [Tooltip("비워두면 OS 글꼴을 빌린다. 모바일 빌드에는 한글 폰트 에셋이 필요하다.")]
        [SerializeField] private Font font;

        [Header("연출")]
        [Tooltip("비워두면 씬에서 찾는다. 없으면 컷인 없이 조용히 돈다.")]
        [SerializeField] private CutinPlayer cutin;

        [Header("개발")]
        [Tooltip("켜면 DebugHud를 숨긴다. 값 대조를 하는 동안에는 꺼두는 게 좋다.")]
        [SerializeField] private bool hideDebugHud;

        // ── 상단 ──
        private Text nightLabel;      // 밤 3 · 웨이브 41
        private Text bestLabel;       // 최고 85
        private RectTransform waveFill;
        private Text coinLabel;

        // ── 전장 ──
        private Image fieldBg;
        private Image haetae;
        private RectTransform haetaeRt;

        /// <summary>
        /// 적·총알·데미지 숫자. 이 화면이 소유하고 매 프레임 Tick을 불러준다.
        ///
        /// ★ 컴포넌트로 만들지 않은 이유 — 인스펙터에서 붙이는 걸 잊으면
        ///   전장이 조용히 사라진다. 실제로 BattleFeedback이 프리팹 두 개를 요구한 채
        ///   아무것도 안 하고 있었고, 아무도 몰랐다.
        /// </summary>
        private BattleField field;

        /// <summary>해치가 앞으로 튀어나온 정도(0~1). 발사 때마다 1로 차고 감쇠한다.</summary>
        private float lunge;

        // ── 부적 ──
        private struct SlotRefs
        {
            public Image Frame;
            public Image Art;
            public Text Name;
            public RectTransform CoolFill;
            public Button Button;
        }
        private readonly SlotRefs[] slots = new SlotRefs[TalismanSystem.MaxSlots];
        private Text multLabel;

        // ── 오방색 ──
        private struct TrackRefs
        {
            public EconomyCore.Track Track;
            public Image Block;
            public Text Level;
            public Button Button;
        }
        private readonly List<TrackRefs> tracks = new List<TrackRefs>(5);

        /// <summary>
        /// 강화 비용은 트랙당 하나가 아니라 **화면당 하나**다.
        ///
        /// ★ NextCost는 트랙별 레벨이 아니라 전체 합산 레벨로 값을 매긴다
        ///   (한 트랙 몰아주기를 비싸게 만들어 분산 빌드를 유리하게 하려는 설계다).
        ///   그래서 다섯 칸에 각각 찍으면 **언제나 같은 숫자 다섯 개**가 뜬다.
        ///   정보량은 0인데 자리는 다 차지하고, 무엇보다 고장난 것처럼 보인다.
        ///   한 번만 띄우면 "값은 하나, 어디에 쓸지만 고르면 된다"는 설계가 그대로 읽힌다.
        /// </summary>
        private Text costLabel;

        // ── 조건부 ──
        private GameObject rebirthGo;
        private GameObject offlineGo;
        private Text ascendLabel;

        // 매 프레임 문자열을 새로 만들지 않기 위한 캐시.
        // 방치형은 화면이 몇 시간씩 켜져 있다. GC가 곧 발열이다.
        private int lastWave = -1, lastTier = -1, lastBest = -1;
        private string lastCoin = "";
        private readonly int[] lastLevels = new int[5];
        private readonly string[] lastCosts = new string[5];
        private readonly string[] lastSlotIds = new string[TalismanSystem.MaxSlots];

        /// <summary>
        /// 첫 갱신이 실제로 끝났는가.
        ///
        /// ★ Awake에서 Refresh(true)를 부르면 GameController가 아직 State를 안 만들었을 수 있다.
        ///   그러면 '한 번만 하는 일'(스프라이트 배정)이 조용히 건너뛰어지고 영영 안 돌아온다.
        ///   실행 순서에 기대는 대신, 성공한 첫 갱신을 직접 확인한다.
        /// </summary>
        private bool firstDone;

        // ─────────────────────────────────────────

        private void Awake()
        {
            if (controller == null) controller = FindObjectOfType<GameController>();
            if (controller == null)
            {
                Debug.LogError("[BattleScreen] GameController가 없습니다.");
                enabled = false;
                return;
            }

            if (cutin == null) cutin = FindObjectOfType<CutinPlayer>();

            font = UiTheme.ResolveFont(font);
            WarnIfNoEventSystem();

            for (int i = 0; i < lastLevels.Length; i++) { lastLevels[i] = -1; lastCosts[i] = ""; }

            Build();
        }

        private void OnEnable()
        {
            if (hideDebugHud) DebugHud.Suppressed = true;
        }

        private void OnDisable()
        {
            if (hideDebugHud) DebugHud.Suppressed = false;
            field?.Unbind();
        }

        /// <summary>
        /// 버튼이 눌리려면 씬에 EventSystem이 있어야 한다.
        /// 직접 만들지 않는 이유는 이 프로젝트가 새 Input System을 쓰기 때문이다 —
        /// 구형 StandaloneInputModule을 코드로 붙이면 런타임에 예외가 난다.
        /// </summary>
        private static void WarnIfNoEventSystem()
        {
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
            Debug.LogWarning(
                "[BattleScreen] 씬에 EventSystem이 없어 버튼이 눌리지 않습니다.\n" +
                "  Hierarchy 우클릭 > UI > Event System 을 추가해주세요.");
        }

        // ─────────────────────────────────────────
        // 조립 — 아래에서 위로 쌓는다

        // ★ 2026-08-21 글자 사다리 상향(30/20/15 → 52/40/30/24/20)에 맞춰 다시 잡았다.
        //   글자만 키우면 상자 밖으로 넘친다. 사다리를 올리는 순간 이 네 수는 같이 움직인다.
        private const float TopH    = 200f;   // 150 — 웨이브 52 + 최고 24 + 진행 막대
        private const float TrackH  = 240f;   // 220 — 트랙 이름 40 + 레벨 30
        private const float SlotH   = 230f;   // 210 — 카드 이름 24 + 쿨 막대
        private const float MultH   = 56f;    // 44  — 한 줄에 세 값(부적배수·강화비용·승천)

        /// <summary>
        /// 하단 HUD가 차지하는 높이. 다른 화면이 여기를 침범하지 않도록 공개한다.
        ///
        /// ★ 이 값이 없으면 각 화면이 자기 자리를 눈대중으로 잡고,
        ///   실제로 부적 버튼이 흑 트랙 위에 얹히는 일이 벌어진다. 한 번 겪었다.
        /// </summary>
        public const float HudBottomHeight = TrackH + MultH + SlotH;

        private void Build()
        {
            var canvas = NewCanvas();
            var root = NewRect("Root", canvas.transform);
            Stretch(root, 0f, 0f);
            AddImage(root, UiTheme.Background);

            // ★ 배경은 화면 전체를 덮는다. HUD가 그 위에 얹힌다.
            //   전장 영역에만 넣으면 상단 바와 하단 패널 사이에 이음매가 보인다.
            //   배경 그림은 이미 9:16이라 늘리지 않아도 맞는다.
            var bg = NewRect("FieldBg", root);
            Stretch(bg, 0f, 0f);
            fieldBg = bg.gameObject.AddComponent<Image>();
            fieldBg.raycastTarget = false;
            fieldBg.color = Color.white;

            // ★ 그리는 순서가 곧 앞뒤다.
            //   배경 → 적 → 해치 → HUD.
            //   적을 해치보다 먼저 만들어야 가까이 온 적이 해치 뒤로 가려진다.
            //   그 가림 하나가 "해치가 앞에 서 있다"를 설명 없이 전달한다.
            field = new BattleField(root, font);
            BuildField(root);        // 해치
            BuildTopBar(root);
            BuildTracks(root);
            BuildSlots(root);
            BuildConditional(root);

            Refresh(true);
        }

        private void BuildTopBar(RectTransform root)
        {
            var bar = NewRect("TopBar", root);
            Anchor(bar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -TopH), new Vector2(0f, 0f));
            AddImage(bar, UiTheme.Panel);

            // ★ 화면에서 가장 큰 글자는 여기 하나뿐이다.
            //   "지금 몇 번째 밤의 몇 웨이브인가"가 이 게임의 유일한 진행도이고,
            //   흘끗 봤을 때 눈에 걸려야 하는 것도 그것 하나다.
            //   큰 글자를 둘 이상 두면 둘 다 안 커 보인다.
            nightLabel = NewText("Night", bar, UiTheme.FontHuge, UiTheme.Text);
            Anchor(nightLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.62f, 1f),
                   new Vector2(UiTheme.GapWide, -82f), new Vector2(0f, -16f));
            nightLabel.alignment = TextAnchor.MiddleLeft;

            bestLabel = NewText("Best", bar, UiTheme.FontSmall, UiTheme.TextDim);
            Anchor(bestLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.62f, 1f),
                   new Vector2(UiTheme.GapWide, -124f), new Vector2(0f, -82f));
            bestLabel.alignment = TextAnchor.MiddleLeft;

            // 웨이브 진행 막대 — 현재 웨이브의 남은 체력이다.
            // 이 값이 어둑시니(조건부 축)의 입력이기도 하므로 화면과 전투가 같은 수를 본다.
            var barBg = NewRect("WaveBarBg", bar);
            Anchor(barBg, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.GapWide, 16f), new Vector2(-UiTheme.GapWide, 34f));
            AddImage(barBg, UiTheme.Card);

            waveFill = NewRect("WaveBarFill", barBg);
            waveFill.anchorMin = new Vector2(0f, 0f);
            waveFill.anchorMax = new Vector2(0f, 1f);
            waveFill.offsetMin = Vector2.zero;
            waveFill.offsetMax = Vector2.zero;
            AddImage(waveFill, UiTheme.Accent);

            coinLabel = NewText("Coin", bar, UiTheme.FontTitle, UiTheme.Text);
            Anchor(coinLabel.rectTransform, new Vector2(0.62f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -92f), new Vector2(-UiTheme.GapWide, -24f));
            coinLabel.alignment = TextAnchor.MiddleRight;
        }

        /// <summary>
        /// 전장. 지금은 해치 한 마리뿐이다.
        /// 적·투사체·장승은 전투 연출 작업에서 들어온다 — 여기 자리만 잡아둔다.
        /// </summary>
        private void BuildField(RectTransform root)
        {
            var field = NewRect("Field", root);
            Anchor(field, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(0f, TrackH + SlotH + MultH), new Vector2(0f, -TopH));

            var rt = NewRect("Haetae", field);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(420f, 420f);
            rt.anchoredPosition = new Vector2(0f, UiTheme.GapWide);

            haetaeRt = rt;
            haetae = rt.gameObject.AddComponent<Image>();
            haetae.preserveAspect = true;
            haetae.raycastTarget = false;
        }

        private void BuildSlots(RectTransform root)
        {
            var row = NewRect("Slots", root);
            Anchor(row, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Gap, TrackH + MultH), new Vector2(-UiTheme.Gap, TrackH + MultH + SlotH));

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = UiTheme.Gap;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            for (int i = 0; i < slots.Length; i++) slots[i] = BuildSlot(row, i);

            // ★ 세 값이 한 줄을 나눠 쓴다 — 왼쪽 부적배수 / 가운데 강화비용 / 오른쪽 승천.
            //   전부 폭 전체에 걸쳐두고 정렬만 달리하면, 숫자가 길어지는 날
            //   (엽전이 e12로 넘어가는 날이 반드시 온다) 셋이 서로 겹친다.
            //   비율로 칸을 갈라두면 겹치는 대신 잘린다. 겹치는 것보다 잘리는 게 낫다.
            multLabel = NewText("Mult", root, UiTheme.FontSmall, UiTheme.TextDim);
            Anchor(multLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0.32f, 0f),
                   new Vector2(UiTheme.GapWide, TrackH), new Vector2(0f, TrackH + MultH));
            multLabel.alignment = TextAnchor.MiddleLeft;

            costLabel = NewText("Cost", root, UiTheme.FontName, UiTheme.Accent);
            Anchor(costLabel.rectTransform, new Vector2(0.32f, 0f), new Vector2(0.72f, 0f),
                   new Vector2(0f, TrackH), new Vector2(0f, TrackH + MultH));
        }

        private SlotRefs BuildSlot(RectTransform parent, int index)
        {
            var card = NewRect("Slot" + index, parent);
            var frame = AddImage(card, UiTheme.Card);

            var artRt = NewRect("Art", card);
            Anchor(artRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(8f, 58f), new Vector2(-8f, -8f));
            var art = artRt.gameObject.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;

            var name = NewFittedText("Name", card, UiTheme.FontSmall, UiTheme.Text);
            Anchor(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(6f, 20f), new Vector2(-6f, 54f));

            // 쿨타임 막대. 원형 링이 예쁘지만 Filled 타입은 스프라이트가 있어야 한다.
            // 아트가 들어오기 전까지는 스프라이트 없이 그려지는 가로 막대가 정직하다.
            var coolBg = NewRect("CoolBg", card);
            Anchor(coolBg, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(6f, 6f), new Vector2(-6f, 16f));
            AddImage(coolBg, UiTheme.CardLocked);

            var coolFill = NewRect("CoolFill", coolBg);
            coolFill.anchorMin = new Vector2(0f, 0f);
            coolFill.anchorMax = new Vector2(0f, 1f);
            coolFill.offsetMin = Vector2.zero;
            coolFill.offsetMax = Vector2.zero;
            AddImage(coolFill, UiTheme.Accent);

            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            int slot = index;                       // 클로저가 루프 변수를 잡지 않도록 복사한다
            btn.onClick.AddListener(() => Summon(slot));

            return new SlotRefs { Frame = frame, Art = art, Name = name, CoolFill = coolFill, Button = btn };
        }

        private void BuildTracks(RectTransform root)
        {
            var row = NewRect("Tracks", root);
            Anchor(row, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(UiTheme.Gap, UiTheme.Gap), new Vector2(-UiTheme.Gap, TrackH));

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = UiTheme.Gap;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            foreach (EconomyCore.Track t in Enum.GetValues(typeof(EconomyCore.Track)))
                tracks.Add(BuildTrack(row, t));
        }

        private TrackRefs BuildTrack(RectTransform parent, EconomyCore.Track track)
        {
            var cell = NewRect("Track_" + track, parent);

            // ★ 흑 트랙 문제.
            //   TrackColor(Black)은 #2B2B33이라 어두운 배경 위에서 사라진다.
            //   다섯 중 하나가 안 보이면 오방색이 넷이 된다.
            //   그래서 모든 칸에 밝은 테두리를 두른다 — 흑만 특별 취급하면 색이 어긋난다.
            var border = AddImage(cell, UiTheme.TextDim);

            var inner = NewRect("Block", cell);
            Stretch(inner, 3f, 3f);
            var block = AddImage(inner, UiTheme.Parse(UpgradeTracks.TrackColor(track), UiTheme.Card));

            // 흰 트랙(#F0EDE4) 위에서는 흰 글자가 안 읽힌다. 밝기로 글자색을 고른다.
            var c = block.color;
            var ink = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) > 0.6f
                ? UiTheme.Background : UiTheme.Text;

            var name = NewText("Name", inner, UiTheme.FontTitle, ink);
            Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -86f), new Vector2(0f, -10f));
            name.text = UpgradeTracks.TrackName(track);

            var level = NewText("Level", inner, UiTheme.FontName, ink);
            Anchor(level.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(0f, 14f), new Vector2(0f, -92f));
            level.alignment = TextAnchor.UpperCenter;

            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = border;
            var captured = track;
            btn.onClick.AddListener(() => controller.TryUpgrade(captured));

            return new TrackRefs { Track = track, Block = block, Level = level, Button = btn };
        }

        /// <summary>
        /// 조건부 버튼들. 조건이 아닐 때는 <b>존재하지 않는다</b>.
        /// 회색으로 떠 있는 것과 없는 것은 다르다 — 없으면 유저가 등장을 사건으로 읽는다.
        /// </summary>
        private void BuildConditional(RectTransform root)
        {
            var rebirth = NewButton("Rebirth", root, "환생", UiTheme.FontTitle);
            rebirthGo = rebirth.gameObject;
            Anchor(rebirth.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-220f, -70f), new Vector2(220f, 70f));
            rebirth.onClick.AddListener(() => controller.DoRebirth());
            SetImage(rebirthGo, UiTheme.Accent);
            rebirthGo.SetActive(false);

            var offline = NewButton("Offline", root, "밤새 모인 것 받기", UiTheme.FontName);
            offlineGo = offline.gameObject;
            Anchor(offline.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-260f, -60f), new Vector2(260f, 60f));
            offline.onClick.AddListener(() => controller.ClaimOffline());
            SetImage(offlineGo, UiTheme.Accent);
            offlineGo.SetActive(false);

            ascendLabel = NewText("Ascend", root, UiTheme.FontSmall, UiTheme.Accent);
            Anchor(ascendLabel.rectTransform, new Vector2(0.72f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, TrackH), new Vector2(-UiTheme.GapWide, TrackH + MultH));
            ascendLabel.alignment = TextAnchor.MiddleRight;
        }

        // ─────────────────────────────────────────
        // 갱신

        private void Update() => Refresh(false);

        private void Refresh(bool force)
        {
            var s = controller.State;
            var b = controller.Battle;
            if (s == null || b == null) return;

            if (!firstDone) { force = true; firstDone = true; }

            // ── 상단 ──
            if (force || b.CurrentWave != lastWave || s.tier != lastTier)
            {
                bool tierChanged = s.tier != lastTier;
                lastWave = b.CurrentWave;
                lastTier = s.tier;
                nightLabel.text = $"{NightName(s.tier)} 밤   ·   웨이브 {lastWave}";
                if (tierChanged || force)
                {
                    haetae.sprite = ArtLibrary.HaetaeTier(s.tier);
                    fieldBg.sprite = ArtLibrary.Field(s.tier);
                }
            }
            if (force || s.bestWave != lastBest)
            {
                lastBest = s.bestWave;
                bestLabel.text = lastBest > 0 ? $"최고 {lastBest}" : "첫 밤";
            }

            // 남은 체력이 아니라 '깎은 만큼'을 채운다. 진행 막대는 차오르는 게 맞다.
            //
            // ★ 웨이브가 아직 없을 때 막대를 비워야 한다.
            //   WaveHpRatio는 총 체력이 0이면 0을 돌려준다(그게 맞는 계약이다).
            //   그런데 여기서 1-0 = 1이 되어, **오프라인 보상을 안 받은 첫 화면에서
            //   진행 막대가 가득 찬 채로 떠 있었다.** 웨이브 0인데 다 깬 것처럼 보인다.
            //   "값이 없다"와 "값이 0이다"를 구분하지 않으면 늘 이 자리에서 터진다.
            bool waveRunning = b.CurrentWave > 0 && !b.WaveHpTotal.IsZero;
            float done = waveRunning ? 1f - Mathf.Clamp01(b.WaveHpRatio) : 0f;
            waveFill.anchorMax = new Vector2(done, 1f);

            string coin = b.Coin.ToString();
            if (force || coin != lastCoin) { lastCoin = coin; coinLabel.text = "엽전 " + coin; }

            // ── 부적 5칸 ──
            var eq = controller.Talismans != null ? controller.Talismans.Equipped : null;
            int count = eq != null ? eq.Count : 0;

            for (int i = 0; i < slots.Length; i++)
            {
                var sl = slots[i];
                if (i >= count)
                {
                    // ★ 빈 칸을 감추지 않는다. 다섯 칸이라는 사실 자체가 정보다.
                    sl.Frame.color = UiTheme.CardLocked;
                    sl.Art.enabled = false;
                    sl.Name.text = "빈 칸";
                    sl.Name.color = UiTheme.TextLocked;
                    sl.CoolFill.anchorMax = new Vector2(0f, 1f);
                    sl.Button.interactable = false;
                    lastSlotIds[i] = null;
                    continue;
                }

                var t = eq[i];
                bool ready = t.IsReady;

                sl.Art.enabled = true;
                // 장착을 바꾸면 칸의 주인이 바뀐다. 첫 프레임에만 배정하면 그대로 남는다.
                if (lastSlotIds[i] != t.Id)
                {
                    lastSlotIds[i] = t.Id;
                    sl.Art.sprite = ArtLibrary.Card(t.Id);
                }
                sl.Art.color = ready ? Color.white : new Color(0.45f, 0.45f, 0.5f, 1f);

                sl.Frame.color = ready ? UiTheme.AccentDim : UiTheme.Card;
                sl.Name.text = t.DisplayName;
                sl.Name.color = ready ? UiTheme.Text : UiTheme.TextDim;
                sl.Button.interactable = ready;

                // 쿨이 돌수록 차오른다. 가득 차면 준비 완료다.
                float f = t.Cooldown > 0.0
                    ? 1f - (float)(t.CooldownRemaining / t.Cooldown)
                    : 1f;
                sl.CoolFill.anchorMax = new Vector2(Mathf.Clamp01(f), 1f);
            }

            // 화면에 실제 계산값을 띄운다. 표시용 근사값이면 x0.98 같은 건 영영 안 보인다.
            // ★ 윷 배수까지 곱해서 띄운다. 화면이 실제 전투 계산과 다른 수를 말하면
            //   x0.98 같은 결함이 영영 안 보인다 — 그때는 사장님 눈이 잡았지만,
            //   화면이 근사값을 띄우고 있었다면 그것도 못 잡았다.
            multLabel.text = $"부적 x{controller.Talismans.DamageMultiplierAt(b.WaveHpRatio) * controller.YutRunMultiplier:F2}";

            // ── 오방색 ──
            var tr = controller.Tracks;
            if (tr != null)
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    var cell = tracks[i];
                    int lv = tr.GetLevel(cell.Track);
                    if (force || lv != lastLevels[i])
                    {
                        lastLevels[i] = lv;
                        cell.Level.text = "Lv" + lv;
                    }

                    // 판정은 UpgradeTracks가 한다. 여기서 비교식을 다시 쓰지 않는다.
                    bool afford = tr.CanAfford(cell.Track, b.Coin);
                    cell.Button.interactable = afford;
                    var c = cell.Block.color;
                    c.a = afford ? 1f : 0.45f;
                    cell.Block.color = c;
                }
            }

            // 비용은 트랙과 무관하므로 아무 트랙이나 물어보면 된다.
            if (tr != null)
            {
                string cost = tr.NextCost(EconomyCore.Track.Blue).ToString();
                if (force || cost != lastCosts[0])
                {
                    lastCosts[0] = cost;
                    costLabel.text = "다음 강화  " + cost;
                }
            }

            // ── 전장 ──
            //
            // ★ 발사 이벤트에 붙는 것은 여기서 한다. 러너가 새로 만들어지면 다시 붙어야 하고,
            //   Bind는 같은 러너면 아무 일도 안 하므로 매 프레임 불러도 안전하다.
            field.Bind(b);
            field.Tick(b, Time.deltaTime);

            // 해치는 늘 숨을 쉰다. 1픽셀도 안 움직이는 것이 살아 있지 않다는 가장 큰 신호다.
            if (haetaeRt != null)
            {
                if (field.ConsumeShotPulse()) lunge = 1f;
                if (lunge > 0f) lunge -= Time.deltaTime * 6f;
                float breathe = Mathf.Sin(Time.time * 1.9f) * 6f;
                float push = Mathf.Max(0f, lunge) * 26f;       // 발사하면 앞(위)으로 튄다
                haetaeRt.anchoredPosition = new Vector2(0f, UiTheme.GapWide + breathe + push);
                float k = 1f + Mathf.Max(0f, lunge) * 0.05f;
                haetaeRt.localScale = new Vector3(k, k, 1f);
            }

            // ── 조건부 ──
            bool walled = controller.CanRebirth;
            bool offline = controller.HasPendingOffline;
            if (rebirthGo.activeSelf != walled) rebirthGo.SetActive(walled);
            if (offlineGo.activeSelf != offline) offlineGo.SetActive(offline);

            var p = controller.AscendProgress();
            ascendLabel.text = p.waveOk && p.coreOk ? "승천 준비됨"
                             : p.waveOk            ? "승천 — 도깨비불 부족"
                             : p.coreOk            ? "승천 — 웨이브 부족"
                                                   : "";
        }

        private void Summon(int slot)
        {
            // 성공 여부는 TalismanSystem이 판정한다. 실패해도 화면은 다음 프레임에 알아서 맞는다.
            var eq = controller.Talismans != null ? controller.Talismans.Equipped : null;
            if (eq == null || slot < 0 || slot >= eq.Count) return;

            var t = eq[slot];
            if (!controller.SummonTalisman(slot, TalismanSystem.Lane.Middle)) return;

            // ★ 컷인은 여기서만 부른다 — 손가락이 닿은 경로다.
            //   TalismanSystem.OnSummoned에 붙이면 자동 소환에도 발화해서,
            //   자동화를 산 유저가 5초마다 컷인을 보게 된다. 산 편의가 벌이 된다.
            if (cutin != null) cutin.Play(t.Id, t.DisplayName);
        }

        /// <summary>티어를 숫자 대신 서사 언어로. "TIER 3"보다 "셋째 밤"이 이 게임에 맞다.</summary>
        private static string NightName(int tier)
        {
            switch (tier)
            {
                case 1: return "첫";
                case 2: return "둘째";
                case 3: return "셋째";
                case 4: return "넷째";
                case 5: return "다섯째";
                case 6: return "여섯째";
                default: return tier + "번째";
            }
        }

        // ─────────────────────────────────────────
        // uGUI 조립 도우미

        private Canvas NewCanvas()
        {
            var go = new GameObject("BattleCanvas", typeof(Canvas),
                                    typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 10;   // 부적 화면(50)보다 아래

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            // 세로 게임이다. 높이를 기준으로 맞춰야 기기가 길어져도 하단 버튼이 안 잘린다.
            scaler.matchWidthOrHeight = 1f;
            return c;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image AddImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static void SetImage(GameObject go, Color color)
        {
            var img = go.GetComponent<Image>();
            if (img != null) img.color = color;
        }

        private Text NewText(string name, Transform parent, int size, Color color)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// 상자 밖으로 절대 나가지 않는 글자.
        ///
        /// ★ 기본 NewText는 Overflow라 상자보다 길면 밖으로 흘러나간다.
        ///   전투 화면에서는 그게 옆 칸을 침범하고, 부적 화면에서는
        ///   맨 왼쪽 열이 스크롤 마스크에 잘려 **한 글자가 사라진다.**
        ///   실제로 "저승사자"가 "승사자"로 보였다.
        ///
        ///   이름은 길이를 우리가 못 정한다 — 부적이 늘면 더 긴 이름이 온다.
        ///   그래서 상자에 맞춰 글자가 줄어드는 쪽을 택한다. 작아진 글자는 읽히지만
        ///   잘린 글자는 다른 낱말이 된다.
        /// </summary>
        private Text NewFittedText(string name, Transform parent, int size, Color color)
        {
            var t = NewText(name, parent, size, color);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 14;
            t.resizeTextMaxSize = size;
            return t;
        }

        private Button NewButton(string name, Transform parent, string label, int size)
        {
            var rt = NewRect(name, parent);
            var img = AddImage(rt, UiTheme.Panel);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var t = NewText("Label", rt, size, UiTheme.Background);
            Stretch(t.rectTransform, 0f, 0f);
            t.text = label;
            return btn;
        }

        private static void Stretch(RectTransform rt, float pad, float padY)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, padY);
            rt.offsetMax = new Vector2(-pad, -padY);
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max,
                                   Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}

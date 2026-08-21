using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleDefense.Art;
using IdleDefense.Economy;
using IdleDefense.Game;

namespace IdleDefense.UI
{
    /// <summary>
    /// 부적 화면. 17종을 보여주고 5칸을 갈아끼운다.
    ///
    /// ★ 프리팹도 씬 편집도 필요 없다. 전부 코드로 조립한다.
    ///   빈 GameObject에 이 컴포넌트만 붙이면 끝이다.
    ///   프리팹으로 만들면 아트가 바뀔 때마다 에디터를 열어야 하고,
    ///   17종이 20종, 30종으로 늘 때마다 손으로 칸을 늘려야 한다.
    ///
    /// ★ 이 화면은 아무것도 판정하지 않는다.
    ///   장착 규칙(중복 금지·슬롯 상한·정렬·해금)은 전부 GameController가 판정한다.
    ///   여기서는 목록을 만들어 넘기고, 돌아온 결과를 다시 그린다.
    ///   DebugHud와 같은 원칙이다 — 화면이 자체 계산을 하면
    ///   "화면은 되는데 실제로는 안 되는" 상태를 아무도 못 잡는다.
    ///
    /// ★ 아트가 없어도 돈다. ArtLibrary가 플레이스홀더를 준다.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class TalismanScreen : MonoBehaviour
    {
        [SerializeField] private GameController controller;

        [Header("글꼴")]
        [Tooltip("비워두면 에디터에서 OS 글꼴을 빌려 쓴다. " +
                 "모바일 빌드에는 한글이 든 폰트 에셋을 반드시 넣어야 한다.")]
        [SerializeField] private Font font;

        [Header("표시")]
        [Tooltip("켜면 부팅 직후 부적 화면이 열린 채로 시작한다. 보통은 꺼둔다.")]
        [SerializeField] private bool openOnStart = false;

        private Canvas canvas;
        private RectTransform panel;
        private GameObject openButton;
        private RectTransform grid;
        private Text header;

        /// <summary>카드 하나가 다시 그릴 때 필요한 것들.</summary>
        private struct CardRefs
        {
            public string Id;
            public Image Frame;
            public Image Art;
            public Text Name;
            public Text Sub;
            public Button Button;
        }

        private readonly List<CardRefs> cards = new List<CardRefs>(20);

        // ─────────────────────────────────────────

        private void Awake()
        {
            if (controller == null) controller = FindObjectOfType<GameController>();
            if (controller == null)
            {
                Debug.LogError("[TalismanScreen] GameController가 없습니다.");
                enabled = false;
                return;
            }

            font = UiTheme.ResolveFont(font);
            WarnIfNoEventSystem();
            Build();
            SetOpen(openOnStart);
        }

        private void OnEnable()
        {
            if (controller != null) controller.OnTalismanChanged += HandleTalismanChanged;
            UiScreens.Changed += RefreshOpener;
            RefreshOpener();
        }

        private void OnDisable()
        {
            if (controller != null) controller.OnTalismanChanged -= HandleTalismanChanged;
            UiScreens.Changed -= RefreshOpener;

            // 화면이 열린 채로 꺼지면 디버그 HUD가 영영 숨은 채 남는다.
            UiScreens.SetOpen(this, false);
        }

        /// <summary>
        /// 여는 버튼은 **아무 전체화면도 안 열려 있을 때만** 보인다.
        /// 내가 닫혀 있다는 것만으로는 부족하다 — 윷 화면이 열려 있으면
        /// 내 버튼이 그 위에 뜬다. 실제로 반대 방향으로 그런 일이 있었다.
        /// </summary>
        private void RefreshOpener()
        {
            if (openButton != null) openButton.SetActive(UiScreens.CanShowOpener());
        }

        private void HandleTalismanChanged(GameController.TalismanChange _) => Refresh();

        /// <summary>
        /// 버튼이 눌리려면 씬에 EventSystem이 있어야 한다.
        ///
        /// ★ 여기서 직접 만들지 않는다.
        ///   이 프로젝트에는 새 Input System 패키지가 들어 있어서
        ///   구형 StandaloneInputModule을 코드로 붙이면 런타임에 예외가 난다.
        ///   어느 입력 모듈이 맞는지는 프로젝트 설정에 달려 있고,
        ///   에디터의 GameObject > UI > Event System 메뉴가 그걸 알아서 골라준다.
        ///   그래서 만들지 않고 알려만 준다.
        /// </summary>
        private static void WarnIfNoEventSystem()
        {
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;

            Debug.LogWarning(
                "[TalismanScreen] 씬에 EventSystem이 없어 버튼이 눌리지 않습니다.\n" +
                "  Hierarchy 우클릭 > UI > Event System 을 추가해주세요.");
        }

        // ─────────────────────────────────────────
        // 조립

        private void Build()
        {
            canvas = NewCanvas();

            // ★ 여는 버튼은 캔버스에 남겨두고 패널만 껐다 켠다.
            //   캔버스째 끄면 다시 열 방법이 사라진다 — 실제로 한 번 그랬다.
            //   자리는 전투 HUD 바로 위 오른쪽이다.
            //   화면 맨 아래는 오방색 트랙이 쓰고, 왼쪽 아래는 DebugHud가 쓴다.
            //   눈대중으로 잡으면 흑 트랙 위에 얹힌다 — 실제로 한 번 그랬다.
            float y = BattleScreen.HudBottomHeight + UiTheme.Gap;
            var open = NewButton("Open", canvas.transform, "부적", UiTheme.FontName);
            openButton = open.gameObject;
            Anchor(open.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                   new Vector2(-150f, y), new Vector2(-UiTheme.GapWide, y + 72f));
            open.onClick.AddListener(() => SetOpen(true));

            // ★ 화면을 통째로 덮는다. 24px만 남겨두면 그 틈으로 뒤의 오방색 트랙이 비쳐
            //   "덜 닫힌 창"처럼 보인다. 전체화면은 전체를 덮어야 전체화면이다.
            panel = NewRect("Panel", canvas.transform);
            Stretch(panel, 0f, 0f);
            AddImage(panel, UiTheme.Background);

            header = NewText("Header", panel, UiTheme.FontTitle, UiTheme.Text);
            Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(0.6f, 1f),
                   new Vector2(UiTheme.GapWide, -92f), new Vector2(0f, -20f));
            header.alignment = TextAnchor.MiddleLeft;

            var close = NewButton("Close", panel, "닫기", UiTheme.FontName);
            Anchor(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                   new Vector2(-180f, -92f), new Vector2(-UiTheme.GapWide, -20f));
            close.onClick.AddListener(() => SetOpen(false));

            // 스크롤 영역 — 17종이 화면에 다 안 들어간다. 20종, 30종이 되면 더 그렇다.
            var scrollGo = NewRect("Scroll", panel);
            Anchor(scrollGo, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(UiTheme.GapWide, UiTheme.GapWide), new Vector2(-UiTheme.GapWide, -104f));

            var scroll = scrollGo.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var viewport = NewRect("Viewport", scrollGo);
            Stretch(viewport, 0f, 0f);
            AddImage(viewport, new Color(0f, 0f, 0f, 0.001f));   // Mask에는 Graphic이 필요하다
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            grid = NewRect("Grid", viewport);
            grid.anchorMin = new Vector2(0f, 1f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 1f);
            scroll.content = grid;

            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(UiTheme.CardW, UiTheme.CardH);
            layout.spacing = new Vector2(UiTheme.Gap, UiTheme.Gap);

            // ★ 좌우 여백이 0이면 맨 왼쪽 열이 스크롤 마스크에 딱 붙는다.
            //   그러면 상자를 조금이라도 넘치는 글자가 **잘린 채로** 그려진다.
            //   "저승사자"가 "승사자"로 보였던 원인의 절반이 이 0이었다.
            layout.padding = new RectOffset(12, 12, 0, (int)UiTheme.Gap);
            layout.constraint = GridLayoutGroup.Constraint.Flexible;

            // 남는 폭을 오른쪽에 몰아두지 않는다. 열 수가 기기마다 달라지므로
            // 왼쪽 정렬이면 좁은 기기에서 화면이 한쪽으로 쏠려 보인다.
            layout.childAlignment = TextAnchor.UpperCenter;

            var fitter = grid.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var id in TalismanCatalog.DisplayOrder)
                cards.Add(BuildCard(id));

            Refresh();
        }

        private CardRefs BuildCard(string id)
        {
            var t = TalismanCatalog.Get(id);

            var root = NewRect("Card_" + id, grid);
            var frame = AddImage(root, UiTheme.Card);

            var artRt = NewRect("Art", root);
            Anchor(artRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(10f, 84f), new Vector2(-10f, -10f));
            var art = artRt.gameObject.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;

            var name = NewFittedText("Name", root, UiTheme.FontName, UiTheme.Text);
            Anchor(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(8f, 46f), new Vector2(-8f, 84f));
            name.text = t.DisplayName;

            // 해금 조건이 들어오는 칸이라 "티어 5 승천"처럼 길어질 수 있다.
            // 여기도 맞춰 줄어드는 쪽이다 — 조건이 잘리면 왜 못 쓰는지 못 읽는다.
            var sub = NewFittedText("Sub", root, UiTheme.FontTiny, UiTheme.TextDim);
            Anchor(sub.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(8f, 10f), new Vector2(-8f, 44f));

            var btn = root.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            btn.onClick.AddListener(() => Toggle(id));

            return new CardRefs { Id = id, Frame = frame, Art = art, Name = name, Sub = sub, Button = btn };
        }

        // ─────────────────────────────────────────
        // 상태 반영

        /// <summary>
        /// 다시 그린다. 상태는 전부 GameController에서 읽는다 — 여기서 만들지 않는다.
        /// </summary>
        public void Refresh()
        {
            var state = controller.State;
            int tier = state != null ? state.tier : 1;
            int best = state != null ? state.bestWave : 0;
            var equipped = state != null && state.equippedTalismans != null
                ? state.equippedTalismans : new string[0];

            if (header != null)
                header.text = $"부적   {equipped.Length} / {TalismanSystem.MaxSlots}";

            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                var t = TalismanCatalog.Get(c.Id);

                bool unlocked = TalismanCatalog.IsUnlocked(c.Id, tier, best);
                bool on = System.Array.IndexOf(equipped, c.Id) >= 0;

                c.Art.sprite = ArtLibrary.Card(c.Id);
                c.Art.color = unlocked ? Color.white : new Color(0.35f, 0.35f, 0.4f, 1f);

                c.Frame.color = !unlocked ? UiTheme.CardLocked
                              : on        ? UiTheme.AccentDim
                                          : UiTheme.Card;

                c.Name.color = unlocked ? UiTheme.Text : UiTheme.TextLocked;
                c.Button.interactable = unlocked;

                if (!unlocked)
                {
                    // ★ '왜 못 쓰는가'를 반드시 말해준다.
                    //   조건을 안 보여주면 유저는 버그로 받아들인다.
                    c.Sub.text = TalismanCatalog.UnlockHint(c.Id, tier, best);
                    c.Sub.color = UiTheme.TextLocked;
                }
                else
                {
                    c.Sub.text = on ? "장착 중" : AxisName(t.Effect);
                    c.Sub.color = on ? UiTheme.Accent : UiTheme.TextDim;
                }
            }
        }

        private void Toggle(string id)
        {
            var state = controller.State;
            var list = new List<string>(
                state != null && state.equippedTalismans != null
                    ? state.equippedTalismans : new string[0]);

            if (list.Contains(id)) list.Remove(id);
            else if (list.Count < TalismanSystem.MaxSlots) list.Add(id);
            else return;   // 꽉 찼다. 무엇을 뺄지는 유저가 정한다

            // 판정은 전부 컨트롤러가 한다. 결과는 OnTalismanChanged로 돌아온다.
            controller.ApplyLoadout(list.ToArray());
            Refresh();     // 같은 구성이면 이벤트가 안 오므로 여기서도 한 번 그린다
        }

        public void SetOpen(bool open)
        {
            if (panel != null) panel.gameObject.SetActive(open);

            // 여는 버튼과 디버그 HUD는 UiScreens가 판정한다. 여기서 직접 끄지 않는다.
            UiScreens.SetOpen(this, open);
            RefreshOpener();

            if (open) Refresh();
        }

        public void Toggle() => SetOpen(panel == null || !panel.gameObject.activeSelf);

        private static string AxisName(TalismanEffect e)
        {
            switch (e)
            {
                case TalismanEffect.Damage:      return "피해";
                case TalismanEffect.Execute:     return "즉시삭제";
                case TalismanEffect.Amplify:     return "증폭";
                case TalismanEffect.Duplicate:   return "복제";
                case TalismanEffect.Haste:       return "쿨감";
                case TalismanEffect.Extend:      return "지속연장";
                case TalismanEffect.Random:      return "변덕";
                case TalismanEffect.Stack:       return "누적";
                case TalismanEffect.Mature:      return "만숙";
                case TalismanEffect.Auto:        return "자동";
                case TalismanEffect.Feed:        return "희생";
                case TalismanEffect.Conditional: return "조건부";
                default:                         return "-";
            }
        }

        // ─────────────────────────────────────────
        // uGUI 조립 도우미

        private Canvas NewCanvas()
        {
            var go = new GameObject("TalismanCanvas", typeof(Canvas),
                                    typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 50;   // DebugHud(IMGUI)보다 아래에 둔다

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
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
        /// 상자 밖으로 절대 나가지 않는 글자. BattleScreen과 같은 규칙이다.
        ///
        /// ★ 이 화면에서 특히 중요하다 — 카드가 스크롤 마스크 안에 있어서
        ///   넘친 글자가 흘러나오는 게 아니라 **잘려 사라진다.**
        ///   맨 왼쪽 열의 "저승사자"가 "승사자"로, "어둑시니"가 "둑시니"로 보였다.
        ///   잘린 글자는 작아진 글자보다 나쁘다. 다른 낱말이 되기 때문이다.
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

            var t = NewText("Label", rt, size, UiTheme.Text);
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

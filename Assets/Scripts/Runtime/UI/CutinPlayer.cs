using UnityEngine;
using UnityEngine.UI;
using IdleDefense.Art;
using IdleDefense.Economy;

namespace IdleDefense.UI
{
    /// <summary>
    /// 소환 컷인. 부적을 누르면 그 인물이 화면 왼쪽에서 밀려 들어왔다 사라진다.
    ///
    /// ★ 존재 이유는 연출이 아니라 계약이다.
    ///   마케팅 기준 3장의 절대 규칙 — **"광고에 나온 것은 게임 안에 있어야 한다."**
    ///   페인터리 미남으로 광고하고 게임 안에는 200픽셀 카드만 있으면
    ///   모바일에서 가장 흔한 리뷰 폭탄 사유이고 스토어 정책 위반 소지가 있다.
    ///   이 화면이 그 규칙을 지키는 유일한 자리다.
    ///
    /// ★ 게임을 멈추지 않는다.
    ///   방치형은 화면을 안 보고 있어도 굴러가야 한다. 컷인이 입력을 막거나
    ///   시간을 세우면 그 순간 방치형이 아니게 된다.
    ///   그래서 raycastTarget은 전부 꺼져 있고 Time.timeScale은 건드리지 않는다.
    ///
    /// ★ 자동 소환에는 뜨지 않는다.
    ///   TalismanSystem.OnSummoned는 자동 소환에도 발화한다. 거기에 붙이면
    ///   자동화를 산 유저가 5초마다 컷인을 본다 — 산 편의가 벌이 된다.
    ///   그래서 BattleScreen의 **손가락이 닿은 경로**에서만 Play()를 부른다.
    ///
    /// ★ 짧다. 0.75초.
    ///   부적 5개면 런 내내 수십 번 뜬다. 1초를 넘기는 순간 자산이 아니라 방해가 된다.
    ///   그리고 화면 아래쪽만 쓴다 — 위쪽 전장은 계속 보여야 한다.
    ///
    /// ★ 컷인 아트는 배경을 살린다 — 잘라낸 인물이 아니라 '한 장의 그림'이다.
    ///   그래서 액자로 그린다. 인물만 오려 띄우면 캐릭터별 배경 색조
    ///   (저승사자=남색, 도깨비=검은 초록, 구미호=거의 검정 …)가 통째로 버려지는데,
    ///   그 색조는 마케팅 기준 5장이 '로스터에서 서로 잡아먹지 않도록' 배정한 값이다.
    ///   배경을 살리면 컷인 자체가 캐릭터를 구분하는 신호를 하나 더 갖는다.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public class CutinPlayer : MonoBehaviour
    {
        [Header("연출")]
        [Tooltip("전체 길이(초). 0.75 근처를 권장한다. 1초를 넘기면 방해가 된다.")]
        [SerializeField] private float duration = 0.75f;

        [Tooltip("끄면 컷인이 아예 나오지 않는다. 유저 설정으로 노출할 값이다.")]
        [SerializeField] private bool enabledByUser = true;

        [Header("글꼴")]
        [SerializeField] private Font font;

        private Canvas canvas;
        private RectTransform root;     // 전체를 껐다 켠다
        private RectTransform band;     // 어두운 띠
        private RectTransform frame;    // 그림 액자 테두리
        private RectTransform portrait;
        private Image portraitImage;
        private Image frameImage;
        private Image bandImage;
        private Text nameText;

        private float t = -1f;          // 음수 = 재생 중 아님

        // 컷인이 쓰는 세로 구간. 전장 위쪽을 가리지 않도록 하단에 붙인다.
        private const float BandBottom = BattleScreen.HudBottomHeight + 8f;
        private const float BandHeight = 460f;

        private void Awake()
        {
            font = UiTheme.ResolveFont(font);
            Build();
            root.gameObject.SetActive(false);
        }

        /// <summary>
        /// 컷인을 시작한다. 이미 재생 중이면 새 것으로 갈아탄다 —
        /// 큐에 쌓으면 부적을 연달아 누른 유저가 지나간 소환의 컷인을 보게 된다.
        /// </summary>
        public void Play(string talismanId, string displayName)
        {
            if (!enabledByUser || string.IsNullOrEmpty(talismanId)) return;

            portraitImage.sprite = ArtLibrary.Cutin(talismanId);
            nameText.text = displayName;
            t = 0f;
            root.gameObject.SetActive(true);
            Step(0f);
        }

        private void Update()
        {
            if (t < 0f) return;
            t += Time.unscaledDeltaTime;      // 배속·일시정지와 무관하게 같은 길이로
            if (t >= duration) { t = -1f; root.gameObject.SetActive(false); return; }
            Step(t / duration);
        }

        /// <summary>
        /// p = 0~1. 앞 20%는 들어오고, 뒤 30%는 나간다. 가운데는 멈춰 있다.
        /// 멈추는 구간이 있어야 얼굴이 읽힌다 — 계속 움직이면 아무도 못 본다.
        /// </summary>
        private void Step(float p)
        {
            const float In = 0.20f, Out = 0.70f;

            float slide, alpha;
            if (p < In)
            {
                float k = p / In;
                k = 1f - (1f - k) * (1f - k);           // ease-out
                slide = Mathf.Lerp(-420f, 0f, k);
                alpha = k;
            }
            else if (p < Out)
            {
                slide = 0f;
                alpha = 1f;
            }
            else
            {
                float k = (p - Out) / (1f - Out);
                slide = Mathf.Lerp(0f, 160f, k);        // 나갈 때는 반대로 밀린다
                alpha = 1f - k;
            }

            frame.anchoredPosition = new Vector2(slide, 0f);

            var c = portraitImage.color; c.a = alpha; portraitImage.color = c;
            var f = frameImage.color;    f.a = alpha; frameImage.color = f;
            var b = bandImage.color;     b.a = alpha * 0.55f; bandImage.color = b;
            var n = nameText.color;      n.a = alpha; nameText.color = n;
        }

        // ─────────────────────────────────────────

        private void Build()
        {
            var go = new GameObject("CutinCanvas", typeof(Canvas),
                                    typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;   // 전투 화면(10) 위, 부적 화면(50) 아래

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 1f;

            // GraphicRaycaster가 붙어 있어도 자식이 전부 raycastTarget=false라
            // 클릭은 아래 화면으로 그대로 지나간다.
            root = NewRect("Root", go.transform);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.offsetMin = new Vector2(0f, BandBottom);
            root.offsetMax = new Vector2(0f, BandBottom + BandHeight);

            band = NewRect("Band", root);
            Stretch(band);
            bandImage = band.gameObject.AddComponent<Image>();
            bandImage.color = new Color(UiTheme.Background.r, UiTheme.Background.g,
                                        UiTheme.Background.b, 0.55f);
            bandImage.raycastTarget = false;

            // 액자 — 그림이 배경을 갖고 있으므로 가장자리를 끊어줘야 화면에 얹힌다.
            frame = NewRect("Frame", root);
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 0.5f);
            frame.offsetMin = new Vector2(UiTheme.Gap, UiTheme.Gap);
            frame.offsetMax = new Vector2(UiTheme.Gap + BandHeight, -UiTheme.Gap);
            frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = UiTheme.Accent;
            frameImage.raycastTarget = false;

            portrait = NewRect("Portrait", frame);
            Stretch(portrait);
            portrait.offsetMin = new Vector2(3f, 3f);
            portrait.offsetMax = new Vector2(-3f, -3f);
            portraitImage = portrait.gameObject.AddComponent<Image>();
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            // ★ 이름은 액자 오른쪽, 밴드 세로 가운데.
            //   전에는 밴드 바닥에 붙어 있어서 액자 모서리와 겹쳐 보였고,
            //   무엇보다 **배경 그림 위에 맨 글자로 얹혀 있어 배경에 따라 안 읽혔다.**
            //   컷인은 0.75초만 떠 있다 — 그 안에 못 읽으면 없는 것과 같다.
            //   그래서 자리를 올리고 검은 외곽선을 둘렀다. 밴드 반투명만으로는 부족했다.
            nameText = NewText("Name", root, UiTheme.FontHuge, UiTheme.Text);
            nameText.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameText.rectTransform.anchorMax = new Vector2(1f, 0f);
            nameText.rectTransform.offsetMin = new Vector2(BandHeight + 56f, 150f);
            nameText.rectTransform.offsetMax = new Vector2(-UiTheme.GapWide, 270f);
            nameText.alignment = TextAnchor.MiddleLeft;

            var nameEdge = nameText.gameObject.AddComponent<Outline>();
            nameEdge.effectColor = new Color(0f, 0f, 0f, 0.85f);
            nameEdge.effectDistance = new Vector2(3f, -3f);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private Text NewText(string name, Transform parent, int size, Color color)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}

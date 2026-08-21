using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleDefense.Art;
using IdleDefense.Economy;
using IdleDefense.Game;

namespace IdleDefense.UI
{
    /// <summary>
    /// 윷놀이 화면. 도깨비와 노는 자리다.
    ///
    /// ★ 이 화면은 굴리지 않는다.
    ///   난수는 GameController가 소유한다. 화면이 굴리면 결과를 만들어낼 수 있고,
    ///   그러면 밸런스가 화면 코드에 종속된다. 여기서는 ThrowYut()을 부르고
    ///   돌아온 결과를 연출할 뿐이다. 부적·전투 화면과 같은 원칙이다.
    ///
    /// ★ 부르기는 '모' 하나만 제안한다.
    ///   40만 판 실측 — 모 부르기 9.2% / 안 부르기 5.4% / **윷 부르기 1.5%**.
    ///   윷은 2.6% 확률을 맞히려다 나머지를 다 버린다. 함정이다.
    ///   다섯 개를 보여주고 넷이 함정이면 그건 선택지가 아니라 벌이다.
    ///   버튼 하나면 결정이 홀짝이 된다 — "모 부를래, 말래?"
    ///
    /// ★ 보상 상한을 넘어도 놀 수 있다.
    ///   상한을 채우려고 하루 20판을 던지면 그건 재미가 아니라 노동이고,
    ///   데일리 숙제가 된 미니게임은 그만두게 된다.
    ///   **도깨비는 재물을 주려고 노는 게 아니라 놀고 싶어서 논다.**
    ///   그리고 보상이 끊긴 뒤에도 던지는 비율이,
    ///   이 미니게임이 실제로 재미있는지를 말해주는 유일한 지표가 된다.
    /// </summary>
    [DefaultExecutionOrder(125)]
    public class YutScreen : MonoBehaviour
    {
        [SerializeField] private GameController controller;
        [SerializeField] private Font font;

        [Header("연출")]
        [Tooltip("첫 가락이 서기까지의 체공 시간(초). 나머지 셋은 0.065초씩 늦게 선다.\n" +
                 "던지기 → 글자까지 = 이 값 + 0.195 + 0.06.\n" +
                 "0.5면 약 0.75초. 1초를 넘기면 연타할 마음이 사라진다.")]
        [SerializeField] private float rollSeconds = 0.5f;

        private Canvas canvas;
        private Image panelImage;
        private RectTransform panel;
        private GameObject openButton;

        private Text titleText;
        private Text bigText;        // 도 개 걸 윷 모
        private Text againText;      // 한 번 더!
        private Text multText;       // 이번 밤 부적 x1.60
        private Text limitText;      // 오늘 보상 1 / 2
        private Text noteText;
        private Button callButton;
        private Image callFill;
        private Image callBox;
        private Text callLabel;
        private Button throwButton;
        private Text throwLabel;

        /// <summary>
        /// 윷가락 하나. **배와 등이 있다는 것이 이 게임의 전부다.**
        ///
        /// 지금까지는 넷 다 똑같은 갈색 막대였고, 결과는 큰 글자가 통보했다.
        /// 그러면 애니메이션이 아무리 길어도 유저는 글자만 기다린다.
        /// 가락이 배(평평·밝음)와 등(둥금·어두움)으로 갈려 서야
        /// **유저가 글자보다 먼저 답을 읽고**, 그 반 박자가 재미가 된다.
        /// </summary>
        private struct Stick
        {
            public RectTransform Rt;
            public Image Body;
            public GameObject Mark;   // 백도 가락의 X. 한 가락에만 있다
            public Image Dust;    // 착지 먼지. 가락과 같이 돌면 안 되므로 형제로 둔다
            public float BaseX;

            // 던질 때마다 정해지는 것들
            public float Flight;  // 이 가락이 서는 시각
            public float Spin;    // 총 회전각(360의 배수)
            public float Drift;   // 좌우로 흩어지는 거리
            public bool  Belly;   // 배로 서는가
        }

        /// <summary>
        /// 가락 하나의 크기와 간격.
        ///
        /// ★ 230×74에 간격 140이었을 때 공중에서 서로 겹쳤다.
        ///   긴 막대가 돌면 가로로 길이만큼 쓸고 지나간다 — 230짜리가 140칸 안에 못 있는다.
        ///   짧게 줄이고 간격을 좁히는 것이 답이지, 흩어지는 거리를 키우는 게 아니다.
        /// </summary>
        private const float StickW = 66f, StickH = 200f, SlotW = 130f;

        private readonly Stick[] sticks = new Stick[YutGame.StickCount];
        private readonly int[] order = new int[YutGame.StickCount];

        /// <summary>
        /// 백도 가락. **넷 중 하나에만 X가 새겨져 있다.**
        ///
        /// ★ 고증이다. 윷가락 넷 중 하나에 X(또는 점)를 새겨 '백도'라 부르고,
        ///   그것만 배로 서면 말을 한 칸 물린다. 우리 게임엔 말판이 없으니
        ///   **규칙으로는 아무 의미가 없다.** 그래도 새겨 두는 이유는 두 가지다 —
        ///   윷을 놀아본 사람이 첫눈에 "윷가락이네" 하고 알아보고,
        ///   넷이 구를 때 하나가 구별돼 눈이 궤적을 따라갈 수 있다.
        ///   의미 없는 디테일이 아니라 **읽는 데 쓰이는 디테일**이다.
        /// </summary>
        private const int MarkedStick = 1;

        // 있으면 쓰고 없으면 색칠한 사각형으로 돈다. ArtLibrary.YutStick은 null을 준다.
        private Sprite spBack, spBelly, spBellyMark;

        // 공중에서는 아직 모른다. 착지하는 순간에만 갈린다.
        private static readonly Color StickAir   = new Color(0.62f, 0.47f, 0.29f, 1f);
        private static readonly Color StickBack  = new Color(0.36f, 0.26f, 0.16f, 1f);
        private static readonly Color StickBelly = new Color(0.91f, 0.81f, 0.61f, 1f);

        private readonly List<Text> chain = new List<Text>(8);
        private RectTransform chainRow;

        /// <summary>
        /// 결과 글자 크기. UiTheme 사다리(최대 52) 밖의 값이다.
        ///
        /// ★ 사다리를 어기는 유일한 자리이며, 어기는 것이 맞다.
        ///   "모"는 읽는 글자가 아니라 **터지는 그림**이다. 사다리는 위계를 위한 것이고
        ///   이건 위계 밖에 있다 — 이 화면에 다른 글자가 뭐가 있든 상관없이 가장 크다.
        ///   다른 화면에 이런 자리를 또 만들지 말 것. 둘이 되는 순간 그냥 큰 글자가 된다.
        /// </summary>
        private const int YutLetterSize = 220;

        private bool callMo;
        private float rolling = -1f;
        private YutResult pending;
        private bool letterShown;
        private bool punchy;        // 윷·모인가. 연출을 가르는 유일한 스위치

        // ★ 판이 끝나는 순간과 유저가 결과를 보는 순간은 다르다.
        //   GameController는 마지막 던지기 직후 바로 OnYutFinished를 쏜다.
        //   그걸 그 자리에서 그리면 윷가락이 아직 구르는데 배수가 먼저 뜬다.
        //   결과보다 결산이 먼저 보이면 던진 의미가 없다. 그래서 들고 있다가 같이 푼다.
        private bool summaryPending;
        private GameController.YutSummary summary;

        // ─────────────────────────────────────────

        private void Awake()
        {
            if (controller == null) controller = FindObjectOfType<GameController>();
            if (controller == null)
            {
                Debug.LogError("[YutScreen] GameController가 없습니다.");
                enabled = false;
                return;
            }

            font = UiTheme.ResolveFont(font);
            Build();
            SetOpen(false);
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.OnYutThrown += HandleThrown;
                controller.OnYutFinished += HandleFinished;
            }
            UiScreens.Changed += RefreshOpener;
            RefreshOpener();
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnYutThrown -= HandleThrown;
                controller.OnYutFinished -= HandleFinished;
            }
            UiScreens.Changed -= RefreshOpener;
            UiScreens.SetOpen(this, false);
        }

        /// <summary>
        /// 여는 버튼은 **아무 전체화면도 안 열려 있을 때만** 보인다.
        /// 내가 닫혀 있다는 것만으로는 부족했다 — 부적 화면 위에 이 버튼이 떠 있었다.
        /// 정렬 순서(55 &gt; 50)로는 못 막는다. 순서가 아니라 정책 문제다.
        /// </summary>
        private void RefreshOpener()
        {
            if (openButton != null) openButton.SetActive(UiScreens.CanShowOpener());
        }

        // ─────────────────────────────────────────
        // 판 진행

        private void Throw()
        {
            // ★ 글자가 뜬 뒤에는 팝이 끝나기 전에도 다시 던질 수 있다.
            //   "계속 눌러보고 싶게" 만드는 건 연출의 화려함이 아니라 **다시 누르기까지의 시간**이다.
            //   연출이 끝날 때까지 손을 묶으면 두세 판 만에 기다림이 되고, 기다림은 그만두게 만든다.
            if (rolling >= 0f && !letterShown) return;

            // 새 판이면 지난 판의 흔적을 지운다.
            // ThrowYut()이 알아서 BeginYut()을 부르므로 여기서 판을 열지는 않는다 —
            // 판을 여는 곳이 둘이 되면 어느 쪽이 먼저인지 화면 코드가 알아야 한다.
            if (!controller.YutInProgress)
            {
                ClearChain();
                multText.text = "";
            }

            controller.ThrowYut(callMo ? YutCall.Mo : YutCall.None);
        }

        /// <summary>
        /// 배(평평한 면)가 위로 온 가락 수. **이게 곧 결과다.**
        ///
        /// 도 1 · 개 2 · 걸 3 · 윷 4 · 모 0(넷 다 등).
        /// `YutGame`이 등 개수로 굴리고, 화면은 결과에서 배 개수를 되짚는다.
        ///
        /// ★ 화면은 **몇 개가 배인지 정하지 않는다.** 결과에서 역산할 뿐이다.
        ///   어느 가락이 배인지만 고르고, 그건 순수한 연출이라 결과를 못 바꾼다.
        ///   이 경계를 지키는 한 "화면은 굴리지 않는다"는 계약은 그대로다.
        /// </summary>
        private static int Bellies(YutResult r)
        {
            switch (r)
            {
                case YutResult.Do:   return 1;
                case YutResult.Gae:  return 2;
                case YutResult.Geol: return 3;
                case YutResult.Yut:  return 4;
                default:             return 0;   // 모
            }
        }

        /// <summary>
        /// 한 번 굴렸다. 여기서 **이번 던지기의 연출을 통째로 정해둔다.**
        /// 매 프레임 난수를 뽑으면 가락이 떨리기만 하고 궤적이 안 생긴다.
        /// </summary>
        private void HandleThrown(YutResult r, bool again)
        {
            pending = r;
            rolling = 0f;
            letterShown = false;
            punchy = YutScoring.Pips(r) > 0;      // 윷·모만 크게 터진다

            bigText.text = "";
            bigText.rectTransform.localScale = Vector3.one;
            againText.text = "";
            throwButton.interactable = false;
            callButton.interactable = false;      // 던진 뒤에 부르는 건 부르기가 아니다

            // ── 어느 가락이 배로 설지 고른다 ──
            int bellies = Bellies(r);
            for (int i = 0; i < sticks.Length; i++) sticks[i].Belly = false;
            for (int picked = 0; picked < bellies; )
            {
                int k = Random.Range(0, sticks.Length);
                if (sticks[k].Belly) continue;
                sticks[k].Belly = true;
                picked++;
            }

            // ── 착지 순서를 섞는다 ──
            //
            // ★ 순서를 조작하지 않는 것이 중요하다.
            //   "잭팟일 때만 같은 면을 먼저 보여준다" 같은 연출을 넣고 싶어지지만,
            //   그러면 유저가 몇 판 만에 패턴을 읽고 **그 순간 모든 긴장이 죽는다.**
            //   무작위로 두면 등 셋이 먼저 서는 일이 저절로 생기고,
            //   그때 유저가 "모인가?" 하고 마지막 하나를 노려보게 된다.
            //   그 1초가 이 미니게임의 전부다. 공짜로 얻는 것이니 손대지 말 것.
            for (int i = 0; i < sticks.Length; i++) order[i] = i;
            for (int i = sticks.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            float baseFlight = Mathf.Max(0.2f, rollSeconds);
            for (int slot = 0; slot < order.Length; slot++)
            {
                int i = order[slot];
                sticks[i].Flight = baseFlight + slot * Stagger;
                // 정수 바퀴로 끝낸다. 그래야 마지막에 각도를 억지로 튕겨 맞출 필요가 없다.
                sticks[i].Spin  = 360f * Random.Range(2, 4) * (Random.value < 0.5f ? -1f : 1f);
                sticks[i].Drift = Random.Range(-38f, 38f);
            }
        }

        private void HandleFinished(GameController.YutSummary s)
        {
            summary = s;
            summaryPending = true;
        }

        // ─────────────────────────────────────────
        // 던지는 그림
        //
        // ★ 이 연출이 노리는 것은 화려함이 아니라 **읽을 수 있는 카운트다운**이다.
        //   주사위·슬롯·가챠가 전부 같은 구조다 — 결과는 이미 정해져 있고,
        //   유저는 "곧 알게 된다"는 것만 안다. 그 사이가 길수록 지루하고,
        //   짧을수록 던진 것 같지가 않다.
        //
        // ★ 그래서 가락이 **하나씩 순서대로** 선다.
        //   넷이 동시에 서면 그건 물체 하나다. 0.065초씩 어긋나면 넷이 된다.
        //   셋이 선 시점에서 남은 하나가 결과를 가르고, 유저는 그걸 본다.
        //   **애니메이션이 답을 알려주는 게 아니라, 유저가 답을 읽는다.**
        //   큰 글자는 이미 읽은 것을 확인해주는 역할이지 통보가 아니다.

        /// <summary>
        /// 가락이 올라가는 최고 높이(1080 기준).
        ///
        /// ★ 200을 넘기면 최고점에서 가락 머리가 상단 바를 뚫는다.
///   가락 바닥(패널 위에서 700) − 이 높이 − 가락 길이의 절반이 상단 바 140보다
        ///   작아지는 순간 머리가 바를 뚫는다. 셋 중 하나를 바꾸면 나머지 둘을 다시 재야 한다.
        /// </summary>
        private const float TossRise    = 260f;
        /// <summary>가락 사이의 착지 간격. 이 값이 긴장의 길이를 정한다.</summary>
        private const float Stagger     = 0.065f;
        /// <summary>착지 눌림이 돌아오는 시간.</summary>
        private const float SquashTime  = 0.12f;
        /// <summary>마지막 가락이 선 뒤 글자가 뜨기까지. 짧은 정적이 있어야 글자가 산다.</summary>
        private const float LetterDelay = 0.06f;
        /// <summary>글자가 튀어나오는 시간.</summary>
        private const float LetterPop   = 0.14f;

        private void Update()
        {
            if (rolling < 0f) return;

            rolling += Time.unscaledDeltaTime;

            float lastLand = 0f;
            for (int i = 0; i < sticks.Length; i++)
            {
                var s = sticks[i];
                if (s.Rt == null) continue;
                if (s.Flight > lastLand) lastLand = s.Flight;

                float p = Mathf.Clamp01(rolling / Mathf.Max(0.05f, s.Flight));
                float e = EaseOutCubic(p);

                // 포물선. 등속으로 올렸다 내리면 던진 게 아니라 옮긴 것처럼 보인다.
                // 올라갈 때 느려지고 내려올 때 빨라지는 것 하나가 무게를 만든다.
                float rise = TossRise * 4f * p * (1f - p);
                float ang  = s.Spin * e;
                float sy   = 1f;

                if (p >= 1f)
                {
                    ang = 0f;
                    float q = Mathf.Clamp01((rolling - s.Flight) / SquashTime);
                    sy = 0.70f + 0.30f * EaseOutBack(q);   // 눌렸다가 돌아온다

                    // 먼지. **소리가 없는 동안은 이게 소리다.**
                    float a = 1f - q;
                    s.Dust.color = new Color(0.86f, 0.83f, 0.72f, 0.40f * a * a);
                    s.Dust.rectTransform.sizeDelta = new Vector2(22f + 110f * q, 6f);
                }

                // 피벗이 가운데이므로, 눌릴 때 바닥이 뜨지 않게 절반만큼 내려준다.
                s.Rt.anchoredPosition = new Vector2(s.BaseX + s.Drift * e,
                                                     StickH * 0.5f * sy + rise);
                s.Rt.localRotation = Quaternion.Euler(0f, 0f, ang);
                s.Rt.localScale = new Vector3(1f, sy, 1f);

                // ★ 면은 **착지하는 순간에만** 확정된다.
                //   공중에서 배·등이 보이면 눈이 못 따라가고, 따라가려다 실패하면 그냥 안 본다.
                //   중간색으로 돌다가 땅에 닿는 순간 뒤집히는 편이 훨씬 잘 읽힌다.
                bool landed = p >= 1f;
                ShowFace(s, landed, i == MarkedStick);
            }

            float letterAt = lastLand + LetterDelay;
            if (rolling < letterAt) return;

            if (!letterShown) { letterShown = true; Reveal(); }

            // 도·개·걸은 조용히 스며들고, 윷·모는 튀어나온다.
            // ★ 확률이 이분법이고 보상이 이분법이면 **연출도 이분법이어야 한다.**
            //   전부 화려하게 만들면 화려한 게 하나도 없어진다.
            float t = Mathf.Clamp01((rolling - letterAt) / LetterPop);
            if (punchy)
            {
                float k = 1f + 0.55f * (1f - EaseOutBack(t));
                bigText.rectTransform.localScale = new Vector3(k, k, 1f);
            }
            else
            {
                var c = bigText.color; c.a = t; bigText.color = c;
            }

            if (rolling >= letterAt + LetterPop)
            {
                rolling = -1f;
                bigText.rectTransform.localScale = Vector3.one;
                var c = bigText.color; c.a = 1f; bigText.color = c;
            }
        }

        /// <summary>가락이 다 섰다. 이제 읽은 것을 확인해준다.</summary>
        private void Reveal()
        {
            bool hit = callMo && pending == YutResult.Mo;

            bigText.text = YutGame.DisplayName(pending);
            var col = punchy ? UiTheme.Accent : UiTheme.TextDim;
            if (!punchy) col.a = 0f;              // 조용한 쪽은 스며들게
            bigText.color = col;

            AddChain(pending, hit);

            bool again = YutGame.ThrowsAgain(pending);
            againText.text = again ? "한 번 더!" : (hit ? "불렀다!" : "");
            throwLabel.text = again ? "한 번 더 던지기" : "한 판 더";
            if (!again) callMo = false;

            // 팝이 끝나기를 기다리지 않는다. 손은 여기서 풀린다.
            throwButton.interactable = true;
            callButton.interactable = true;
            RefreshCall();

            if (summaryPending)
            {
                summaryPending = false;
                multText.text = summary.Rewarded
                    ? $"이번 밤 부적  x{summary.Multiplier:F2}"
                    : "보상 없이 논 판";
                RefreshLimit();
            }
        }

        /// <summary>
        /// 가락 하나의 면을 그린다. **한 곳에서만 면을 정한다.**
        ///
        /// 공중(landed=false)에서는 중간색으로 둔다 — 아직 답이 아니다.
        /// 스프라이트가 있으면 그림으로, 없으면 색칠한 사각형으로 그린다.
        /// 둘 다 같은 자리에서 갈리므로 아트가 들어와도 코드가 안 바뀐다.
        /// </summary>
        private void ShowFace(Stick s, bool landed, bool marked)
        {
            var sprite = !landed ? spBack
                       : s.Belly ? (marked && spBellyMark != null ? spBellyMark : spBelly)
                                 : spBack;

            s.Body.sprite = sprite;
            s.Body.color = sprite != null
                ? (landed ? Color.white : StickAir)          // 그림이 있으면 공중만 어둡게 눌러둔다
                : (!landed ? StickAir : (s.Belly ? StickBelly : StickBack));

            // 그림이 X를 직접 갖고 있으면 코드로 그린 X는 끈다.
            bool drawX = landed && s.Belly && marked && spBellyMark == null;
            if (s.Mark.activeSelf != drawX) s.Mark.SetActive(drawX);
        }

        private static float EaseOutCubic(float p)
        {
            p = 1f - p;
            return 1f - p * p * p;
        }

        /// <summary>끝에서 살짝 넘어갔다 돌아온다. 눌림과 글자 팝에 쓴다.</summary>
        private static float EaseOutBack(float p)
        {
            const float c = 1.9f;
            float q = p - 1f;
            return 1f + (c + 1f) * q * q * q + c * q * q;
        }

        private void AddChain(YutResult r, bool hit)
        {
            var t = NewText("c", chainRow, UiTheme.FontSmall, hit ? UiTheme.Accent : UiTheme.TextDim);
            t.text = YutGame.DisplayName(r);
            var ol = t.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.6f);
            chain.Add(t);
            RefreshChainRow();
        }

        private void ClearChain()
        {
            foreach (var t in chain) if (t != null) Destroy(t.gameObject);
            chain.Clear();
            RefreshChainRow();
        }

        /// <summary>
        /// 연쇄 줄은 **두 번 이상 던졌을 때만** 보인다.
        ///
        /// ★ 하나뿐이면 바로 위의 큰 글자와 똑같은 글자가 작게 한 번 더 찍힌다.
        ///   같은 정보를 두 번 보여주면 유저는 둘이 다른 뜻이라고 읽으려 애쓴다.
        ///   이 줄의 뜻은 "결과"가 아니라 **"이어졌다"**이므로, 이어지기 전에는 없는 게 맞다.
        /// </summary>
        private void RefreshChainRow()
        {
            if (chainRow != null) chainRow.gameObject.SetActive(chain.Count >= 2);
        }

        // ─────────────────────────────────────────

        public void SetOpen(bool open)
        {
            if (panel != null) panel.gameObject.SetActive(open);

            // 여는 버튼과 디버그 HUD는 UiScreens가 판정한다. 여기서 직접 끄지 않는다.
            UiScreens.SetOpen(this, open);
            RefreshOpener();

            if (open)
            {
                ApplyBackdrop();
                ClearChain();
                RestSticks();
                bigText.text = "";
                bigText.rectTransform.localScale = Vector3.one;
                againText.text = "";
                multText.text = "";
                throwLabel.text = "던지기";
                throwButton.interactable = true;
                callButton.interactable = true;
                rolling = -1f;
                letterShown = false;
                summaryPending = false;
                callMo = false;
                RefreshCall();
                RefreshLimit();
            }
        }

        /// <summary>
        /// 가락을 던지기 전 자세로 되돌린다.
        ///
        /// ★ 면을 중간색으로 되돌리는 것이 핵심이다.
        ///   지난 판의 배·등이 남아 있으면 화면을 열자마자 결과가 떠 있는 셈이 된다.
        /// </summary>
        private void RestSticks()
        {
            for (int i = 0; i < sticks.Length; i++)
            {
                var s = sticks[i];
                if (s.Rt == null) continue;
                s.Rt.anchoredPosition = new Vector2(s.BaseX, StickH * 0.5f);
                s.Rt.localRotation = Quaternion.identity;
                s.Rt.localScale = Vector3.one;
                ShowFace(s, false, i == MarkedStick);
                s.Dust.color = new Color(0.86f, 0.83f, 0.72f, 0f);
            }
        }

        /// <summary>
        /// 배경을 지금 밤의 골목으로 깐다.
        ///
        /// ★ 도깨비와 노는 자리는 **다른 데가 아니라 그 골목**이어야 한다.
        ///   단색 남색 위에서 던지면 미니게임이 게임 밖 부록처럼 보이고,
        ///   그러면 "본판보다 미니게임이 재밌어서 계속했다"는 그 감각이 안 생긴다.
        ///   전장 배경을 그대로 어둡게 눌러 쓰면 공짜로 같은 세계가 된다.
        ///
        /// ★ 어둡게 누르는 것이 핵심이다. 원본 밝기로 깔면 윷가락과 결과 글자가 묻힌다.
        ///   배경은 장소를 말할 뿐이고, 읽혀야 하는 것은 결과다.
        /// </summary>
        private void ApplyBackdrop()
        {
            if (panelImage == null) return;

            var s = controller.State;
            var sprite = ArtLibrary.Field(s != null ? s.tier : 1);
            if (sprite == null) return;   // 아트가 없으면 단색 그대로 둔다

            panelImage.sprite = sprite;
            panelImage.type = Image.Type.Simple;
            panelImage.preserveAspect = false;
            panelImage.color = BackdropTint;
        }

        /// <summary>배경을 누르는 정도. 곱해지는 값이라 0.3이면 밝기 30%다.</summary>
        private static readonly Color BackdropTint = new Color(0.30f, 0.32f, 0.42f, 1f);

        private void RefreshCall()
        {
            callFill.color = callMo ? UiTheme.AccentDim : UiTheme.Card;
            callBox.color  = callMo ? UiTheme.Accent : UiTheme.Card;
            callLabel.color = UiTheme.Accent;
        }

        private void RefreshLimit()
        {
            int left = controller.YutRewardedPlaysLeft;
            limitText.text = left > 0
                ? $"오늘 보상  {GameController.YutRewardedPlaysPerDay - left} / {GameController.YutRewardedPlaysPerDay}"
                : "오늘 보상은 다 받았습니다";
            // ★ 순서를 글로 못 박는다. 부르기는 **던지기 전에** 하는 것이다.
            //   토글이 버튼처럼 안 보였을 때 제일 먼저 나온 질문이 순서였다.
            //   형태로 고쳤어도 글로 한 번 더 말해두는 편이 싸다.
            noteText.text = left > 0
                ? "던지기 전에 부릅니다 · 맞히면 두 배, 빗나가면 이번 판은 없음"
                : "보상은 없지만 계속 놀 수 있습니다";
        }

        // ─────────────────────────────────────────
        // 조립

        private void Build()
        {
            // 없으면 null이 온다. 그때는 색칠한 사각형으로 돈다.
            spBack      = ArtLibrary.YutStick("back");
            spBelly     = ArtLibrary.YutStick("belly");
            spBellyMark = ArtLibrary.YutStick("belly_mark");

            canvas = NewCanvas();

            var open = NewButton("Open", canvas.transform, "윷놀이", UiTheme.FontName);
            openButton = open.gameObject;
            float y = BattleScreen.HudBottomHeight + UiTheme.Gap + 80f;
            Anchor(open.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                   new Vector2(-190f, y), new Vector2(-UiTheme.GapWide, y + 72f));
            open.onClick.AddListener(() => SetOpen(true));

            panel = NewRect("Panel", canvas.transform);
            Stretch(panel, 0f, 0f);
            panelImage = AddImage(panel, UiTheme.Background);

            // 머리
            var head = NewRect("Head", panel);
            Anchor(head, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -140f), new Vector2(0f, 0f));
            AddImage(head, UiTheme.Panel);

            titleText = NewText("Title", head, UiTheme.FontTitle, UiTheme.Text);
            Anchor(titleText.rectTransform, new Vector2(0f, 0f), new Vector2(0.7f, 1f),
                   new Vector2(UiTheme.GapWide, 0f), new Vector2(0f, 0f));
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.text = "도깨비와 윷놀이";

            var close = NewButton("Close", head, "그만", UiTheme.FontName);
            Anchor(close.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(-150f, -34f), new Vector2(-UiTheme.GapWide, 34f));
            close.onClick.AddListener(() => SetOpen(false));

            // 윷가락 넷
            var row = NewRect("Sticks", panel);
            Anchor(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-280f, -700f), new Vector2(280f, -380f));
            // ★ 가락은 늘이지 않고 **고정 크기로 놓는다.**
            //   앵커로 칸을 나눠 늘리면 위치를 코드로 못 움직인다 — 던질 수가 없다.
            //   pivot을 바닥에 두는 것도 의도다. 착지 눌림(scaleY)이 바닥을 파고들지 않는다.
            const float SlotW = 140f, StickW = 74f, StickH = 230f;
            for (int i = 0; i < YutGame.StickCount; i++)
            {
                float x = (i - 1.5f) * SlotW;

                var st = NewRect("Stick" + i, row);
                st.anchorMin = new Vector2(0.5f, 0f);
                st.anchorMax = new Vector2(0.5f, 0f);
                st.pivot = new Vector2(0.5f, 0f);
                st.sizeDelta = new Vector2(StickW, StickH);
                st.anchoredPosition = new Vector2(x, 0f);
                var body = AddImage(st, StickAir);
                body.type = Image.Type.Simple;
                body.preserveAspect = false;

                // 백도 가락의 X. 얇은 막대 둘을 ±45도로 겹쳐 만든다.
                // 스프라이트가 들어오면 이 조각들은 꺼진다 — 그림이 X를 직접 갖고 있다.
                var mk = NewRect("Mark", st);
                mk.anchorMin = mk.anchorMax = mk.pivot = new Vector2(0.5f, 0.5f);
                mk.sizeDelta = new Vector2(StickW, StickW);
                mk.anchoredPosition = Vector2.zero;
                for (int k = 0; k < 2; k++)
                {
                    var bar = NewRect("Bar" + k, mk);
                    bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
                    bar.sizeDelta = new Vector2(StickW - 22f, 8f);
                    bar.anchoredPosition = Vector2.zero;
                    bar.localRotation = Quaternion.Euler(0f, 0f, k == 0 ? 45f : -45f);
                    AddImage(bar, new Color(0.28f, 0.18f, 0.11f, 1f));
                }
                mk.gameObject.SetActive(false);

                // 먼지는 가락의 형제다. 자식이면 같이 돌고 같이 눌린다.
                var dt = NewRect("Dust" + i, row);
                dt.anchorMin = dt.anchorMax = new Vector2(0.5f, 0f);
                dt.pivot = new Vector2(0.5f, 0.5f);
                dt.sizeDelta = new Vector2(22f, 6f);
                dt.anchoredPosition = new Vector2(x, 5f);
                var dust = AddImage(dt, new Color(0.86f, 0.83f, 0.72f, 0f));
                dt.SetAsFirstSibling();   // 먼지는 가락 뒤에 깔린다

                sticks[i] = new Stick
                {
                    Rt = st, Body = body, Mark = mk.gameObject, Dust = dust,
                    BaseX = x, Flight = 0.5f, Spin = 720f, Drift = 0f, Belly = false,
                };
            }

            // 결과
            bigText = NewText("Big", panel, YutLetterSize, UiTheme.Accent);
            Anchor(bigText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -1040f), new Vector2(0f, -720f));

            againText = NewText("Again", panel, UiTheme.FontTitle, UiTheme.Text);
            Anchor(againText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -1130f), new Vector2(0f, -1050f));

            chainRow = NewRect("Chain", panel);
            Anchor(chainRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -1230f), new Vector2(0f, -1140f));
            var cl = chainRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            cl.childAlignment = TextAnchor.MiddleCenter;
            cl.spacing = 14f;
            cl.childForceExpandWidth = false;
            cl.childControlWidth = true;

            multText = NewText("Mult", panel, UiTheme.FontName, UiTheme.TextDim);
            Anchor(multText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -1340f), new Vector2(0f, -1250f));

            // 아래 — 부르기 / 던지기 / 상한
            var foot = NewRect("Foot", panel);
            Anchor(foot, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, 0f), new Vector2(0f, 430f));
            AddImage(foot, UiTheme.Panel);

            // ★ 이게 버튼으로 안 보였다. "먼저 누르고 던지는 건가?"라고 물으신 것이 증거다.
            //   원인은 Outline이었다 — Outline은 그래픽을 복사해 어긋나게 한 번 더 그린다.
            //   바탕이 완전 투명이면 **복사본도 투명해서 테두리가 아예 안 그려진다.**
            //   켰을 때만 주황으로 차오르니, 꺼진 상태는 그냥 안내문으로 읽혔다.
            //   빈 상태에도 형태가 있어야 누를 수 있는 물건이 된다.
            var call = NewRect("Call", foot);
            Anchor(call, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-300f, -116f), new Vector2(300f, -28f));
            callFill = AddImage(call, UiTheme.Card);
            callButton = call.gameObject.AddComponent<Button>();
            callButton.targetGraphic = callFill;

            // 네모 표시. 찼는가 비었는가는 설명이 필요 없다.
            var box = NewRect("Box", call);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 0.5f);
            box.sizeDelta = new Vector2(40f, 40f);
            box.anchoredPosition = new Vector2(30f, 0f);
            AddImage(box, UiTheme.Accent);
            var inner = NewRect("BoxInner", box);
            Stretch(inner, 4f, 4f);
            callBox = AddImage(inner, UiTheme.Card);

            callLabel = NewText("CallLabel", call, UiTheme.FontName, UiTheme.Accent);
            Anchor(callLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(88f, 0f), new Vector2(-30f, 0f));
            callLabel.alignment = TextAnchor.MiddleLeft;
            callLabel.text = "“모야!” 부르기";
            callButton.onClick.AddListener(() => { callMo = !callMo; RefreshCall(); });

            noteText = NewText("Note", foot, UiTheme.FontSmall, UiTheme.TextDim);
            Anchor(noteText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -150f), new Vector2(0f, -110f));

            throwButton = NewButton("Throw", foot, "던지기", UiTheme.FontTitle);
            Anchor(throwButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                   new Vector2(-300f, 90f), new Vector2(300f, 190f));
            SetImage(throwButton.gameObject, UiTheme.Accent);
            throwLabel = throwButton.GetComponentInChildren<Text>();
            throwButton.onClick.AddListener(Throw);

            limitText = NewText("Limit", foot, UiTheme.FontTiny, UiTheme.TextLocked);
            Anchor(limitText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(0f, 30f), new Vector2(0f, 80f));
        }

        // ─────────────────────────────────────────
        // uGUI 도우미

        private Canvas NewCanvas()
        {
            var go = new GameObject("YutCanvas", typeof(Canvas),
                                    typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 55;   // 부적 화면(50)보다 위
            var s = go.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1080f, 1920f);
            s.matchWidthOrHeight = 1f;
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
            t.font = font; t.fontSize = size; t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
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
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, padY); rt.offsetMax = new Vector2(-pad, -padY);
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max,
                                   Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }
    }
}

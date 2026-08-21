using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleDefense.Art;
using IdleDefense.Core;
using IdleDefense.Economy;

namespace IdleDefense.UI
{
    /// <summary>
    /// 전장에서 실제로 벌어지는 일. 적이 걸어오고, 해치가 쏘고, 적이 죽는다.
    ///
    /// ★ 이 층이 없을 때 이 게임의 화면에서는 **아무 일도 일어나지 않았다.**
    ///   웨이브 숫자가 오르고 막대가 차오르는 것이 전부였고,
    ///   타워디펜스인데 방어하는 그림이 없었다. 재미의 문제가 아니라
    ///   "지금 뭘 하고 있는가"가 화면에 없다는 문제였다.
    ///
    /// ★ 그런데 전투 계산에는 **적이 한 마리도 없다.**
    ///   BattleRunner는 웨이브 체력 한 덩이를 DPS로 깎는다 — 저발열을 위해 그렇게 설계했고,
    ///   그 설계는 옳다. 적 열 마리를 실제로 돌리면 발열이 열 배가 된다.
    ///
    ///   그래서 여기서도 **적을 만들지 않는다.** 남은 체력 비율에서 몇 마리가 살아 있어야
    ///   하는지를 되짚어 그림만 맞춘다. 화면이 전투를 다시 구현하는 순간
    ///   "화면은 47인데 계산은 48"이 시작되고, 그건 아무도 못 잡는다.
    ///
    /// ★ 때리는 순간은 지어내지 않는다.
    ///   BattleRunner가 이미 `OnShotFired`를 0.4초마다 쏜다 — 계산은 연속이지만
    ///   연출용 발사 이벤트를 따로 내주는 계층이 처음부터 있었다.
    ///   화면이 자기 박자로 때리면 화면과 계산이 서로 다른 리듬을 갖게 된다.
    ///   **총알이 날아가는 순간과 체력이 깎이는 순간은 같아야 한다.**
    ///
    /// ★ MonoBehaviour가 아니다.
    ///   BattleScreen이 소유하고 Tick을 불러준다. 컴포넌트로 만들면
    ///   인스펙터에서 붙이는 걸 잊는 순간 전장이 조용히 사라진다 — 이미 그런 게 하나 있다
    ///   (BattleFeedback은 프리팹 두 개를 요구해서 아무것도 안 하고 있었다).
    /// </summary>
    public class BattleField
    {
        // ── 원근 ──
        //
        // 배경 여섯 장이 전부 같은 1점 투시 골목이라, 소실점과 앞줄만 정하면
        // 적이 그 길 위를 걸어오는 것처럼 보인다. 배경이 통일돼 있어 공짜로 얻는 것이다.
        private const float HorizonY     = 790f;   // 소실점 (화면 위에서)
        private const float FrontY       = 1160f;  // 앞줄 — 해치 앞
        private const float HorizonScale = 0.10f;
        private const float FrontScale   = 0.85f;
        private const float SpreadX      = 330f;   // 앞줄에서 좌우로 벌어지는 폭
        private const float CenterX      = 540f;

        /// <summary>한 마리가 소실점에서 앞줄까지 오는 시간.</summary>
        private const float MarchSeconds = 5.0f;

        /// <summary>
        /// 화면에 세우는 적의 수. **연출용 숫자다.**
        /// EconomyConfig.enemiesPerWave(10)와 맞춰 둔 것이지 계산에 쓰이지 않는다.
        /// 어긋나도 게임은 정확하다 — 다만 "한 마리당 체력"의 체감이 달라진다.
        /// </summary>
        private const int EnemyCount = 10;

        private const float DeathSeconds = 0.28f;
        private const float ShotSeconds  = 0.16f;
        private const float NumberSeconds = 0.65f;

        // ── 색 ──
        private static readonly Color SmokeColor = new Color(0.04f, 0.05f, 0.09f, 0.92f);
        private static readonly Color EyeColor   = new Color(1.00f, 0.93f, 0.62f, 1f);
        private static readonly Color HitColor   = new Color(1.00f, 0.86f, 0.70f, 1f);

        private sealed class Enemy
        {
            public RectTransform Rt;
            public Image Smoke;
            public Image EyeL, EyeR;
            public float Lane;      // -1 ~ 1
            public float T;         // 0 소실점 ~ 1 앞줄. 음수면 아직 안 나왔다
            public float Bob;       // 흔들림 위상
            public float Dying;     // 0보다 크면 사라지는 중
            public float Flash;     // 피격 잔광
            public bool Alive;
        }

        private readonly Enemy[] enemies = new Enemy[EnemyCount];
        private readonly List<Image> shots = new List<Image>(6);
        private readonly List<float> shotAge = new List<float>(6);
        private readonly List<Vector2> shotFrom = new List<Vector2>(6);
        private readonly List<Vector2> shotTo = new List<Vector2>(6);
        private readonly List<Text> numbers = new List<Text>(6);
        private readonly List<float> numberAge = new List<float>(6);

        private RectTransform root;
        private BattleRunner bound;
        private int lastWave = -1;
        private float shake;        // 남은 흔들림 시간
        private float shakePower;
        private bool shotPulse;

        /// <summary>
        /// 방금 한 발 나갔는가. 해치를 앞으로 튀게 하려고 화면이 읽어 간다.
        ///
        /// ★ 이벤트를 하나 더 만들지 않은 이유 — 구독은 해제를 잊으면 새고,
        ///   이 값은 한 프레임짜리라 놓쳐도 잃을 것이 없다.
        ///   놓쳐도 되는 신호에 해제 책임이 따라오는 장치를 쓰지 않는다.
        /// </summary>
        public bool ConsumeShotPulse()
        {
            if (!shotPulse) return false;
            shotPulse = false;
            return true;
        }

        // ★ 여기 두 값은 **anchoredPosition 좌표**다 — y가 음수인 것이 정상이다.
        //   위 상수들(HorizonY 등)은 "화면 위에서 몇 px"이고, 이건 앵커가 좌상단이라 부호가 뒤집힌다.
        //   섞어 쓰면 총알이 화면 밖으로 나간다. 한 번 그랬다.

        /// <summary>해치의 입 언저리. 총알이 여기서 나간다.</summary>
        private static readonly Vector2 Muzzle = new Vector2(CenterX, -1010f);

        /// <summary>겨눌 적이 없을 때의 대상.</summary>
        private static readonly Vector2 Horizon = new Vector2(CenterX, -HorizonY);

        // ─────────────────────────────────────────

        public BattleField(RectTransform parent, Font font)
        {
            root = NewRect("Field", parent);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            for (int i = 0; i < EnemyCount; i++) enemies[i] = BuildEnemy(i);
            for (int i = 0; i < 6; i++) BuildShot();
            for (int i = 0; i < 6; i++) BuildNumber(font);
        }

        /// <summary>
        /// 발사 이벤트에 붙는다. 런이 새로 만들어지면 다시 불러야 한다.
        /// 같은 러너에 두 번 붙으면 총알이 두 발씩 나간다.
        /// </summary>
        public void Bind(BattleRunner battle)
        {
            if (ReferenceEquals(bound, battle)) return;
            Unbind();
            bound = battle;
            if (bound == null) return;
            bound.OnShotFired += HandleShot;
            bound.OnWaveFinisher += HandleFinisher;
        }

        public void Unbind()
        {
            if (bound == null) return;
            bound.OnShotFired -= HandleShot;
            bound.OnWaveFinisher -= HandleFinisher;
            bound = null;
        }

        // ─────────────────────────────────────────
        // 매 프레임

        public void Tick(BattleRunner b, float dt)
        {
            if (root == null) return;

            bool running = b != null && b.CurrentWave > 0 && !b.WaveHpTotal.IsZero;

            // 웨이브가 바뀌면 열 마리를 새로 세운다. 한 줄로 세우지 않고 시차를 준다 —
            // 동시에 출발하면 열 마리가 아니라 벽 하나로 보인다.
            if (b != null && b.CurrentWave != lastWave)
            {
                lastWave = b.CurrentWave;
                for (int i = 0; i < enemies.Length; i++)
                {
                    var e = enemies[i];
                    e.Alive = running;
                    e.Dying = 0f;
                    e.Flash = 0f;
                    e.T = -(i * 0.11f);                       // 음수 = 아직 소실점 뒤
                    e.Lane = (i - (EnemyCount - 1) * 0.5f) / (EnemyCount * 0.5f);
                    e.Bob = i * 1.37f;
                }
            }

            // 살아 있어야 하는 수. 남은 체력에서 되짚는다 — 여기서 만들지 않는다.
            int want = running
                ? Mathf.Clamp(Mathf.CeilToInt(EnemyCount * b.WaveHpRatio), 0, EnemyCount)
                : 0;

            int have = 0;
            for (int i = 0; i < enemies.Length; i++)
                if (enemies[i].Alive && enemies[i].Dying <= 0f) have++;

            // 넘치면 **가장 앞에 온 놈부터** 죽인다. 뒤에서 사라지면 아무도 못 본다.
            while (have > want)
            {
                int front = -1;
                float best = -999f;
                for (int i = 0; i < enemies.Length; i++)
                {
                    var e = enemies[i];
                    if (!e.Alive || e.Dying > 0f) continue;
                    if (e.T > best) { best = e.T; front = i; }
                }
                if (front < 0) break;
                enemies[front].Dying = DeathSeconds;
                have--;
            }

            for (int i = 0; i < enemies.Length; i++) TickEnemy(enemies[i], dt);
            TickShots(dt);
            TickNumbers(dt);
            TickShake(dt);
        }

        private void TickEnemy(Enemy e, float dt)
        {
            if (!e.Alive) { Hide(e); return; }

            if (e.T < 1f) e.T += dt / MarchSeconds;
            if (e.T > 1f) e.T = 1f;
            if (e.Flash > 0f) e.Flash -= dt;

            if (e.T < 0f) { Hide(e); return; }

            // ★ 원근은 선형이 아니다.
            //   멀리 있을 때는 화면에서 거의 안 움직이다가 가까워질수록 급해진다.
            //   t를 그대로 쓰면 등속으로 커져서 "다가온다"가 아니라 "확대된다"로 보인다.
            float u = e.T * e.T;

            float y = Mathf.Lerp(HorizonY, FrontY, u);
            float s = Mathf.Lerp(HorizonScale, FrontScale, u);
            float x = CenterX + e.Lane * SpreadX * u;

            float scale = s;
            float alpha = Mathf.Clamp01(e.T / 0.12f);          // 소실점에서 스며 나온다

            if (e.Dying > 0f)
            {
                e.Dying -= dt;
                float q = Mathf.Clamp01(1f - e.Dying / DeathSeconds);
                scale = s * (1f + 0.55f * q);                  // 흩어지듯 부풀며
                alpha *= 1f - q;                               // 사라진다
                if (e.Dying <= 0f) { e.Alive = false; Hide(e); return; }
            }
            else
            {
                // 걸어오는 흔들림. 가까울수록 크게 — 멀리서 흔들면 지직거림으로 보인다.
                y += Mathf.Sin(Time.time * 3.1f + e.Bob) * 10f * u;
            }

            e.Rt.anchoredPosition = new Vector2(x, -y);
            e.Rt.localScale = new Vector3(scale, scale, 1f);

            var body = SmokeColor;
            if (e.Flash > 0f) body = Color.Lerp(body, HitColor, Mathf.Clamp01(e.Flash / 0.10f) * 0.75f);
            body.a *= alpha;
            e.Smoke.color = body;

            var eye = EyeColor;
            eye.a = alpha;
            e.EyeL.color = eye;
            e.EyeR.color = eye;

            if (!e.Rt.gameObject.activeSelf) e.Rt.gameObject.SetActive(true);
        }

        private static void Hide(Enemy e)
        {
            if (e.Rt != null && e.Rt.gameObject.activeSelf) e.Rt.gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────
        // 발사

        private void HandleShot(BattleRunner.ShotInfo info)
        {
            var target = Frontmost();

            // 살아 있는 적이 없으면 소실점을 쏜다. 발사가 끊기는 것보다 낫다 —
            // 웨이브가 막 넘어가는 순간에는 한두 발이 늘 허공으로 나간다.
            Vector2 to = target != null ? target.Rt.anchoredPosition : Horizon;

            Fire(Muzzle, to, info.IsCritical);
            PopNumber(to, info.Damage, info.IsCritical);

            if (target != null) target.Flash = 0.10f;
            shotPulse = true;

            // ★ 매번 흔들면 몇 분 만에 피로해진다. 크리티컬에서만.
            if (info.IsCritical) Shake(0.10f, 7f);
        }

        private void HandleFinisher(int wave)
        {
            Shake(0.16f, 13f);
        }

        private Enemy Frontmost()
        {
            Enemy best = null;
            float t = -999f;
            for (int i = 0; i < enemies.Length; i++)
            {
                var e = enemies[i];
                if (!e.Alive || e.Dying > 0f || e.T < 0f) continue;
                if (e.T > t) { t = e.T; best = e; }
            }
            return best;
        }

        private void Fire(Vector2 from, Vector2 to, bool crit)
        {
            for (int i = 0; i < shots.Count; i++)
            {
                if (shotAge[i] >= 0f) continue;
                shotAge[i] = 0f;
                shotFrom[i] = from;
                shotTo[i] = to;
                shots[i].color = crit ? UiTheme.Accent : new Color(1f, 0.95f, 0.80f, 1f);
                shots[i].rectTransform.sizeDelta = crit ? new Vector2(46f, 46f) : new Vector2(32f, 32f);
                shots[i].gameObject.SetActive(true);
                return;
            }
        }

        private void TickShots(float dt)
        {
            for (int i = 0; i < shots.Count; i++)
            {
                if (shotAge[i] < 0f) continue;
                shotAge[i] += dt;
                float p = Mathf.Clamp01(shotAge[i] / ShotSeconds);

                // 살짝 위로 휘어 날아간다. 직선이면 총알이 아니라 선분으로 보인다.
                var pos = Vector2.Lerp(shotFrom[i], shotTo[i], p);
                pos.y += Mathf.Sin(p * Mathf.PI) * 40f;
                shots[i].rectTransform.anchoredPosition = pos;

                var c = shots[i].color;
                c.a = 1f - p * p;
                shots[i].color = c;

                if (p >= 1f)
                {
                    shotAge[i] = -1f;
                    shots[i].gameObject.SetActive(false);
                }
            }
        }

        // ─────────────────────────────────────────
        // 데미지 숫자

        private void PopNumber(Vector2 at, BigNumber damage, bool crit)
        {
            for (int i = 0; i < numbers.Count; i++)
            {
                if (numberAge[i] >= 0f) continue;
                numberAge[i] = 0f;
                var t = numbers[i];
                t.text = damage.ToString();
                t.fontSize = crit ? UiTheme.FontTitle : UiTheme.FontSmall;
                t.color = crit ? UiTheme.Accent : UiTheme.Text;
                t.rectTransform.anchoredPosition = at + new Vector2(Random.Range(-40f, 40f), 40f);
                t.gameObject.SetActive(true);
                return;
            }
        }

        private void TickNumbers(float dt)
        {
            for (int i = 0; i < numbers.Count; i++)
            {
                if (numberAge[i] < 0f) continue;
                numberAge[i] += dt;
                float p = Mathf.Clamp01(numberAge[i] / NumberSeconds);

                var rt = numbers[i].rectTransform;
                rt.anchoredPosition += new Vector2(0f, 90f * dt);

                var c = numbers[i].color;
                c.a = 1f - p * p;
                numbers[i].color = c;

                if (p >= 1f)
                {
                    numberAge[i] = -1f;
                    numbers[i].gameObject.SetActive(false);
                }
            }
        }

        // ─────────────────────────────────────────
        // 흔들림

        private void Shake(float seconds, float power)
        {
            // 더 센 흔들림이 이미 돌고 있으면 덮어쓰지 않는다.
            if (shake > 0f && shakePower > power) return;
            shake = seconds;
            shakePower = power;
        }

        private void TickShake(float dt)
        {
            if (shake <= 0f)
            {
                if (root.anchoredPosition != Vector2.zero) root.anchoredPosition = Vector2.zero;
                return;
            }

            shake -= dt;
            float k = Mathf.Max(0f, shake) * shakePower * 6f;
            root.anchoredPosition = new Vector2(Random.Range(-k, k), Random.Range(-k, k));
            if (shake <= 0f) root.anchoredPosition = Vector2.zero;
        }

        // ─────────────────────────────────────────
        // 조립

        private Enemy BuildEnemy(int index)
        {
            var rt = NewRect("Enemy" + index, root);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(210f, 240f);

            var smoke = AddDot(rt, SmokeColor);
            smoke.rectTransform.anchorMin = Vector2.zero;
            smoke.rectTransform.anchorMax = Vector2.one;
            smoke.rectTransform.offsetMin = Vector2.zero;
            smoke.rectTransform.offsetMax = Vector2.zero;

            var eyeL = BuildEye(rt, -34f);
            var eyeR = BuildEye(rt, 34f);

            rt.gameObject.SetActive(false);
            return new Enemy { Rt = rt, Smoke = smoke, EyeL = eyeL, EyeR = eyeR };
        }

        /// <summary>
        /// 눈. **이 게임의 적은 배경 하늘에 떠 있는 그 눈이 땅으로 내려온 것이다.**
        /// 배경 여섯 장에 전부 같은 눈이 그려져 있으므로, 새 아트 없이 세계가 이어진다.
        /// </summary>
        private Image BuildEye(RectTransform parent, float dx)
        {
            var rt = NewRect("Eye", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 26f);
            rt.anchoredPosition = new Vector2(dx, 26f);
            return AddDot(rt, EyeColor);
        }

        private void BuildShot()
        {
            var rt = NewRect("Shot", root);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(32f, 32f);
            var img = AddDot(rt, Color.white);
            rt.gameObject.SetActive(false);
            shots.Add(img);
            shotAge.Add(-1f);
            shotFrom.Add(Vector2.zero);
            shotTo.Add(Vector2.zero);
        }

        private void BuildNumber(Font font)
        {
            var rt = NewRect("Damage", root);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(360f, 60f);

            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = UiTheme.FontSmall;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            var ol = rt.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.8f);
            ol.effectDistance = new Vector2(2f, -2f);

            rt.gameObject.SetActive(false);
            numbers.Add(t);
            numberAge.Add(-1f);
        }

        private static Image AddDot(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = ArtLibrary.SoftDot;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}

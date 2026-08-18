using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using IdleDefense.Core;
using IdleDefense.Economy;

namespace IdleDefense.Game
{
    /// <summary>
    /// 타격감 연출.
    ///
    /// 설계 원칙:
    ///   연출은 전투 계산을 절대 건드리지 않는다.
    ///   BattleRunner가 발사 이벤트를 쏘면 여기서 화면만 처리한다.
    ///   그래야 연출을 바꿔도 경제 테스트가 계속 유효하다.
    ///
    /// 저발열 대응:
    ///   투사체와 데미지 텍스트를 매번 생성/파괴하지 않고 풀에서 재사용한다.
    ///   방치형은 몇 시간씩 켜두는 게임이라 GC 스파이크가 발열로 직결된다.
    /// </summary>
    public class BattleFeedback : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameController controller;
        [SerializeField] private Transform towerPoint;
        [SerializeField] private Transform enemyPoint;

        [Header("풀 프리팹")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private GameObject damageTextPrefab;

        [Tooltip("동시에 존재할 수 있는 최대 개수. 넘으면 가장 오래된 것을 재사용한다")]
        [SerializeField] private int poolSize = 12;

        [Header("연출 강도")]
        [SerializeField] private float projectileSpeed = 14f;

        [Tooltip("크리티컬에서만 화면을 흔든다. 매번 흔들면 몇 분 만에 피로해진다")]
        [SerializeField] private float critShakeStrength = 0.18f;
        [SerializeField] private float critShakeDuration = 0.12f;

        [Tooltip("웨이브 클리어 시 흔들림. 크리티컬보다 강하게")]
        [SerializeField] private float finisherShakeStrength = 0.35f;

        [Header("색")]
        [SerializeField] private Color normalDamageColor = new Color(0.99f, 0.94f, 0.81f);
        [SerializeField] private Color critDamageColor = new Color(0.99f, 0.80f, 0.31f);

        private readonly Queue<GameObject> projectilePool = new Queue<GameObject>();
        private readonly Queue<GameObject> textPool = new Queue<GameObject>();
        private Vector3 cameraHome;
        private Coroutine shakeRoutine;

        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Camera.main != null) cameraHome = Camera.main.transform.localPosition;
            Prewarm();
        }

        private void OnEnable()
        {
            if (controller?.Battle == null) return;
            controller.Battle.OnShotFired += HandleShot;
            controller.Battle.OnWaveFinisher += HandleFinisher;
        }

        private void OnDisable()
        {
            if (controller?.Battle == null) return;
            controller.Battle.OnShotFired -= HandleShot;
            controller.Battle.OnWaveFinisher -= HandleFinisher;
        }

        /// <summary>
        /// 미리 생성해 둔다. 전투 중 Instantiate가 일어나면
        /// 프레임이 튀고 GC가 돌아 발열이 생긴다.
        /// </summary>
        private void Prewarm()
        {
            for (int i = 0; i < poolSize; i++)
            {
                if (projectilePrefab != null)
                {
                    var p = Instantiate(projectilePrefab, transform);
                    p.SetActive(false);
                    projectilePool.Enqueue(p);
                }
                if (damageTextPrefab != null)
                {
                    var t = Instantiate(damageTextPrefab, transform);
                    t.SetActive(false);
                    textPool.Enqueue(t);
                }
            }
        }

        private GameObject Rent(Queue<GameObject> pool)
        {
            if (pool.Count == 0) return null;
            var go = pool.Dequeue();
            pool.Enqueue(go);   // 순환 재사용 — 부족하면 가장 오래된 것을 뺏는다
            return go;
        }

        // ─────────────────────────────────────────

        private void HandleShot(BattleRunner.ShotInfo shot)
        {
            StartCoroutine(ProjectileRoutine(shot));

            if (shot.IsCritical)
                Shake(critShakeStrength, critShakeDuration);
        }

        private void HandleFinisher(int wave)
        {
            Shake(finisherShakeStrength, critShakeDuration * 1.5f);
        }

        private IEnumerator ProjectileRoutine(BattleRunner.ShotInfo shot)
        {
            var proj = Rent(projectilePool);
            if (proj == null || towerPoint == null || enemyPoint == null)
            {
                ShowDamage(shot);
                yield break;
            }

            proj.transform.position = towerPoint.position;
            proj.SetActive(true);

            Vector3 from = towerPoint.position;
            Vector3 to = enemyPoint.position;
            float dist = Vector3.Distance(from, to);
            float dur = Mathf.Max(0.05f, dist / projectileSpeed);
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                proj.transform.position = Vector3.Lerp(from, to, t);
                yield return null;
            }

            proj.SetActive(false);
            ShowDamage(shot);   // 명중한 순간에 숫자가 뜬다
        }

        private void ShowDamage(BattleRunner.ShotInfo shot)
        {
            var go = Rent(textPool);
            if (go == null) return;

            go.transform.position = enemyPoint != null
                ? enemyPoint.position + (Vector3)Random.insideUnitCircle * 0.4f
                : transform.position;

            var text = go.GetComponentInChildren<TMPro.TextMeshPro>();
            if (text != null)
            {
                text.text = shot.Damage.ToString();
                text.color = shot.IsCritical ? critDamageColor : normalDamageColor;
                text.fontSize = shot.IsCritical ? 5.5f : 4f;
            }

            go.SetActive(true);
            StartCoroutine(FloatAndFade(go, shot.IsCritical ? 0.7f : 0.5f));
        }

        private IEnumerator FloatAndFade(GameObject go, float duration)
        {
            Vector3 start = go.transform.position;
            float t = 0f;
            var text = go.GetComponentInChildren<TMPro.TextMeshPro>();
            Color baseColor = text != null ? text.color : Color.white;

            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                go.transform.position = start + Vector3.up * (t * 0.9f);
                if (text != null)
                {
                    var c = baseColor;
                    c.a = 1f - t * t;   // 후반에 빠르게 사라진다
                    text.color = c;
                }
                yield return null;
            }
            go.SetActive(false);
        }

        // ─────────────────────────────────────────

        private void Shake(float strength, float duration)
        {
            if (Camera.main == null) return;
            if (shakeRoutine != null) StopCoroutine(shakeRoutine);
            shakeRoutine = StartCoroutine(ShakeRoutine(strength, duration));
        }

        private IEnumerator ShakeRoutine(float strength, float duration)
        {
            var cam = Camera.main.transform;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float decay = 1f - (t / duration);
                Vector2 offset = Random.insideUnitCircle * strength * decay;
                cam.localPosition = cameraHome + new Vector3(offset.x, offset.y, 0f);
                yield return null;
            }
            cam.localPosition = cameraHome;
            shakeRoutine = null;
        }
    }
}

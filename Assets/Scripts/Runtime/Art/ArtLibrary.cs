using System.Collections.Generic;
using UnityEngine;

namespace IdleDefense.Art
{
    /// <summary>
    /// id로 아트를 찾는 유일한 통로.
    ///
    /// 존재 이유는 '나중에 아트를 갈아끼우는 비용'을 0으로 만드는 것이다.
    /// 코드나 프리팹에 스프라이트를 직접 물리면 그 순간 교체가 재작업이 된다.
    /// 여기를 거치면 정해진 경로에 파일을 덮어쓰는 것으로 끝난다.
    ///
    /// 경로 규약 — Assets/Resources/ 아래에 있어야 Resources.Load가 찾는다.
    ///
    ///   Assets/Resources/Art/Card/{id}.png        1024x1024  카드 일러스트
    ///   Assets/Resources/Art/Unit/{id}.png         256x256   치비 유닛 (투명 배경)
    ///   Assets/Resources/Art/Cutin/{id}.png       1024x1024  소환 컷인
    ///   Assets/Resources/Art/Haetae/tier{n}.png   1024x1024  해치 티어 1~6
    ///
    /// {id}는 TalismanCatalog의 id를 그대로 쓴다 — jeoseungsaja, cheoyong ...
    ///
    /// 설계 원칙 — 아트가 없어도 게임은 돌아간다.
    /// 못 찾으면 예외를 던지지 않고 플레이스홀더를 돌려주고 경고를 한 번만 남긴다.
    /// 그러지 않으면 12종이 전부 들어오기 전까지 Play가 막힌다.
    /// </summary>
    public static class ArtLibrary
    {
        public const string CardRoot   = "Art/Card/";
        public const string UnitRoot   = "Art/Unit/";
        public const string CutinRoot  = "Art/Cutin/";
        public const string HaetaeRoot = "Art/Haetae/tier";
        public const string FieldRoot  = "Art/Field/tier";
        public const string YutRoot    = "Art/Yut/";

        private static readonly Dictionary<string, Sprite> cache =
            new Dictionary<string, Sprite>(32);

        /// <summary>이미 경고한 경로. 매 프레임 같은 경고를 쏟지 않기 위한 것이다.</summary>
        private static readonly HashSet<string> warned = new HashSet<string>();

        private static Sprite placeholder;
        private static Sprite softDot;

        // ─────────────────────────────────────────
        // 조회

        public static Sprite Card(string id)  => Load(CardRoot  + id);
        public static Sprite Unit(string id)  => Load(UnitRoot  + id);
        public static Sprite Cutin(string id) => Load(CutinRoot + id);

        /// <summary>해치 티어 아트. 범위를 벗어나면 가장 가까운 티어로 붙인다.</summary>
        public static Sprite HaetaeTier(int tier) => Load(HaetaeRoot + ClampTier(tier));

        /// <summary>
        /// 전장 배경. 티어마다 같은 골목의 더 늦은 시각이다.
        ///
        /// ★ 세 축이 동시에 움직인다 — 문이 자라고(장승→남대문),
        ///   하늘이 밝아지고(초저녁→동틀녘), **등불이 줄어든다**(9개→0개).
        ///   마지막 축이 "밤이 길어지고 있다"를 설명 없이 전달한다. 마을이 비어가는 것이다.
        ///
        /// 티어 7 이상은 6번 배경을 계속 쓴다 — 다음 밤은 같은 문에서 다시 시작한다.
        /// </summary>
        public static Sprite Field(int tier) => Load(FieldRoot + ClampTier(tier));

        private static int ClampTier(int tier)
        {
            if (tier < 1) return 1;
            if (tier > 6) return 6;
            return tier;
        }

        /// <summary>
        /// 윷가락의 면. `back`(등) · `belly`(배) · `belly_mark`(백도 가락의 배).
        ///
        /// ★ 여기만 **없으면 null을 돌려준다.** 플레이스홀더로 때우지 않는다.
        ///   윷가락은 아트가 없어도 색칠한 사각형으로 충분히 읽히는 층이라,
        ///   자홍색 네모 넷이 뜨는 쪽이 오히려 화면을 망가뜨린다.
        ///   호출부가 null을 보고 사각형으로 그리도록 두는 것이 맞다.
        /// </summary>
        public static Sprite YutStick(string face) => TryLoad(YutRoot + face);

        /// <summary>
        /// 없으면 null. 있으면 좋고 없어도 되는 아트에 쓴다.
        /// null도 캐시한다 — 매 프레임 없는 파일을 찾으러 가지 않게.
        /// </summary>
        public static Sprite TryLoad(string path)
        {
            if (cache.TryGetValue(path, out var cached)) return cached;
            var sprite = Resources.Load<Sprite>(path);
            cache[path] = sprite;
            return sprite;
        }

        /// <summary>
        /// 파일이 실제로 있는지. 플레이스홀더로 때우고 있는지 확인할 때 쓴다.
        /// 에디터 검사와 테스트용이며 런타임 분기에 쓰지 않는다.
        /// </summary>
        public static bool Has(string resourcePath)
            => Resources.Load<Sprite>(resourcePath) != null;

        // ─────────────────────────────────────────
        // 내부

        private static Sprite Load(string path)
        {
            if (cache.TryGetValue(path, out var cached)) return cached;

            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                if (warned.Add(path))
                    Debug.LogWarning($"[ArtLibrary] 아트 없음 → 플레이스홀더 사용: Resources/{path}");
                sprite = Placeholder;
            }

            cache[path] = sprite;
            return sprite;
        }

        /// <summary>
        /// 파일 없이 코드로 만드는 대체 이미지.
        /// 에디터에서는 자홍색이라 눈에 바로 띄고, 빌드에서는 어두운 회색이라 덜 거슬린다.
        /// </summary>
        private static Sprite Placeholder
        {
            get
            {
                if (placeholder != null) return placeholder;

                const int size = 64;
#if UNITY_EDITOR
                var fill   = new Color(0.85f, 0.10f, 0.65f, 1f);
                var border = new Color(1.00f, 1.00f, 1.00f, 1f);
#else
                var fill   = new Color(0.16f, 0.16f, 0.18f, 1f);
                var border = new Color(0.30f, 0.30f, 0.34f, 1f);
#endif
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "ArtLibrary_Placeholder",
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };

                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        bool edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                        px[y * size + x] = edge ? border : fill;
                    }
                tex.SetPixels(px);
                tex.Apply();

                placeholder = Sprite.Create(
                    tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                placeholder.name = "ArtLibrary_Placeholder";
                placeholder.hideFlags = HideFlags.HideAndDontSave;
                return placeholder;
            }
        }

        /// <summary>
        /// 가운데가 진하고 가장자리로 사라지는 원.
        ///
        /// ★ 파일로 두지 않는 이유는 이게 **아트가 아니라 도구**이기 때문이다.
        ///   연기·눈빛·투사체·먼지가 전부 이 한 장을 색만 바꿔 쓴다.
        ///   파일로 만들면 누군가 "연기용 원"과 "눈빛용 원"을 따로 만들기 시작하고,
        ///   그때부터 같은 것이 네 벌 생긴다.
        ///
        /// uGUI의 기본 스프라이트는 네모라 연기가 상자로 보인다. 그것만은 못 쓴다.
        /// </summary>
        public static Sprite SoftDot
        {
            get
            {
                if (softDot != null) return softDot;

                const int size = 64;
                const float r = size * 0.5f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "ArtLibrary_SoftDot",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };

                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) - r, dy = (y + 0.5f) - r;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;      // 0 가운데 ~ 1 가장자리
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);                        // 부드럽게
                        px[y * size + x] = new Color(1f, 1f, 1f, a);
                    }
                tex.SetPixels(px);
                tex.Apply();

                softDot = Sprite.Create(
                    tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                softDot.name = "ArtLibrary_SoftDot";
                softDot.hideFlags = HideFlags.HideAndDontSave;
                return softDot;
            }
        }

        /// <summary>
        /// 캐시를 비운다. 에디터에서 아트를 교체한 뒤 Play를 다시 돌리지 않고 반영할 때,
        /// 그리고 테스트 간 격리를 위해 쓴다.
        /// </summary>
        public static void ClearCache()
        {
            cache.Clear();
            warned.Clear();
        }
    }
}

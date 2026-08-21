using UnityEngine;

namespace IdleDefense.UI
{
    /// <summary>
    /// UI 색과 치수의 유일한 출처.
    ///
    /// ★ 이 파일의 존재 이유는 예쁜 색을 모아두는 것이 아니라
    ///   **오방색과 충돌하지 않게 막는 것**이다.
    ///
    ///   이 게임에서 청·적·황·백·흑 다섯 색은 이미 '강화 트랙'이라는 뜻을 갖는다.
    ///   UI가 그 다섯 색에 다른 의미(성공/실패/희귀도 등)를 또 붙이면
    ///   같은 화면에서 색이 두 가지를 뜻하게 되고, 유저는 둘 다 못 읽는다.
    ///
    ///   그래서 UI의 강조색은 오방색에 없는 **주황**을 쓴다.
    ///   주황은 이미 브랜드 마크(저승사자가 든 명부)의 색이기도 하다.
    ///
    ///   근거: docs/마케팅_비주얼_기준.md 5장
    ///
    /// ★ 여기 없는 색을 코드에 직접 적지 마라.
    ///   한 번 흩어지면 톤을 되돌릴 수 없다.
    /// </summary>
    public static class UiTheme
    {
        // ── 배경 ── 밤이 설정이다. 어두운 쪽에서 시작한다.
        public static readonly Color Background = Hex("0E1626");
        public static readonly Color Panel      = Hex("162034");
        public static readonly Color Card       = Hex("1E2A42");
        public static readonly Color CardLocked = Hex("141B29");

        // ── 강조 ── 오방색에 없는 색만 쓴다.
        public static readonly Color Accent      = Hex("E8873C");   // 주황 — 브랜드 마크
        public static readonly Color AccentDim   = Hex("8A5426");

        // ── 글자 ──
        public static readonly Color Text       = Hex("E8E2D0");
        public static readonly Color TextDim    = Hex("8B93A3");
        public static readonly Color TextLocked = Hex("5A6273");

        // ── 치수 ── 8의 배수로 유지한다.
        public const float Gap      = 8f;
        public const float GapWide  = 24f;
        /// <summary>
        /// 부적 카드 한 장. 152 × 200 → **220 × 330**.
        ///
        /// ★ 폭은 이름이 정한다. "저승사자" 네 글자가 30px 글꼴로 120px이고,
        ///   좌우 여백을 빼면 200이 최소선이다. 152로는 구조적으로 잘렸다.
        /// ★ 높이는 화면이 정한다. 1080폭에서 네 열이 서고, 17종이 다섯 줄이 되며,
        ///   다섯 줄 × 330이 패널 안쪽 높이를 거의 정확히 채운다.
        ///   2:3은 덤으로 실제 카드 비율이다 — 이 화면은 카드를 고르는 화면이 맞다.
        /// </summary>
        public const float CardW    = 220f;
        public const float CardH    = 330f;
        public const float Radius   = 8f;

        /// <summary>
        /// 글자 크기 사다리. **기준 해상도 1080 × 1920에서의 값**이다.
        ///
        /// ★ 2026-08-21 전면 상향. 30/20/15 → 52/40/30/24/20.
        ///
        ///   옛 값은 PC 에디터 창에서 정한 것이라 손에 쥔 6인치 화면 기준이 아니었다.
        ///   1080폭에서 15px 글자는 실기에서 약 2.5mm다 — 읽히긴 하지만
        ///   **읽으려고 눈을 모아야 한다.** 방치형은 한 손으로 흘끗 보는 게임이라
        ///   "읽을 수 있다"가 아니라 "안 보려 해도 보인다"가 기준이어야 한다.
        ///
        /// ★ 다섯 단만 쓴다. 여섯 번째가 필요해지면 그건 위계가 아니라 예외다.
        ///   예외는 화면마다 다른 값을 낳고, 그 순간 이 파일이 무의미해진다.
        /// </summary>
        public const int FontHuge  = 52;   // 웨이브 번호 · 윷 결과 — 화면당 하나뿐
        public const int FontTitle = 40;   // 화면 제목 · 트랙 이름
        public const int FontName  = 30;   // 버튼 · 카드 이름
        public const int FontSmall = 24;   // 보조 설명
        public const int FontTiny  = 20;   // 각주 · 해금 조건

        /// <summary>"#2E6F9E" 같은 문자열을 색으로. UpgradeTracks.TrackColor를 그대로 받는다.</summary>
        public static Color Parse(string cssHex, Color fallback)
        {
            if (!string.IsNullOrEmpty(cssHex) &&
                ColorUtility.TryParseHtmlString(cssHex, out var c)) return c;
            return fallback;
        }

        /// <summary>
        /// 한글이 나오는 글꼴을 찾는다. 화면마다 따로 만들면 서로 달라진다.
        ///
        /// ★ OS 글꼴은 에디터·PC에서만 동작한다. 모바일 빌드에는 폰트 에셋이 필요하다.
        /// </summary>
        public static Font ResolveFont(Font preferred)
        {
            if (preferred != null) return preferred;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
            string[] candidates = { "Malgun Gothic", "AppleSDGothicNeo-Regular", "NanumGothic", "Arial" };
            foreach (var name in candidates)
            {
                var f = Font.CreateDynamicFontFromOSFont(name, FontName);
                if (f != null) return f;
            }
#endif
            Debug.LogWarning("[UiTheme] 한글 글꼴을 못 찾았습니다. " +
                             "인스펙터의 Font 칸에 한글 폰트를 넣어주세요.");
            return null;
        }

        /// <summary>"E8873C" 또는 "E8873CFF" 형식. 실패하면 자홍색을 돌려줘 눈에 띄게 한다.</summary>
        private static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex, out var c)) return c;
            return new Color(1f, 0f, 1f, 1f);
        }
    }
}

using System;
using System.Collections.Generic;

namespace IdleDefense.Economy
{
    /// <summary>던지기 전에 외치는 것. None이면 안 불렀다.</summary>
    public enum YutCall { None = 0, Do, Gae, Geol, Yut, Mo }

    /// <summary>
    /// 윷 결과를 보상으로 바꾼다.
    ///
    /// ★ 이 클래스가 돌려주는 것은 **부적 배수 하나뿐**이다. 그게 계약이다.
    ///
    ///   2026-08-20 스윕 실측으로 확인된 것:
    ///     ① 엽전 일시금 — 웨이브 보상의 43배를 줘도 4.9% 단축. 지렛대가 아니다.
    ///     ② 엽전 배율   — 단축률이 **마이너스**. 최고웨이브가 233→235로 밀렸다.
    ///                     엽전 → 강화 레벨 → 진짜 DPS → **벽이 밀린다.** 철칙 위반.
    ///     ③ 부적 배수   — 1.8배까지 올려도 최고웨이브·코어가 숫자 하나 안 움직였다.
    ///
    ///   그래서 규칙은 "화폐를 주지 마라"가 아니라 이것이다.
    ///   **벽 판정에 들어가지 않는 축으로만 준다.** 지금 그 축은 TalismanMultiplier 하나다.
    ///
    /// ★ 여기에 코어(도깨비불)나 코인 배율을 돌려주는 API를 추가하지 마라.
    ///   추가되는 순간 90일 곡선이 조작 실력에 종속되고, 그건 화면으로 안 보인다.
    ///   YutScoringTests.계약_보상은_부적배수_하나뿐이다 가 이걸 지킨다.
    ///
    /// ★ 위로상은 안전하다.
    ///   ①의 실패가 이걸 가능하게 했다 — 엽전은 밸런스에 영향이 없다고 실측됐으므로
    ///   도·개·걸에 소액을 줘도 곡선이 안 흔들린다. 체감만 보고 액수를 정하면 된다.
    /// </summary>
    public static class YutScoring
    {
        /// <summary>
        /// 눈 하나당 부적 배수 증가분.
        ///
        /// 0.06인 근거 — 단축률 = 1 − 1/배수 라는 실측 법칙에서 역산했다.
        /// 최적 플레이(매 던지기마다 모 부르기, 하루 2판)가 17.6% 단축이 되고,
        /// 부적(33.2%)·광고(31%)보다 확실히 작다.
        /// **평균이 아니라 최적을 상한에 맞춘다** — 최댓값이 상한 아래면 나머지는 자동이다.
        /// </summary>
        public const double PipValue = 0.06;

        /// <summary>부르기를 맞혔을 때의 배율.</summary>
        public const double CallBonus = 2.0;

        /// <summary>
        /// 보상이 붙는 눈. **도·개·걸은 0이다.**
        ///
        /// ★ 이게 이 설계의 핵심이다.
        ///   실제 윷의 확률 구조는 이미 이분법이다 — 도·개·걸 84.5% vs 윷·모 15.5%.
        ///   초안은 보상을 +4/+8/+12/+16/+20으로 평평하게 깔아 그 구조를 죽였다.
        ///   어느 결과가 나와도 "그럭저럭"이 되면 사람이 빠지는 지점이 사라진다.
        ///   확률이 이분법이면 보상도 이분법이어야 한다.
        ///
        /// 모(5)가 윷(4)보다 후한 것은 고증이다 — 윷놀이에서 모는 5칸, 윷은 4칸이다.
        /// 윷이 더 희귀한데도 그렇다.
        /// </summary>
        public static int Pips(YutResult r)
        {
            switch (r)
            {
                case YutResult.Yut: return 4;
                case YutResult.Mo:  return 5;
                default: return 0;   // 도·개·걸 — 위로상만
            }
        }

        public static bool Matches(YutCall call, YutResult r)
        {
            switch (call)
            {
                case YutCall.Do:   return r == YutResult.Do;
                case YutCall.Gae:  return r == YutResult.Gae;
                case YutCall.Geol: return r == YutResult.Geol;
                case YutCall.Yut:  return r == YutResult.Yut;
                case YutCall.Mo:   return r == YutResult.Mo;
                default: return false;
            }
        }

        /// <summary>
        /// 던지기 한 번의 부적 배수. 1.0 이상이며 절대 그 아래로 안 내려간다.
        ///
        /// ★ 마이너스 없음. 방치형에서 벌은 이탈이다.
        ///   부르기 실패도 손해가 아니라 **기회 상실**이다 — 위로상만 못 받는다.
        /// </summary>
        public static double ThrowMultiplier(YutResult r, YutCall call = YutCall.None)
        {
            int pips = Pips(r);
            if (call == YutCall.None) return 1.0 + pips * PipValue;
            return Matches(call, r) ? 1.0 + pips * PipValue * CallBonus : 1.0;
        }

        /// <summary>
        /// 위로상(소액 엽전)을 받는가.
        /// 안 부르고 도·개·걸이 나왔을 때만이다. 부르고 빗나가면 이것도 없다.
        /// </summary>
        public static bool Consolation(YutResult r, YutCall call = YutCall.None)
            => call == YutCall.None && Pips(r) == 0;

        /// <summary>
        /// 한 판의 결과. **여기 있는 값이 이 시스템이 줄 수 있는 전부다.**
        /// 코어·코인 배율 필드가 없는 것이 의도다.
        /// </summary>
        public struct Outcome
        {
            /// <summary>이번 런의 부적 배수. 1.0이면 아무 일도 없었던 것.</summary>
            public double TalismanMultiplier;

            /// <summary>위로상을 받은 던지기 수. 액수는 호출부가 정한다.</summary>
            public int ConsolationCount;
        }

        /// <summary>
        /// 던진 결과들과 그때그때 부른 것을 받아 한 판의 배수를 낸다.
        /// 배수는 곱으로 쌓인다 — 연쇄가 나면 모 두 번에 배수가 2.2배까지 간다.
        /// </summary>
        public static Outcome Score(IList<YutResult> throws, IList<YutCall> calls = null)
        {
            var o = new Outcome { TalismanMultiplier = 1.0 };
            if (throws == null) return o;

            for (int i = 0; i < throws.Count; i++)
            {
                var call = (calls != null && i < calls.Count) ? calls[i] : YutCall.None;
                o.TalismanMultiplier *= ThrowMultiplier(throws[i], call);
                if (Consolation(throws[i], call)) o.ConsolationCount++;
            }
            return o;
        }

        /// <summary>
        /// UI가 제안할 부르기. **모 하나뿐이다.**
        ///
        /// ★ 다섯 개를 다 보여주면 넷이 함정이 된다.
        ///   실측(40만 판): 모 부르기 9.2% / 안 부르기 5.4% / **윷 부르기 1.5%**.
        ///   윷은 2.6% 확률이라 맞히려다 나머지를 다 버린다. 절대 이득이 안 난다.
        ///   버튼 하나면 결정이 홀짝이 된다 — "모 부를래, 말래?"
        /// </summary>
        public static readonly YutCall[] OfferedCalls = { YutCall.Mo };

        public static string DisplayName(YutCall c)
        {
            switch (c)
            {
                case YutCall.Do:   return "도";
                case YutCall.Gae:  return "개";
                case YutCall.Geol: return "걸";
                case YutCall.Yut:  return "윷";
                case YutCall.Mo:   return "모";
                default: return "-";
            }
        }
    }
}

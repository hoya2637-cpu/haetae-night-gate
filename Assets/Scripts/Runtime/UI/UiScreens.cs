using System;
using IdleDefense.Game;

namespace IdleDefense.UI
{
    /// <summary>
    /// 지금 어떤 전체화면이 열려 있는가. **한 곳만 안다.**
    ///
    /// ★ 이게 없으면 화면들이 서로를 직접 알아야 한다.
    ///   부적 화면이 열릴 때 윷 버튼을 숨기려면 부적 화면이 윷 화면을 참조해야 하고,
    ///   반대도 마찬가지다. 화면이 셋이면 참조가 여섯이 되고, 넷이면 열둘이 된다.
    ///   **실제로 부적 화면 위에 윷놀이 버튼이 떠 있었다** — 정렬 순서(55 > 50)만으로는
    ///   못 막는다. 순서를 뒤집으면 이번엔 반대로 뜬다. 순서 문제가 아니라 **정책 문제**다.
    ///
    /// ★ 그래서 규칙은 하나다 — **전체화면이 하나라도 열려 있으면 여는 버튼은 전부 숨는다.**
    ///   화면들은 서로를 모른 채 여기에만 말을 건다.
    ///
    /// ★ DebugHud.Suppressed도 여기서 한 번에 판정한다.
    ///   전에는 화면마다 각자 껐다 켰는데, 두 화면이 겹치면 나중에 닫힌 쪽이
    ///   "이제 아무도 안 열려 있다"고 잘못 판단해 디버그를 되살렸다.
    ///   여는 쪽이 아니라 **여기가** 셈을 한다.
    /// </summary>
    public static class UiScreens
    {
        private static object current;

        /// <summary>
        /// Play를 다시 눌렀을 때 지난 판의 상태가 남지 않게 한다.
        ///
        /// ★ 도메인 리로드를 끄면 static이 에디터 세션 내내 살아남는다.
        ///   그러면 화면을 연 채로 Play를 멈춘 다음날, 아무것도 안 열려 있는데
        ///   여는 버튼이 전부 사라진 채로 시작한다. 원인을 찾는 데 하루가 든다.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            current = null;
            Changed = null;
            DebugHud.Suppressed = false;
        }

        /// <summary>열려 있는 전체화면. 없으면 null.</summary>
        public static object Current => current;

        /// <summary>열림 상태가 바뀌었다. 여는 버튼을 가진 화면이 구독한다.</summary>
        public static event Action Changed;

        /// <summary>
        /// 전체화면이 열리거나 닫혔음을 알린다.
        ///
        /// 닫힘은 **자기가 연 것일 때만** 받는다. 그러지 않으면
        /// 이미 다른 화면이 주인인 상태에서 누가 닫히며 주인을 지워버린다.
        /// </summary>
        public static void SetOpen(object screen, bool open)
        {
            if (screen == null) return;

            if (open)
            {
                if (ReferenceEquals(current, screen)) return;
                current = screen;
            }
            else
            {
                if (!ReferenceEquals(current, screen)) return;
                current = null;
            }

            // IMGUI는 Canvas 위에 그려진다. 정렬 순서로는 못 가리므로 끄는 수밖에 없다.
            DebugHud.Suppressed = current != null;
            Changed?.Invoke();
        }

        /// <summary>내 '여는 버튼'을 보여도 되는가. 아무것도 안 열려 있을 때만이다.</summary>
        public static bool CanShowOpener() => current == null;
    }
}

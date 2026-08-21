using System;
using System.Collections.Generic;

namespace IdleDefense.Economy
{
    /// <summary>윷 한 판의 결과. 값은 그대로 보상 배수다.</summary>
    public enum YutResult
    {
        Do = 1,     // 도 — 배 1
        Gae = 2,    // 개 — 배 2
        Geol = 3,   // 걸 — 배 3
        Yut = 4,    // 윷 — 배 4 (한 번 더)
        Mo = 5,     // 모 — 배 0 (한 번 더)
    }

    /// <summary>
    /// 윷놀이 — 도깨비와의 첫 번째 놀이.
    ///
    /// ★ 룰을 각색하지 않았다. 고증이 곧 게임 디자인인 경우다.
    ///   실제 윷놀이의 "윷이나 모가 나오면 한 번 더 던진다"가
    ///   그대로 연쇄 보상이 된다. 우리가 만든 규칙이 아니라 500년 된 규칙이다.
    ///
    /// ★ 동전 4개를 쓰지 않는다 — 이게 이 파일에서 가장 중요한 결정이다.
    ///   균등 확률(p=0.5)이면 모가 1/16(6.25%)로 떨어져
    ///   **가장 신나는 결과가 가장 안 나오는** 게임이 된다.
    ///   실제 윷가락은 등이 둥글어 엎어질 확률이 높고(통상 0.6),
    ///   그래서 모가 13%로 윷(2.6%)보다 흔하다. 그게 윷놀이가 재미있는 이유다.
    ///
    /// ★ 마이너스가 없다.
    ///   실제 윷놀이에는 뒷도(백도)가 있지만 넣지 않는다.
    ///   방치형에서 벌은 이탈이다. 꽝은 있어도 손해는 없다.
    ///
    /// ★ 이 클래스는 화폐를 모른다.
    ///   배수만 돌려주고, 그 배수로 무엇을 지급할지는 GameController가 정한다.
    ///   미니게임이 직접 지갑을 만지면 "도깨비불은 절대 금지"라는 철칙을
    ///   지키는 자리가 여러 곳으로 흩어진다.
    /// </summary>
    public class YutGame
    {
        /// <summary>
        /// 윷가락 하나가 '엎어질'(등이 위로) 확률.
        ///
        /// 0.5가 아니라 0.6인 것이 이 게임의 확률 분포 전부를 결정한다.
        /// 실물 윷가락의 단면이 반원이라 등 쪽이 더 자주 위로 온다.
        /// </summary>
        public const double BackProbability = 0.6;

        /// <summary>윷가락 개수. 4개는 규칙이지 조절값이 아니다.</summary>
        public const int StickCount = 4;

        /// <summary>
        /// 연쇄 상한. 윷·모가 계속 나오면 이론상 끝나지 않는다.
        ///
        /// 확률적으로는 20연속이 1/10^16 수준이라 실제로는 안 걸린다.
        /// 그래도 상한을 두는 이유는 난수 생성기가 고장났을 때
        /// 게임이 멈추는 대신 이상한 값을 내고 넘어가게 하기 위해서다.
        /// 멈춘 게임은 원인을 못 찾지만, 이상한 값은 테스트가 잡는다.
        /// </summary>
        public const int MaxChain = 20;

        private readonly Random rng;

        public YutGame(int seed) { rng = new Random(seed); }
        public YutGame() { rng = new Random(); }

        /// <summary>한 번 던진다.</summary>
        public YutResult Throw()
        {
            int backs = 0;
            for (int i = 0; i < StickCount; i++)
                if (rng.NextDouble() < BackProbability) backs++;

            // 배(평평한 면)가 위로 온 개수가 곧 이름이다.
            // 4개 전부 엎어지면(배 0개) 모, 4개 전부 뒤집히면(배 4개) 윷.
            int bellies = StickCount - backs;
            switch (bellies)
            {
                case 0: return YutResult.Mo;
                case 1: return YutResult.Do;
                case 2: return YutResult.Gae;
                case 3: return YutResult.Geol;
                default: return YutResult.Yut;
            }
        }

        /// <summary>윷·모는 한 번 더 던진다.</summary>
        public static bool ThrowsAgain(YutResult r)
            => r == YutResult.Yut || r == YutResult.Mo;

        /// <summary>한 판의 결과. 던진 순서를 그대로 담는다 — 연출이 이 순서를 쓴다.</summary>
        public struct Session
        {
            /// <summary>던진 순서. 마지막 항목을 뺀 나머지는 전부 윷 또는 모다.</summary>
            public List<YutResult> Throws;

            /// <summary>배수 합계. 기본 보상에 곱할 값.</summary>
            public int Multiplier;

            /// <summary>연쇄 상한에 걸려 끊겼는가. 정상 플레이에서는 절대 참이 아니다.</summary>
            public bool ChainCapped;
        }

        /// <summary>
        /// 한 판을 끝까지 돌린다.
        /// 윷·모가 나오는 동안 계속 던지고, 배수를 전부 더한다.
        /// </summary>
        public Session Play()
        {
            var s = new Session { Throws = new List<YutResult>(4) };

            for (int i = 0; i < MaxChain; i++)
            {
                var r = Throw();
                s.Throws.Add(r);
                s.Multiplier += (int)r;
                if (!ThrowsAgain(r)) return s;
            }

            s.ChainCapped = true;
            return s;
        }

        // ─────────────────────────────────────────
        // 이론값 — 테스트와 밸런스 산정이 같은 수를 보게 한다

        /// <summary>결과 하나가 나올 이론 확률.</summary>
        public static double Probability(YutResult r)
        {
            double p = BackProbability;
            switch (r)
            {
                case YutResult.Mo:   return Pow(p, 4);                        // 배 0
                case YutResult.Do:   return 4 * (1 - p) * Pow(p, 3);          // 배 1
                case YutResult.Gae:  return 6 * Pow(1 - p, 2) * Pow(p, 2);    // 배 2
                case YutResult.Geol: return 4 * Pow(1 - p, 3) * p;            // 배 3
                case YutResult.Yut:  return Pow(1 - p, 4);                    // 배 4
                default: return 0.0;
            }
        }

        /// <summary>한 번 던졌을 때의 기대 배수. 연쇄를 세지 않는다.</summary>
        public static double ExpectedSingle()
        {
            double e = 0.0;
            foreach (YutResult r in Enum.GetValues(typeof(YutResult)))
                e += (int)r * Probability(r);
            return e;
        }

        /// <summary>
        /// 연쇄를 포함한 한 판의 기대 배수.
        ///
        ///   E = E1 / (1 - p_again)
        ///
        /// 기본 보상 계수를 정할 때 이 값으로 역산한다.
        /// p = 0.6에서 약 2.66이다.
        /// </summary>
        public static double ExpectedSession()
        {
            double again = Probability(YutResult.Yut) + Probability(YutResult.Mo);
            return ExpectedSingle() / (1.0 - again);
        }

        /// <summary>표시용 이름. 화면에 한 글자로 크게 띄운다.</summary>
        public static string DisplayName(YutResult r)
        {
            switch (r)
            {
                case YutResult.Do:   return "도";
                case YutResult.Gae:  return "개";
                case YutResult.Geol: return "걸";
                case YutResult.Yut:  return "윷";
                case YutResult.Mo:   return "모";
                default: return "?";
            }
        }

        private static double Pow(double b, int e)
        {
            double r = 1.0;
            for (int i = 0; i < e; i++) r *= b;
            return r;
        }
    }
}

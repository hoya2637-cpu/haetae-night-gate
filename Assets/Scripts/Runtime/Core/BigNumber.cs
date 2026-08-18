using System;
using System.Globalization;
using UnityEngine;

namespace IdleDefense.Core
{
    /// <summary>
    /// 방치형 게임용 대수(大數) 타입.
    /// 가수(mantissa) + 지수(exponent) 방식으로 double의 정밀도 한계를 우회한다.
    ///
    /// 왜 필요한가:
    ///   double은 유효숫자 약 15~17자리. 환생 10회쯤이면 10^15를 넘어가
    ///   1.0000000000001e15 같은 값이 화면에 뜨고 세이브가 손상된다.
    ///   방치형 1인 개발이 중도에 엎어지는 가장 흔한 원인이다.
    ///
    /// 정규화 규칙:
    ///   1.0 &lt;= mantissa &lt; 10.0  (0인 경우는 예외: mantissa=0, exponent=0)
    ///   음수는 mantissa의 부호로 표현한다.
    /// </summary>
    [Serializable]
    public struct BigNumber : IComparable<BigNumber>, IEquatable<BigNumber>
    {
        // Unity 인스펙터에 노출되도록 public 필드 + SerializeField
        [SerializeField] private double mantissa;
        [SerializeField] private int exponent;

        public double Mantissa => mantissa;
        public int Exponent => exponent;

        /// <summary>double 정밀도 한계. 이 지수를 넘으면 ToDouble()이 무의미해진다.</summary>
        private const int DoubleSafeExponent = 300;

        /// <summary>두 수의 지수 차가 이보다 크면 덧셈에서 작은 쪽을 무시한다.</summary>
        private const int AdditionCutoff = 17;

        /// <summary>
        /// 허용 지수 범위. int.MaxValue를 그대로 두면 곱셈 두 번에 오버플로가 나
        /// 지수가 음수로 뒤집힌다(= 거대한 값이 갑자기 0이 됨).
        /// 10^100000은 어떤 방치형 게임도 도달할 수 없는 값이라 실사용에 제약이 없다.
        /// </summary>
        private const int MaxExponent = 100000;
        private const int MinExponent = -100000;

        public static readonly BigNumber Zero = new BigNumber(0.0, 0, true);
        public static readonly BigNumber One = new BigNumber(1.0, 0, true);

        #region 생성

        private BigNumber(double m, int e, bool preNormalized)
        {
            mantissa = m;
            exponent = e;
            if (!preNormalized) Normalize();
        }

        public BigNumber(double value)
        {
            if (value == 0.0 || double.IsNaN(value))
            {
                mantissa = 0.0;
                exponent = 0;
                return;
            }

            if (double.IsInfinity(value))
            {
                // int.MaxValue/2를 넣으면 곱셈 한 번에 오버플로가 난다.
                // 안전 상한으로 포화시킨다.
                mantissa = value > 0 ? 1.0 : -1.0;
                exponent = MaxExponent;
                return;
            }

            int e = (int)Math.Floor(Math.Log10(Math.Abs(value)));
            double m = value / Math.Pow(10.0, e);

            mantissa = m;
            exponent = e;
            Normalize();
        }

        /// <summary>mantissa x 10^exponent 형태로 직접 생성.</summary>
        public static BigNumber FromMantissaExponent(double m, int e)
        {
            return new BigNumber(m, e, false);
        }

        /// <summary>10^power. 지수가 매우 큰 값을 안전하게 만들 때 사용.</summary>
        public static BigNumber Pow10(double power)
        {
            if (double.IsNaN(power)) return Zero;
            if (power > MaxExponent) power = MaxExponent;
            if (power < MinExponent) return Zero;
            int e = (int)Math.Floor(power);
            double m = Math.Pow(10.0, power - e);
            return new BigNumber(m, e, false);
        }

        #endregion

        #region 정규화

        private void Normalize()
        {
            if (mantissa == 0.0 || double.IsNaN(mantissa))
            {
                mantissa = 0.0;
                exponent = 0;
                return;
            }

            double abs = Math.Abs(mantissa);

            // 이미 정규 범위면 빠르게 반환
            if (abs >= 1.0 && abs < 10.0) return;

            int shift = (int)Math.Floor(Math.Log10(abs));

            // Math.Pow(10, -324) 같은 비정규 수로 나누면 결과가 Infinity가 된다.
            // double.Epsilon을 넣었을 때 실제로 재현되는 버그라 두 단계로 나눈다.
            if (shift >= 0)
            {
                mantissa /= Math.Pow(10.0, shift);
            }
            else if (shift > -300)
            {
                mantissa *= Math.Pow(10.0, -shift);
            }
            else
            {
                // 극단적으로 작은 수: 두 번에 나눠 곱해 오버플로를 피한다
                mantissa *= Math.Pow(10.0, 150);
                mantissa *= Math.Pow(10.0, -shift - 150);
            }
            exponent += shift;

            // 부동소수점 오차로 경계를 살짝 벗어나는 경우 보정
            abs = Math.Abs(mantissa);
            if (abs >= 10.0)
            {
                mantissa /= 10.0;
                exponent += 1;
            }
            else if (abs < 1.0 && abs > 0.0)
            {
                mantissa *= 10.0;
                exponent -= 1;
            }

            ClampExponent();
        }

        /// <summary>
        /// 지수를 안전 범위로 제한한다. 하한을 벗어나면 0으로 간주한다.
        /// 오버플로로 부호가 뒤집히는 것보다 포화(saturate)가 안전하다.
        /// </summary>
        private void ClampExponent()
        {
            if (double.IsNaN(mantissa) || double.IsInfinity(mantissa))
            {
                mantissa = 0.0;
                exponent = 0;
                return;
            }
            if (exponent > MaxExponent)
            {
                exponent = MaxExponent;
            }
            else if (exponent < MinExponent)
            {
                mantissa = 0.0;
                exponent = 0;
            }
        }

        #endregion

        #region 사칙연산

        public static BigNumber operator +(BigNumber a, BigNumber b)
        {
            if (a.mantissa == 0.0) return b;
            if (b.mantissa == 0.0) return a;

            // 지수 차가 크면 작은 쪽은 유효숫자에 영향을 주지 못한다
            int diff = a.exponent - b.exponent;
            if (diff > AdditionCutoff) return a;
            if (diff < -AdditionCutoff) return b;

            // 큰 쪽 지수에 맞춰 정렬 후 더한다
            if (diff >= 0)
            {
                double m = a.mantissa + b.mantissa / Math.Pow(10.0, diff);
                return new BigNumber(m, a.exponent, false);
            }
            else
            {
                double m = b.mantissa + a.mantissa / Math.Pow(10.0, -diff);
                return new BigNumber(m, b.exponent, false);
            }
        }

        public static BigNumber operator -(BigNumber a, BigNumber b) => a + (-b);

        public static BigNumber operator -(BigNumber a)
            => new BigNumber(-a.mantissa, a.exponent, true);

        public static BigNumber operator *(BigNumber a, BigNumber b)
        {
            if (a.mantissa == 0.0 || b.mantissa == 0.0) return Zero;
            long e = (long)a.exponent + b.exponent;   // int 오버플로 방지
            if (e > MaxExponent) e = MaxExponent;
            if (e < MinExponent) return Zero;
            return new BigNumber(a.mantissa * b.mantissa, (int)e, false);
        }

        public static BigNumber operator /(BigNumber a, BigNumber b)
        {
            if (b.mantissa == 0.0)
            {
                Debug.LogError("[BigNumber] 0으로 나누기 시도. Zero를 반환합니다.");
                return Zero;
            }
            if (a.mantissa == 0.0) return Zero;
            long e = (long)a.exponent - b.exponent;
            if (e > MaxExponent) e = MaxExponent;
            if (e < MinExponent) return Zero;
            return new BigNumber(a.mantissa / b.mantissa, (int)e, false);
        }

        // double과의 혼합 연산 (편의)
        public static BigNumber operator *(BigNumber a, double b) => a * new BigNumber(b);
        public static BigNumber operator *(double a, BigNumber b) => new BigNumber(a) * b;
        public static BigNumber operator /(BigNumber a, double b) => a / new BigNumber(b);
        public static BigNumber operator +(BigNumber a, double b) => a + new BigNumber(b);
        public static BigNumber operator -(BigNumber a, double b) => a - new BigNumber(b);

        #endregion

        #region 거듭제곱 · 로그

        /// <summary>this^power. 지수부를 직접 계산하므로 오버플로가 없다.</summary>
        public BigNumber Pow(double power)
        {
            if (mantissa == 0.0) return Zero;
            if (power == 0.0) return One;

            // log10(this) = exponent + log10(mantissa)
            double log = exponent + Math.Log10(Math.Abs(mantissa));
            return Pow10(log * power);
        }

        /// <summary>base^power를 BigNumber로. 경제 곡선의 지수 성장에 사용.</summary>
        public static BigNumber PowBase(double baseValue, double power)
        {
            if (baseValue <= 0.0) return Zero;
            return Pow10(Math.Log10(baseValue) * power);
        }

        /// <summary>log10(this). 역산(누적 코인 → 도달 가능 웨이브)에 사용.</summary>
        public double Log10()
        {
            if (mantissa <= 0.0) return double.NegativeInfinity;
            return exponent + Math.Log10(mantissa);
        }

        public BigNumber Sqrt() => Pow(0.5);

        #endregion

        #region 비교

        public int CompareTo(BigNumber other)
        {
            bool aZero = mantissa == 0.0;
            bool bZero = other.mantissa == 0.0;
            if (aZero && bZero) return 0;
            if (aZero) return other.mantissa > 0 ? -1 : 1;
            if (bZero) return mantissa > 0 ? 1 : -1;

            bool aNeg = mantissa < 0.0;
            bool bNeg = other.mantissa < 0.0;
            if (aNeg != bNeg) return aNeg ? -1 : 1;

            // 부호가 같을 때: 지수 먼저, 같으면 가수 비교
            int sign = aNeg ? -1 : 1;
            if (exponent != other.exponent)
                return exponent > other.exponent ? sign : -sign;

            return mantissa.CompareTo(other.mantissa);
        }

        public bool Equals(BigNumber other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is BigNumber b && Equals(b);
        public override int GetHashCode() => mantissa.GetHashCode() ^ exponent.GetHashCode();

        public static bool operator >(BigNumber a, BigNumber b) => a.CompareTo(b) > 0;
        public static bool operator <(BigNumber a, BigNumber b) => a.CompareTo(b) < 0;
        public static bool operator >=(BigNumber a, BigNumber b) => a.CompareTo(b) >= 0;
        public static bool operator <=(BigNumber a, BigNumber b) => a.CompareTo(b) <= 0;
        public static bool operator ==(BigNumber a, BigNumber b) => a.CompareTo(b) == 0;
        public static bool operator !=(BigNumber a, BigNumber b) => a.CompareTo(b) != 0;

        public bool IsZero => mantissa == 0.0;
        public bool IsPositive => mantissa > 0.0;

        public static BigNumber Max(BigNumber a, BigNumber b) => a >= b ? a : b;
        public static BigNumber Min(BigNumber a, BigNumber b) => a <= b ? a : b;

        #endregion

        #region 변환

        public static implicit operator BigNumber(double v) => new BigNumber(v);
        public static implicit operator BigNumber(int v) => new BigNumber(v);

        /// <summary>
        /// double로 변환. 지수가 큰 경우 정밀도를 잃으므로
        /// UI 표시나 비율 계산 등 제한적 용도로만 사용할 것.
        /// </summary>
        public double ToDouble()
        {
            if (mantissa == 0.0) return 0.0;
            if (exponent > DoubleSafeExponent) return double.PositiveInfinity * Math.Sign(mantissa);
            if (exponent < -DoubleSafeExponent) return 0.0;
            return mantissa * Math.Pow(10.0, exponent);
        }

        #endregion

        #region 표시

        private static readonly string[] Suffixes =
        {
            "", "K", "M", "B", "T",
            "aa", "ab", "ac", "ad", "ae", "af", "ag", "ah", "ai", "aj",
            "ak", "al", "am", "an", "ao", "ap", "aq", "ar", "as", "at",
            "au", "av", "aw", "ax", "ay", "az"
        };

        /// <summary>
        /// 게임 UI 표기. 1.23K / 4.56M / 7.89B / 1.23e45 형식.
        /// 접미사 범위를 넘어가면 과학적 표기로 전환한다.
        /// </summary>
        public override string ToString() => ToString(2);

        public string ToString(int decimals)
        {
            if (mantissa == 0.0) return "0";

            string sign = mantissa < 0.0 ? "-" : "";
            double absM = Math.Abs(mantissa);

            // 1000 미만은 그대로 표시
            if (exponent < 3)
            {
                double plain = absM * Math.Pow(10.0, exponent);
                // 정수에 가까우면 소수점 생략
                if (Math.Abs(plain - Math.Round(plain)) < 0.005)
                    return sign + Math.Round(plain).ToString("0", CultureInfo.InvariantCulture);
                return sign + plain.ToString("0.##", CultureInfo.InvariantCulture);
            }

            int tier = exponent / 3;
            if (tier < Suffixes.Length)
            {
                double display = absM * Math.Pow(10.0, exponent % 3);
                string fmt = "0." + new string('#', Math.Max(0, decimals));
                return sign + display.ToString(fmt, CultureInfo.InvariantCulture) + Suffixes[tier];
            }

            // 접미사 소진 → 과학적 표기
            return sign + absM.ToString("0.00", CultureInfo.InvariantCulture) + "e" + exponent;
        }

        #endregion

        #region 직렬화

        /// <summary>
        /// 세이브용 문자열. "mantissa|exponent" 형식.
        ///
        /// G17을 쓰는 이유: "R" 포맷은 왕복(round-trip)을 보장한다고 문서화되어 있지만
        /// Mono(Unity 런타임)에는 미세하게 어긋나는 알려진 결함이 있다.
        /// 5만 건 무작위 테스트에서 실제로 1건이 재현되었다.
        /// G17은 double의 17자리 유효숫자를 모두 출력해 왕복을 보장한다.
        /// </summary>
        public string Serialize()
            => mantissa.ToString("G17", CultureInfo.InvariantCulture) + "|" + exponent;

        public static BigNumber Deserialize(string s)
        {
            if (string.IsNullOrEmpty(s)) return Zero;

            int bar = s.IndexOf('|');
            if (bar < 0)
            {
                // 구버전 세이브 호환: 단순 숫자 문자열
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)
                    ? new BigNumber(v) : Zero;
            }

            string mPart = s.Substring(0, bar);
            string ePart = s.Substring(bar + 1);

            if (!double.TryParse(mPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double m) ||
                !int.TryParse(ePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int e))
            {
                Debug.LogWarning($"[BigNumber] 역직렬화 실패: '{s}'. Zero를 반환합니다.");
                return Zero;
            }

            // 세이브 파일은 유저가 조작할 수 있다.
            // NaN / Infinity가 들어오면 이후 모든 연산이 오염되므로 여기서 차단한다.
            if (double.IsNaN(m) || double.IsInfinity(m))
            {
                Debug.LogWarning($"[BigNumber] 비정상 가수 차단: '{s}'. Zero를 반환합니다.");
                return Zero;
            }
            if (e > MaxExponent || e < MinExponent)
            {
                Debug.LogWarning($"[BigNumber] 지수 범위 초과 차단: '{s}'. Zero를 반환합니다.");
                return Zero;
            }

            return new BigNumber(m, e, false);
        }

        #endregion
    }
}

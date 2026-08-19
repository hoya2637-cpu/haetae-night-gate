using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IdleDefense.Core;

namespace IdleDefense.Analytics
{
    /// <summary>
    /// 계측 이벤트 하나. 순수 C#이며 Unity에 의존하지 않는다.
    ///
    /// MonoBehaviour에 로직을 두지 않는 이유 —
    /// P0의 근본 원인이 "검증이 실제 코드 경로에 닿지 못한 것"이었다.
    /// 계측 로직을 Update() 안에 두면 EditMode 테스트가 못 타는 사각지대가 다시 생긴다.
    /// 여기(순수 계층)에 로직을 두고 Recorder는 구독만 하는 얇은 접착층으로 남긴다.
    /// </summary>
    public sealed class AnalyticsEvent
    {
        public string Name { get; }
        public int Version { get; }
        public long TimeUnixMs { get; }

        private readonly Dictionary<string, object> fields = new Dictionary<string, object>();

        public AnalyticsEvent(string name, int version, long timeUnixMs)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("이벤트 이름이 비었습니다", nameof(name));
            if (version <= 0)
                throw new ArgumentException($"event_version은 1 이상이어야 합니다: {name}", nameof(version));

            Name = name;
            Version = version;
            TimeUnixMs = timeUnixMs;

            fields["event_name"] = name;
            fields["event_version"] = version;
            fields["event_time"] = timeUnixMs;
        }

        public IReadOnlyDictionary<string, object> Fields => fields;

        public AnalyticsEvent Set(string key, object value)
        {
            fields[key] = value;
            return this;
        }

        /// <summary>
        /// BigNumber 전용. <c>key_log10</c>(분석용)과 <c>key_raw</c>(디버그용)로 나눠 담는다.
        ///
        /// ★ 이름을 Set으로 두면 안 된다.
        ///   BigNumber에 int/double 암시적 변환이 있어서, Set(string, object)와
        ///   오버로드가 겹치면 C#이 BigNumber 쪽을 고른다
        ///   (BigNumber -> object 변환은 있고 그 반대는 없어 BigNumber가 '더 나은' 타입).
        ///   그러면 run_id, tier, wave 같은 정수 필드가 전부 _log10/_raw로 새어 나간다.
        ///   실제로 그 버그가 났고 스키마 완결성 테스트가 잡았다.
        /// </summary>
        public AnalyticsEvent SetBig(string key, BigNumber value)
        {
            // 코인은 10^15를 쉽게 넘어 대부분의 SDK가 숫자 필드로 못 받는다.
            // 분석의 기본 단위는 log10이며, 원본 문자열은 디버그용으로만 붙인다.
            fields[key + "_log10"] = Log10OrZero(value);
            fields[key + "_raw"] = value.Serialize();
            return this;
        }

        /// <summary>
        /// 0 이하는 0으로 보낸다. 1 미만의 코인은 구분되지 않지만
        /// 분석 대상 자릿수가 10^3~10^20이라 실무상 문제가 없다.
        /// </summary>
        public static double Log10OrZero(BigNumber v)
            => v.IsZero || !v.IsPositive ? 0.0 : v.Log10();

        public bool Has(string key) => fields.ContainsKey(key);

        public object Get(string key) => fields.TryGetValue(key, out var v) ? v : null;

        /// <summary>JSON 한 줄. SDK가 붙기 전까지 파일/콘솔 싱크가 쓴다.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            bool first = true;
            foreach (var kv in fields)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Escape(kv.Key)).Append("\":");
                AppendValue(sb, kv.Value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: sb.Append('"').Append(Escape(s)).Append('"'); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case double d:
                    // NaN/Infinity는 JSON에 없다. 분석 파이프라인이 깨지므로 0으로 보낸다.
                    sb.Append(double.IsNaN(d) || double.IsInfinity(d)
                        ? "0" : d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                // ★ float를 빠뜨리면 안 된다.
                //   Unity의 시간 값(Time.realtimeSinceStartup 등)이 전부 float라
                //   기본 분기로 떨어져 "32.58694" 처럼 따옴표가 붙은 문자열이 된다.
                //   그러면 분석 도구가 컬럼을 STRING으로 잡아 평균·중앙값이 계산되지 않는다.
                //   실측으로 session_end의 duration_sec이 이렇게 새고 있었다.
                case float f:
                    sb.Append(float.IsNaN(f) || float.IsInfinity(f)
                        ? "0" : ((double)f).ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int[] arr:
                    sb.Append('[');
                    for (int k = 0; k < arr.Length; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append(arr[k].ToString(CultureInfo.InvariantCulture));
                    }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('"').Append(Escape(v.ToString())).Append('"');
                    break;
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

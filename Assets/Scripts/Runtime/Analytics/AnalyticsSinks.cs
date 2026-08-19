using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace IdleDefense.Analytics
{
    /// <summary>
    /// 에디터 콘솔 출력. 스키마 검증과 첫 통합 테스트용이다.
    /// SDK를 붙이기 전에 "이벤트가 예상 순서로 나오는가"를 눈으로 확인한다.
    /// </summary>
    public sealed class EditorLogSink : IAnalyticsSink
    {
        private readonly bool verbose;

        /// <param name="verbose">true면 JSON 전문, false면 이벤트 이름과 핵심 필드만</param>
        public EditorLogSink(bool verbose = false) => this.verbose = verbose;

        public bool Send(IReadOnlyList<AnalyticsEvent> batch)
        {
            var sb = new StringBuilder();
            sb.Append("[Analytics] ").Append(batch.Count).AppendLine("건");
            foreach (var e in batch)
            {
                if (verbose) { sb.AppendLine(e.ToJson()); continue; }
                sb.Append("  ").Append(e.Name)
                  .Append("  run=").Append(e.Get("run_id"))
                  .Append(" tier=").Append(e.Get("tier"))
                  .Append(" wave=").Append(e.Get("wave"))
                  .Append(" sinceAsc=").Append(e.Get("runs_since_ascend"))
                  .AppendLine();
            }
            Debug.Log(sb.ToString());
            return true;
        }
    }

    /// <summary>
    /// 로컬 JSONL 파일. 소프트런치 디버깅과 파생 지표 계산에 쓴다.
    ///
    /// 한 줄에 이벤트 하나(JSON Lines)라 중간에 잘려도 앞부분은 살아남고,
    /// 파이썬·스프레드시트로 바로 읽을 수 있다.
    /// </summary>
    public sealed class FileSink : IAnalyticsSink
    {
        private readonly string path;

        public FileSink(string fileName = "analytics.jsonl")
        {
            path = Path.Combine(Application.persistentDataPath, fileName);
        }

        public string Path_ => path;

        public bool Send(IReadOnlyList<AnalyticsEvent> batch)
        {
            try
            {
                var sb = new StringBuilder(batch.Count * 256);
                foreach (var e in batch) sb.AppendLine(e.ToJson());
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                // 실패를 삼키면 안 된다. false를 돌려주면 버퍼가 이벤트를 보관한다.
                Debug.LogWarning($"[Analytics] 파일 기록 실패: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>여러 싱크에 동시 전송. 하나라도 실패하면 실패로 본다.</summary>
    public sealed class CompositeSink : IAnalyticsSink
    {
        private readonly IAnalyticsSink[] sinks;

        public CompositeSink(params IAnalyticsSink[] sinks)
            => this.sinks = sinks ?? Array.Empty<IAnalyticsSink>();

        public bool Send(IReadOnlyList<AnalyticsEvent> batch)
        {
            bool ok = true;
            foreach (var s in sinks) ok &= s.Send(batch);
            return ok;
        }
    }
}

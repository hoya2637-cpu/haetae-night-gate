using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace IdleDefense.Save
{
    /// <summary>
    /// 세이브 입출력. 원자적 쓰기 + 백업으로 손상을 막는다.
    /// 방치형은 세이브가 곧 자산이라 손상 시 유저가 즉시 이탈한다.
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "save.json";
        private const string BackupName = "save.backup.json";
        private const string TempName = "save.tmp";

        private static string Dir => Application.persistentDataPath;
        private static string Path(string n) => System.IO.Path.Combine(Dir, n);

        /// <summary>
        /// 세이브 무결성 체크섬. 암호학적 보안이 목적이 아니라
        /// 디스크 손상이나 부분 기록을 감지하는 것이 목적이다.
        /// (치팅 방지는 서버 검증 영역이며 클라이언트로는 막을 수 없다)
        /// </summary>
        private static string Checksum(string payload)
        {
            unchecked
            {
                uint h = 2166136261u;                 // FNV-1a
                foreach (char ch in payload)
                {
                    h ^= ch;
                    h *= 16777619u;
                }
                return h.ToString("X8");
            }
        }

        private const string Marker = "\n#chk:";

        public static void Save(GameState state)
        {
            if (state == null) return;
            state.LastSeenUtc = DateTime.UtcNow;
            state.saveVersion = SaveMigration.CurrentVersion;

            try
            {
                string body = JsonUtility.ToJson(state, prettyPrint: false);
                string json = body + Marker + Checksum(body);

                // 임시 파일에 먼저 쓰고 교체 — 쓰기 도중 앱이 죽어도 기존 세이브가 남는다
                File.WriteAllText(Path(TempName), json);

                // 쓴 내용을 실제로 되읽어 검증한다. 여기서 깨지면 교체하지 않는다.
                if (Parse(File.ReadAllText(Path(TempName))) == null)
                {
                    Debug.LogError("[SaveSystem] 임시 파일 검증 실패. 기존 세이브를 유지합니다.");
                    File.Delete(Path(TempName));
                    return;
                }

                if (File.Exists(Path(FileName)))
                    File.Copy(Path(FileName), Path(BackupName), overwrite: true);

                File.Copy(Path(TempName), Path(FileName), overwrite: true);
                File.Delete(Path(TempName));

                // 첫 저장이라 백업이 없으면 지금 만들어 둔다.
                // 이게 없으면 최초 저장 직후의 손상이 곧 전체 손실이 된다.
                if (!File.Exists(Path(BackupName)))
                    File.Copy(Path(FileName), Path(BackupName), overwrite: true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 실패: {e.Message}");
            }
        }

        /// <summary>
        /// 세이브 로드. config를 넘기면 값 범위 검증이 게임 설정 기준으로 정확해진다.
        /// </summary>
        public static GameState Load(Data.EconomyConfig config = null)
        {
            var s = TryLoad(Path(FileName));
            if (s == null)
            {
                Debug.LogWarning("[SaveSystem] 기본 세이브 로드 실패. 백업을 시도합니다.");
                s = TryLoad(Path(BackupName));
            }
            if (s == null)
            {
                Debug.Log("[SaveSystem] 세이브 없음. 새 게임을 시작합니다.");
                s = GameState.CreateNew();
            }
            s.EnsureIntegrity(config);

            // 구버전 세이브를 최신 구조로 올린다. 변환됐으면 즉시 저장해 굳힌다.
            if (SaveMigration.Migrate(s)) Save(s);

            return s;
        }

        private static GameState TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] {path} 로드 실패: {e.Message}");
                return null;
            }
        }

        /// <summary>본문 + 체크섬 문자열을 GameState로. 실패 시 null.</summary>
        private static GameState Parse(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return null;

                string body = json;
                int mark = json.LastIndexOf(Marker, StringComparison.Ordinal);
                if (mark >= 0)
                {
                    body = json.Substring(0, mark);
                    string expected = json.Substring(mark + Marker.Length).Trim();
                    string actual = Checksum(body);
                    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"[SaveSystem] 체크섬 불일치 " +
                                         $"(기대 {expected}, 실제 {actual}). 손상된 세이브입니다.");
                        return null;
                    }
                }
                // 마커가 없으면 체크섬 도입 이전 세이브 — 그대로 통과시킨다

                var state = JsonUtility.FromJson<GameState>(body);

                // 파서가 예외를 던지지 않고 빈 객체를 돌려주는 경우가 있다.
                // 필수 필드를 확인해 쓰레기 세이브를 정상으로 오인하지 않게 한다.
                if (state == null ||
                    state.saveVersion <= 0 ||
                    string.IsNullOrEmpty(state.coinSerialized))
                {
                    Debug.LogWarning("[SaveSystem] 세이브 내용이 유효하지 않습니다.");
                    return null;
                }

                return state;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 세이브 파싱 실패: {e.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            foreach (string n in new[] { FileName, BackupName, TempName })
                try { if (File.Exists(Path(n))) File.Delete(Path(n)); } catch { }
        }
    }
}

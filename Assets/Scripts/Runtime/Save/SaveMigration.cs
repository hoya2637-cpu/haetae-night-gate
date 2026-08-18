using System;
using UnityEngine;

namespace IdleDefense.Save
{
    /// <summary>
    /// 세이브 버전 마이그레이션.
    ///
    /// 왜 지금 만드는가:
    ///   출시 후 필드가 하나라도 추가되면 기존 유저 세이브를 살려야 한다.
    ///   그때 가서 만들면 이미 구버전 세이브가 필드에 흩어져 있어 손댈 수 없다.
    ///   지금은 v1 하나뿐이라 골격만 두면 되지만, 나중엔 불가능하다.
    ///
    /// 사용법:
    ///   구조를 바꿀 때마다 CurrentVersion을 올리고 Migrate에 케이스를 추가한다.
    ///   각 단계는 반드시 '한 버전만' 올린다. 연쇄 적용은 루프가 처리한다.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>현재 세이브 포맷 버전. 구조 변경 시 반드시 올릴 것.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// 세이브를 최신 버전으로 올린다.
        /// 반환값: 마이그레이션이 일어났으면 true (호출부가 즉시 저장하도록)
        /// </summary>
        public static bool Migrate(GameState state)
        {
            if (state == null) return false;

            if (state.saveVersion > CurrentVersion)
            {
                // 상위 버전 세이브 — 앱을 다운그레이드했거나 조작된 경우
                Debug.LogWarning($"[SaveMigration] 세이브 버전 {state.saveVersion}이 " +
                                 $"앱 버전 {CurrentVersion}보다 높습니다. 그대로 사용합니다.");
                return false;
            }

            bool changed = false;
            int guard = 0;

            while (state.saveVersion < CurrentVersion)
            {
                if (++guard > 64)
                {
                    Debug.LogError("[SaveMigration] 마이그레이션이 진행되지 않습니다. 중단합니다.");
                    break;
                }

                int from = state.saveVersion;
                if (!ApplyStep(state, from))
                {
                    Debug.LogError($"[SaveMigration] v{from} 단계를 처리할 수 없습니다.");
                    break;
                }

                if (state.saveVersion == from)
                {
                    Debug.LogError($"[SaveMigration] v{from}에서 버전이 오르지 않았습니다.");
                    break;
                }

                Debug.Log($"[SaveMigration] v{from} → v{state.saveVersion}");
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 한 버전만 올린다. 새 버전을 추가할 때 여기에 case를 넣는다.
        /// </summary>
        private static bool ApplyStep(GameState state, int fromVersion)
        {
            switch (fromVersion)
            {
                // ── 예시 (실제 추가 시 주석 해제하고 CurrentVersion을 2로) ──
                // case 1:
                //     // v1에는 gems가 없었다. 보상 차원에서 초기값을 준다.
                //     if (state.gems <= 0) state.gems = 100;
                //     state.saveVersion = 2;
                //     return true;
                //
                // case 2:
                //     // 오방색 5트랙 → 6트랙 확장. 기존 레벨을 앞에 복사한다.
                //     if (state.trackLevels.Length < 6)
                //     {
                //         var expanded = new int[6];
                //         Array.Copy(state.trackLevels, expanded, state.trackLevels.Length);
                //         state.trackLevels = expanded;
                //     }
                //     state.saveVersion = 3;
                //     return true;

                default:
                    return false;
            }
        }
    }
}

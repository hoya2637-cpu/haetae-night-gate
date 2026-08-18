using System;
using System.Collections.Generic;
using UnityEngine;

namespace IdleDefense.Ads
{
    /// <summary>
    /// AppLovin MAX 연동.
    ///
    /// SDK가 프로젝트에 없어도 컴파일되도록 APPLOVIN_MAX 심볼로 감쌌다.
    /// SDK 설치 후 Player Settings → Scripting Define Symbols에
    /// APPLOVIN_MAX 를 추가하면 실제 구현이 활성화된다.
    ///
    /// 콜백 구조에서 주의할 점:
    ///   OnAdReceivedRewardEvent 는 "보상을 줘야 한다"는 신호이고,
    ///   OnAdHiddenEvent 는 "광고가 닫혔다"는 신호로 서로 별개다.
    ///   보상 콜백에서 바로 지급하면 광고가 아직 화면에 떠 있는 상태에서
    ///   게임 로직이 돌아가고, 유저가 광고를 닫기 전에 UI가 바뀌어 버린다.
    ///   그래서 보상 여부를 플래그로 기억해 두었다가
    ///   OnAdHiddenEvent 시점에 성공/실패를 확정한다.
    /// </summary>
    public class AppLovinAdProvider : IRewardedAdProvider
    {
        private readonly Dictionary<RewardType, string> adUnitIds;
        private readonly Dictionary<string, RewardType> reverseLookup = new Dictionary<string, RewardType>();

        // 진행 중인 요청 (RewardedAdService가 단일 요청만 허용하므로 하나면 충분)
        private RewardType? showingType;
        private Action onRewardedCallback;
        private Action<string> onFailedCallback;
        private bool rewardEarned;

        private readonly Dictionary<RewardType, int> retryAttempts = new Dictionary<RewardType, int>();

        /// <summary>
        /// 광고 유닛 ID는 플랫폼별로 다르다.
        /// AppLovin 대시보드에서 발급받아 넘긴다.
        /// </summary>
        public AppLovinAdProvider(Dictionary<RewardType, string> unitIds)
        {
            adUnitIds = unitIds ?? throw new ArgumentNullException(nameof(unitIds));
            foreach (var kv in adUnitIds) reverseLookup[kv.Value] = kv.Key;

#if APPLOVIN_MAX
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent        += HandleLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent    += HandleLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += HandleDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += HandleRewardReceived;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent        += HandleHidden;
#else
            Debug.LogWarning("[AppLovin] APPLOVIN_MAX 심볼이 정의되지 않았습니다. " +
                             "광고가 동작하지 않습니다.");
#endif
        }

        // ─────────────────────────────────────────

        public bool IsReady(RewardType type)
        {
#if APPLOVIN_MAX
            return adUnitIds.TryGetValue(type, out string id) && MaxSdk.IsRewardedAdReady(id);
#else
            return false;
#endif
        }

        public void Load(RewardType type)
        {
#if APPLOVIN_MAX
            if (adUnitIds.TryGetValue(type, out string id)) MaxSdk.LoadRewardedAd(id);
#endif
        }

        public void Show(RewardType type, Action onRewarded, Action<string> onFailed)
        {
#if APPLOVIN_MAX
            if (!adUnitIds.TryGetValue(type, out string id))
            {
                onFailed?.Invoke($"{type} 광고 유닛 ID가 설정되지 않았습니다.");
                return;
            }
            if (!MaxSdk.IsRewardedAdReady(id))
            {
                onFailed?.Invoke("광고가 아직 준비되지 않았습니다.");
                Load(type);
                return;
            }

            showingType = type;
            onRewardedCallback = onRewarded;
            onFailedCallback = onFailed;
            rewardEarned = false;

            MaxSdk.ShowRewardedAd(id);
#else
            onFailed?.Invoke("AppLovin MAX가 빌드에 포함되지 않았습니다.");
#endif
        }

#if APPLOVIN_MAX
        private void HandleLoaded(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            if (reverseLookup.TryGetValue(adUnitId, out var type)) retryAttempts[type] = 0;
        }

        private void HandleLoadFailed(string adUnitId, MaxSdk.ErrorInfo errorInfo)
        {
            if (!reverseLookup.TryGetValue(adUnitId, out var type)) return;

            // 지수 백오프로 재시도. 최대 64초.
            retryAttempts.TryGetValue(type, out int attempt);
            attempt++;
            retryAttempts[type] = attempt;

            double delay = Math.Pow(2, Math.Min(6, attempt));
            AdRetryScheduler.Schedule((float)delay, () => Load(type));
        }

        private void HandleDisplayFailed(string adUnitId, MaxSdk.ErrorInfo errorInfo, MaxSdk.AdInfo adInfo)
        {
            Resolve(success: false, reason: errorInfo.Message ?? "광고 표시 실패");
            if (reverseLookup.TryGetValue(adUnitId, out var type)) Load(type);
        }

        private void HandleRewardReceived(string adUnitId, MaxSdk.Reward reward, MaxSdk.AdInfo adInfo)
        {
            // 여기서 바로 지급하지 않는다. 광고가 아직 화면에 떠 있다.
            rewardEarned = true;
        }

        private void HandleHidden(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            // 광고가 닫힌 이 시점이 성공/실패를 확정할 자리다.
            Resolve(rewardEarned, rewardEarned ? null : "광고를 끝까지 시청하지 않았습니다.");
            if (reverseLookup.TryGetValue(adUnitId, out var type)) Load(type);
        }
#endif

        private void Resolve(bool success, string reason)
        {
            var ok = onRewardedCallback;
            var fail = onFailedCallback;

            // 콜백이 중복으로 와도 한 번만 처리되도록 먼저 비운다
            onRewardedCallback = null;
            onFailedCallback = null;
            showingType = null;
            rewardEarned = false;

            if (success) ok?.Invoke();
            else fail?.Invoke(reason ?? "알 수 없는 오류");
        }
    }

    /// <summary>
    /// 광고 재로드 지연 실행용. MonoBehaviour가 아닌 곳에서 Invoke를 쓸 수 없어 별도로 둔다.
    /// 씬에 하나 생성해두면 된다(RewardedAdService가 자동 생성해도 무방).
    /// </summary>
    public class AdRetryScheduler : MonoBehaviour
    {
        private static AdRetryScheduler instance;

        public static void Schedule(float delaySeconds, Action action)
        {
            if (instance == null)
            {
                var go = new GameObject("AdRetryScheduler");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<AdRetryScheduler>();
            }
            instance.StartCoroutine(instance.Run(delaySeconds, action));
        }

        private System.Collections.IEnumerator Run(float delay, Action action)
        {
            yield return new WaitForSeconds(delay);
            action?.Invoke();
        }
    }
}

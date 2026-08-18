using System;
using UnityEngine;

namespace IdleDefense.Ads
{
    /// <summary>보상 종류. 광고 배치마다 다른 보상이 나간다.</summary>
    public enum RewardType
    {
        OfflineDouble,   // 오프라인 보상 2배
        DokkaebiSummon,  // 도깨비방망이 재소환 (코인 2배 10초)
        SpeedBoost,      // 전투 배속
        Revive,          // 부활
    }

    /// <summary>
    /// 광고 SDK 추상화. AdMob / Unity Ads / AppLovin MAX 어느 쪽이든
    /// 이 인터페이스만 구현하면 게임 코드를 바꾸지 않고 교체할 수 있다.
    /// </summary>
    public interface IRewardedAdProvider
    {
        bool IsReady(RewardType type);
        void Load(RewardType type);

        /// <summary>
        /// 광고 표시. 시청을 끝까지 완료했을 때만 onRewarded를 호출해야 한다.
        /// 중간에 닫으면 onFailed를 호출한다.
        /// </summary>
        void Show(RewardType type, Action onRewarded, Action<string> onFailed);
    }

    /// <summary>
    /// 광고 보상의 유일한 통로.
    ///
    /// 핵심 원칙:
    ///   "광고 버튼을 눌렀다"와 "광고를 끝까지 봤다"는 절대 같은 사건이 아니다.
    ///   게임 로직은 이 서비스가 발급한 토큰이 있어야만 보상을 지급한다.
    ///
    /// 토큰 방식을 쓰는 이유:
    ///   GameController에 GrantXxx(bool watchedAd) 같은 API를 두면
    ///   UI에서 true를 넘기는 것만으로 보상을 받을 수 있다.
    ///   토큰은 이 서비스만 발급할 수 있고 1회용이라 그 경로가 막힌다.
    /// </summary>
    public class RewardedAdService : MonoBehaviour
    {
        /// <summary>
        /// 광고 시청 완료 증표. 1회용이며 이 서비스만 발급한다.
        /// </summary>
        public sealed class RewardToken
        {
            internal RewardToken(RewardType type, long issuedTicks)
            {
                Type = type;
                IssuedTicks = issuedTicks;
            }

            public RewardType Type { get; }
            internal long IssuedTicks { get; }
            internal bool Consumed { get; set; }
        }

        [SerializeField] private float tokenLifetimeSeconds = 60f;

        private IRewardedAdProvider provider;
        private RewardToken pending;

        /// <summary>
        /// 광고 요청이 진행 중인가. 한 번에 하나만 허용한다(single-flight).
        ///
        /// 왜 필요한가:
        ///   pending 토큰은 하나뿐이라, 두 광고가 겹치면 먼저 완료된 토큰이
        ///   나중 토큰에 덮어써져 조용히 무효가 된다.
        ///   유저는 광고를 다 봤는데 보상을 못 받는 상황이 되고,
        ///   원인 추적도 거의 불가능하다.
        ///   이 게임은 리워드 광고를 동시에 띄울 이유가 없으므로 잠금이 가장 깔끔하다.
        /// </summary>
        public bool IsRequestInFlight { get; private set; }

        /// <summary>진행 중인 요청의 종류. UI 버튼 비활성화에 쓴다.</summary>
        public RewardType? InFlightType { get; private set; }

        public event Action<RewardType> OnAdCompleted;
        public event Action<RewardType, string> OnAdFailed;

        /// <summary>요청 잠금 상태 변경. UI가 버튼을 잠그고 풀 때 구독한다.</summary>
        public event Action<bool> OnRequestLockChanged;

        /// <summary>부팅 시 실제 SDK 구현체를 주입한다.</summary>
        public void Initialize(IRewardedAdProvider adProvider)
        {
            provider = adProvider ?? throw new ArgumentNullException(nameof(adProvider));
            foreach (RewardType t in Enum.GetValues(typeof(RewardType)))
                provider.Load(t);
        }

        public bool IsReady(RewardType type) => provider != null && provider.IsReady(type);

        /// <summary>
        /// 광고 요청. 시청을 완료하면 onVerified가 토큰과 함께 호출된다.
        /// 이 토큰을 GameController에 넘겨야 보상이 지급된다.
        /// </summary>
        public void RequestReward(RewardType type,
                                  Action<RewardToken> onVerified,
                                  Action<string> onFailed = null)
        {
            if (provider == null)
            {
                onFailed?.Invoke("광고 모듈이 초기화되지 않았습니다.");
                return;
            }
            if (IsRequestInFlight)
            {
                onFailed?.Invoke($"이미 {InFlightType} 광고를 처리 중입니다.");
                return;
            }
            if (!provider.IsReady(type))
            {
                provider.Load(type);
                onFailed?.Invoke("광고를 아직 불러오지 못했습니다.");
                return;
            }

            SetLock(true, type);

            // SDK 콜백이 두 번 오는 경우가 실제로 있다.
            // 한 요청에서 보상이 두 번 나가지 않도록 여기서도 막는다.
            bool settled = false;

            provider.Show(type,
                onRewarded: () =>
                {
                    if (settled) return;
                    settled = true;

                    pending = new RewardToken(type, DateTime.UtcNow.Ticks);
                    SetLock(false, null);

                    OnAdCompleted?.Invoke(type);
                    onVerified?.Invoke(pending);
                    provider.Load(type);   // 다음 회차 미리 로드
                },
                onFailed: reason =>
                {
                    if (settled) return;
                    settled = true;

                    SetLock(false, null);

                    OnAdFailed?.Invoke(type, reason);
                    onFailed?.Invoke(reason);
                    provider.Load(type);
                });
        }

        private void SetLock(bool locked, RewardType? type)
        {
            IsRequestInFlight = locked;
            InFlightType = type;
            OnRequestLockChanged?.Invoke(locked);
        }

        /// <summary>
        /// 토큰 검증 및 소비. 게임 로직이 보상 직전에 호출한다.
        /// 한 번 쓴 토큰은 재사용할 수 없다.
        /// </summary>
        public bool ConsumeToken(RewardToken token, RewardType expected)
        {
            if (token == null) return false;
            if (token.Consumed) { Debug.LogWarning("[Ads] 이미 사용된 토큰입니다."); return false; }
            if (token.Type != expected) { Debug.LogWarning("[Ads] 토큰 종류가 다릅니다."); return false; }
            if (!ReferenceEquals(token, pending)) { Debug.LogWarning("[Ads] 발급되지 않은 토큰입니다."); return false; }

            double age = (DateTime.UtcNow.Ticks - token.IssuedTicks) / (double)TimeSpan.TicksPerSecond;
            if (age > tokenLifetimeSeconds) { Debug.LogWarning("[Ads] 토큰이 만료됐습니다."); return false; }

            token.Consumed = true;
            pending = null;
            return true;
        }
    }

    /// <summary>
    /// 에디터 테스트용 더미 구현.
    /// 실제 SDK를 붙이기 전까지 이걸로 흐름을 검증한다.
    /// 빌드에 포함되지 않도록 실제 출시 전 반드시 교체할 것.
    /// </summary>
    public class EditorFakeAdProvider : IRewardedAdProvider
    {
        public bool IsReady(RewardType type) => true;
        public void Load(RewardType type) { }

        public void Show(RewardType type, Action onRewarded, Action<string> onFailed)
        {
            Debug.Log($"[FakeAd] {type} 광고 시청 완료 처리");
            onRewarded?.Invoke();
        }
    }
}

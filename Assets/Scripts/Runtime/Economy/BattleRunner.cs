using System;
using IdleDefense.Core;
using IdleDefense.Data;

namespace IdleDefense.Economy
{
    /// <summary>
    /// 한 런의 전투 진행. MonoBehaviour에 의존하지 않는 순수 계산이다.
    ///
    /// 설계 의도:
    ///   적을 개별 오브젝트로 시뮬레이션하지 않는다.
    ///   웨이브 전체를 하나의 체력 풀로 다루고 DPS를 누적해 깎는다.
    ///   저발열이 이 게임의 스펙이므로, 적 100마리를 매 프레임 갱신하는 구조는 쓸 수 없다.
    ///   화면에는 대표 몇 마리만 연출로 보여주면 된다.
    ///
    /// 사용법:
    ///   var runner = new BattleRunner(config, state);
    ///   runner.BeginRun(startWave, startCoin);
    ///   매 프레임: runner.Tick(Time.deltaTime);
    ///   runner.IsWalled 가 true가 되면 런 종료 → 환생 가능
    /// </summary>
    public class BattleRunner
    {
        private readonly EconomyConfig cfg;

        public int CurrentWave { get; private set; }
        public BigNumber Coin { get; private set; }
        public BigNumber WaveHpRemaining { get; private set; }
        public BigNumber WaveHpTotal { get; private set; }

        /// <summary>현재 웨이브에 들인 시간(초). 벽 판정에 쓴다.</summary>
        public double WaveElapsed { get; private set; }

        /// <summary>런 전체 경과 시간(초).</summary>
        public double RunElapsed { get; private set; }

        /// <summary>벽에 부딪혀 런이 끝났는가.</summary>
        public bool IsWalled { get; private set; }

        public bool IsRunning { get; private set; }

        /// <summary>이번 런에서 도달한 최고 웨이브.</summary>
        public int DeepestWave { get; private set; }

        /// <summary>
        /// 현재 배속. 부스트가 만료되면 자동으로 1.0으로 돌아간다.
        ///
        /// 코루틴으로 관리하지 않는 이유:
        ///   광고를 두 번 보면 코루틴이 두 개 돌고, 먼저 시작한 쪽이 끝날 때
        ///   나중 부스트의 남은 시간까지 함께 꺼버린다.
        ///   남은 시간을 상태로 들고 Tick에서 깎는 편이 훨씬 안전하다.
        /// </summary>
        public double SpeedMultiplier { get; private set; } = 1.0;

        /// <summary>
        /// 배속 잔여 시간(초). UI 표시용.
        ///
        /// 전투가 진행 중일 때만 줄어든다. 벽에 부딪혀 런이 멈춰 있거나
        /// 유저가 메뉴를 보는 동안에는 소진되지 않는다.
        /// 광고로 산 것은 '실시간 10분'이 아니라 '전투 10분'이기 때문이다.
        /// 실시간으로 깎으면 오프라인 보상 화면을 읽는 사이에 사라진다.
        /// </summary>
        public double SpeedBoostRemaining { get; private set; }

        /// <summary>배속 최대 누적 시간(초). 광고 연속 시청으로 무한 연장되는 것을 막는다.</summary>
        public double SpeedBoostMaxSeconds { get; set; } = 600.0;

        /// <summary>이번 런에서 부활을 이미 썼는가. 런당 1회로 제한한다.</summary>
        public bool ReviveUsedThisRun { get; private set; }

        /// <summary>부활 가능 여부. UI 버튼 활성화 판단에 쓴다.</summary>
        public bool CanRevive => IsWalled && !ReviveUsedThisRun;

        /// <summary>도깨비방망이 발동 중이면 코인 2배.</summary>
        public bool DokkaebiActive { get; private set; }
        public double DokkaebiRemaining { get; private set; }

        // 외부에서 주입하는 배수 (환생 코어 + 오방색 트랙)
        public double AttackMultiplier { get; set; } = 1.0;
        public double CoinMultiplier { get; set; } = 1.0;

        /// <summary>
        /// 부적 소환으로 얻는 일시적 전투력 배수.
        ///
        /// AttackMultiplier와 분리한 이유:
        ///   부적이 준 피해에는 코인이 붙으면 안 된다.
        ///   붙으면 "부적 많이 쓰면 부자"가 되어 90일 커브가 무너진다.
        ///   그래서 DPS에는 곱하되 코인 계산에는 절대 쓰지 않는다.
        /// </summary>
        public double TalismanMultiplier { get; set; } = 1.0;

        /// <summary>웨이브를 클리어할 때마다 호출. (클리어한 웨이브, 획득 코인)</summary>
        public event Action<int, BigNumber> OnWaveCleared;

        /// <summary>벽에 부딪혀 런이 끝났을 때 호출. (도달 웨이브)</summary>
        public event Action<int> OnRunEnded;

        /// <summary>도깨비 등장. UI 연출과 광고 제안에 쓴다.</summary>
        public event Action OnDokkaebiAppeared;

        // ── 연출 계층 ──
        //
        // 전투 계산은 "HP -= DPS x dt"로 연속적이다. 저발열을 위해 그렇게 설계했다.
        // 그런데 그러면 화면에 '때리는 순간'이 없어 타격감이 나오지 않는다.
        //
        // 그래서 계산과 별개로 일정 간격의 '발사 이벤트'를 발생시킨다.
        //   - DPS가 아무리 커져도 발사 횟수는 고정 → 발열이 늘지 않는다
        //   - 대신 한 발의 표시 데미지가 커진다 → 성장이 눈에 보인다
        // 방치형에서 타격감을 내는 표준 방식이다.

        /// <summary>발사 정보. UI가 이걸 받아 투사체와 데미지 숫자를 띄운다.</summary>
        public struct ShotInfo
        {
            /// <summary>이번 발사의 표시 데미지 (실제 계산과 별개인 연출용 값)</summary>
            public BigNumber Damage;
            /// <summary>크리티컬 여부. 화면 흔들림과 색 변화에 쓴다.</summary>
            public bool IsCritical;
            /// <summary>이번 발사로 웨이브가 끝나는가. 마무리 연출용.</summary>
            public bool IsFinishing;
        }

        /// <summary>발사 간격(초). 짧을수록 타격이 잦지만 연출 부하가 는다.</summary>
        public double ShotInterval { get; set; } = 0.4;

        /// <summary>크리티컬 확률. 흑(黑) 트랙이 올리는 값을 외부에서 주입한다.</summary>
        public double CriticalChance { get; set; } = 0.05;

        /// <summary>발사. UI가 구독해 투사체·데미지 숫자·화면 흔들림을 처리한다.</summary>
        public event Action<ShotInfo> OnShotFired;

        /// <summary>웨이브의 마지막 일격. 폭발 연출용.</summary>
        public event Action<int> OnWaveFinisher;

        private double shotTimer;
        private readonly Random shotRandom = new Random();

        public BattleRunner(EconomyConfig config)
        {
            cfg = config ?? throw new ArgumentNullException(nameof(config));
        }

        // ─────────────────────────────────────────

        /// <summary>
        /// 런 시작. 오프라인 보상으로 건너뛴 웨이브부터 시작한다.
        /// </summary>
        public void BeginRun(int startWave, BigNumber startCoin)
        {
            CurrentWave = Math.Max(1, startWave);
            DeepestWave = CurrentWave;
            Coin = startCoin;
            WaveHpTotal = EconomyCore.WaveTotalHp(cfg, CurrentWave);
            WaveHpRemaining = WaveHpTotal;
            WaveElapsed = 0;
            RunElapsed = 0;
            IsWalled = false;
            IsRunning = true;
            DokkaebiActive = false;
            DokkaebiRemaining = 0;
            ReviveUsedThisRun = false;
            // 배속은 런을 넘어 유지된다. 광고로 산 시간이 환생 때문에 사라지면 안 된다.
        }

        /// <summary>현재 스탯 기준 DPS. 부적 효과가 포함된다.</summary>
        public BigNumber CurrentDps(int upgradeLevel)
            => EconomyCore.BaseDpsAtLevel(cfg, upgradeLevel)
             * AttackMultiplier * TalismanMultiplier;

        /// <summary>부적을 제외한 순수 DPS. 커브 검증용.</summary>
        public BigNumber BaseDpsWithoutTalisman(int upgradeLevel)
            => EconomyCore.BaseDpsAtLevel(cfg, upgradeLevel) * AttackMultiplier;

        /// <summary>
        /// 한 프레임 진행.
        /// deltaTime은 실제 경과 시간이며 SpeedMultiplier가 여기에 곱해진다.
        /// </summary>
        public void Tick(double deltaTime, int upgradeLevel)
        {
            if (!IsRunning || IsWalled) return;
            if (deltaTime <= 0) return;

            double dt = deltaTime * Math.Max(0.1, SpeedMultiplier);

            RunElapsed += dt;
            WaveElapsed += dt;

            // 배속 잔여시간 — 실제 경과 시간으로 깎는다.
            // dt(배속 적용분)로 깎으면 배속이 빠를수록 빨리 소진되어
            // "10분 2배속"이 5분 만에 끝나버린다.
            if (SpeedBoostRemaining > 0)
            {
                SpeedBoostRemaining -= deltaTime;
                if (SpeedBoostRemaining <= 0)
                {
                    SpeedBoostRemaining = 0;
                    SpeedMultiplier = 1.0;
                }
            }

            // 도깨비 지속시간
            if (DokkaebiActive)
            {
                DokkaebiRemaining -= dt;
                if (DokkaebiRemaining <= 0) DokkaebiActive = false;
            }

            // ── 벽 판정 ──
            //
            // ★ 반드시 피해 적용보다 '먼저' 한다.
            //
            // 뒤에 두면 이런 구멍이 생긴다:
            //   웨이브 잔여 체력이 한 틱의 부적 피해로 0이 되면
            //   아래의 조기 return에 걸려 벽 판정 자체가 실행되지 않는다.
            //   즉 부적이 충분히 세면 넘을 수 없는 벽을 그냥 통과해 버린다.
            //   그 순간 "부적은 속도만 바꾸고 도달점은 못 바꾼다"가 깨지고,
            //   보증의 성립 여부가 dt(프레임 간격)와 부적 세기에 종속된다.
            //
            // 벽은 '부적을 뺀 순수 성장'으로만 판정한다.
            // 부적의 역할은 "이미 넘을 수 있는 구간을 빨리 지나가는 것"이지
            // "못 넘던 벽을 넘는 것"이 아니다.
            var baseDps = BaseDpsWithoutTalisman(upgradeLevel);
            double baseClearSeconds = baseDps.IsZero || !baseDps.IsPositive
                ? double.PositiveInfinity
                : (WaveHpTotal / baseDps).ToDouble();

            if (baseClearSeconds > cfg.waveTimeWall)
            {
                IsWalled = true;
                IsRunning = false;
                OnRunEnded?.Invoke(DeepestWave);
                return;
            }

            var dps = CurrentDps(upgradeLevel);
            WaveHpRemaining -= dps * dt;

            // 연출용 발사 — 실제 계산에는 영향을 주지 않는다
            if (OnShotFired != null)
            {
                shotTimer += dt;
                while (shotTimer >= ShotInterval)
                {
                    shotTimer -= ShotInterval;
                    FireShot(dps);
                }
            }

            if (WaveHpRemaining <= BigNumber.Zero)
            {
                shotTimer = 0;
                OnWaveFinisher?.Invoke(CurrentWave);
                ClearWave();
                return;
            }

        }

        /// <summary>
        /// 연출용 발사 1회.
        /// 표시 데미지는 "DPS x 발사간격"이라 실제 누적 피해와 총량이 일치한다.
        /// 크리티컬은 표시상으로만 크게 보이고 실제 HP 계산은 이미 끝나 있다.
        /// (연출이 계산을 바꾸면 테스트가 무의미해진다)
        /// </summary>
        private void FireShot(BigNumber dps)
        {
            bool crit = shotRandom.NextDouble() < CriticalChance;
            var shown = dps * ShotInterval;
            if (crit) shown *= 2.0;

            OnShotFired.Invoke(new ShotInfo
            {
                Damage = shown,
                IsCritical = crit,
                IsFinishing = WaveHpRemaining <= dps * ShotInterval
            });
        }

        private void ClearWave()
        {
            var reward = EconomyCore.WaveCoinReward(cfg, CurrentWave) * CoinMultiplier;
            if (DokkaebiActive) reward *= 2.0;

            Coin += reward;
            OnWaveCleared?.Invoke(CurrentWave, reward);

            if (CurrentWave > DeepestWave) DeepestWave = CurrentWave;

            CurrentWave++;
            if (CurrentWave > cfg.maxWavePerRun)
            {
                IsRunning = false;
                OnRunEnded?.Invoke(DeepestWave);
                return;
            }

            WaveHpTotal = EconomyCore.WaveTotalHp(cfg, CurrentWave);
            WaveHpRemaining = WaveHpTotal;
            WaveElapsed = 0;
            shotTimer = 0;
        }

        /// <summary>
        /// 부적의 즉시 삭제(저승사자). 현재 웨이브의 '잔여' 체력만 비율로 깎는다.
        ///
        /// ★ WaveHpTotal은 절대 건드리지 않는다.
        ///   벽 판정은 WaveHpTotal / BaseDpsWithoutTalisman 이다.
        ///   총 체력을 깎으면 그 식이 바뀌어 부적이 벽을 밀어내게 되고,
        ///   "부적은 속도만 바꾸고 도달점은 못 바꾼다"는 원칙이 그 자리에서 무너진다.
        ///   90일 커브가 유저의 조작 실력마다 갈라지므로 경제 설계가 성립하지 않는다.
        ///
        /// 웨이브 클리어 판정은 여기서 하지 않는다. 다음 Tick이 처리한다.
        /// (클리어를 두 곳에서 하면 코인이 두 번 들어가는 사고가 난다)
        /// </summary>
        public void ExecuteFraction(double fraction)
        {
            if (!IsRunning || IsWalled) return;
            if (fraction <= 0.0) return;
            if (fraction > 1.0) fraction = 1.0;

            WaveHpRemaining -= WaveHpRemaining * fraction;
        }

        /// <summary>
        /// 배속 부스트 적용/연장.
        /// 이미 배속 중이면 남은 시간에 더하되 상한을 넘기지 않는다.
        /// </summary>
        public void ApplySpeedBoost(double multiplier, double durationSeconds)
        {
            if (multiplier < 1.0) return;

            SpeedMultiplier = Math.Max(SpeedMultiplier, multiplier);
            SpeedBoostRemaining = Math.Min(
                SpeedBoostRemaining + durationSeconds, SpeedBoostMaxSeconds);
        }

        /// <summary>
        /// 도깨비 소환. 지속시간 동안 코인 2배.
        /// 리워드 광고를 보면 다시 부를 수 있다.
        /// </summary>
        public void SummonDokkaebi(double duration = 10.0)
        {
            DokkaebiActive = true;
            DokkaebiRemaining = Math.Max(DokkaebiRemaining, duration);
            OnDokkaebiAppeared?.Invoke();
        }

        /// <summary>
        /// 부활. 벽에 부딪힌 뒤 현재 웨이브를 다시 시도한다.
        /// 웨이브를 건너뛰어 주지는 않는다 — 그러면 벽의 의미가 사라진다.
        ///
        /// 런당 1회로 제한하는 이유:
        ///   무제한이면 광고를 반복 시청해 벽을 영구히 넘길 수 있고,
        ///   그러면 벽과 환생이라는 게임의 중심 구조가 사라진다.
        ///   광고는 벽을 없애는 수단이 아니라 "한 번 더 기회"여야 한다.
        /// </summary>
        public bool Revive()
        {
            if (!CanRevive) return false;

            ReviveUsedThisRun = true;
            IsWalled = false;
            IsRunning = true;
            WaveElapsed = 0;
            WaveHpRemaining = WaveHpTotal;
            return true;
        }

        /// <summary>
        /// 코인 차감. 업그레이드 구매는 이 메서드를 통해서만 한다.
        /// Coin을 외부에서 직접 쓰게 열어두면 어디서 코인이 새는지 추적이 안 된다.
        /// </summary>
        /// <summary>
        /// 전투 밖에서 들어오는 엽전. 지금은 도깨비 놀이의 위로상뿐이다.
        ///
        /// ★ 이 통로로 밸런스를 밀 수 없다는 것이 실측으로 확인됐다.
        ///   런 시작 시 웨이브 보상의 43배를 넣어도 런 시간이 4.9%밖에 안 줄었다
        ///   (SimulationTests 스윕, 2026-08-20). 강화 비용이 지수적이라
        ///   목돈은 몇 레벨 더 사고 끝나고, 런의 결과는 런 도중 획득이 지배한다.
        ///
        /// ★ 다만 '배율'로 주면 이야기가 완전히 달라진다.
        ///   코인 배율을 올렸더니 최고웨이브가 233 → 235로 밀렸다. 도달점이 움직였다.
        ///   **일시금은 안전하고 배율은 위험하다.** 이 차이를 잊지 말 것.
        /// </summary>
        public void AddCoin(BigNumber amount)
        {
            if (amount.IsZero) return;
            Coin += amount;
        }

        public bool SpendCoin(BigNumber amount)
        {
            if (amount <= BigNumber.Zero) return false;
            if (Coin < amount) return false;
            Coin -= amount;
            return true;
        }

        public void EndRun()
        {
            if (!IsRunning) return;
            IsRunning = false;
            OnRunEnded?.Invoke(DeepestWave);
        }

        // ─────────────────────────────────────────
        // UI용 진행률

        /// <summary>
        /// 현재 웨이브 체력 잔량 0~1.
        ///
        /// ★ UI 전용이 아니다. 조건부(Conditional) 부적 — 어둑시니 — 이 값을 보고
        ///   매 틱 배수를 다시 계산한다. 의미를 바꾸면 밸런스가 같이 움직인다.
        ///   GameController가 Talismans.DamageMultiplierAt(Battle.WaveHpRatio)로 넘긴다.
        /// </summary>
        public float WaveHpRatio
        {
            get
            {
                if (WaveHpTotal.IsZero) return 0f;
                double ratio = (WaveHpRemaining / WaveHpTotal).ToDouble();
                return (float)Math.Max(0.0, Math.Min(1.0, ratio));
            }
        }

        /// <summary>벽까지 남은 여유 0~1. 1에 가까울수록 위험하다.</summary>
        public float WallPressure
            => (float)Math.Max(0.0, Math.Min(1.0, WaveElapsed / cfg.waveTimeWall));
    }
}

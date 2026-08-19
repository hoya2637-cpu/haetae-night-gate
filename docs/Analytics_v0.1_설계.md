# Analytics v0.1 — 계측 설계

**작성** 2026년 8월 18일 · **선행** P0 완료 (`docs/P0_완료보고.md`)
**원칙** P0의 교훈을 그대로 적용한다 — **"지표가 통과하는가"가 아니라 "그 지표가 나쁠 수 있는가"**

---

## 0. 먼저 정할 것 — 무엇을 성공으로 볼 것인가

이벤트를 설계하기 전에 판정 기준을 먼저 적습니다.
P0에서 `slack`과 `ValidateOfflineIntegrity`가 항진명제였던 이유는
**"무엇이 나쁜 상태인가"를 정의하지 않고 지표를 먼저 만들었기 때문**입니다.

### 반증 가능성 검사

모든 지표에 대해 묻습니다 — **이 지표가 나쁘게 나올 수 있는가?**
구조적으로 불가능하면 그 지표는 버립니다.

| 지표 후보 | 나쁠 수 있는가 | 판정 |
|---|---|---|
| 런 도달 웨이브 > 시작 웨이브 | **불가능** (러너가 벽까지만 감) | ✗ 버림 |
| 오프라인 시작 웨이브 < 직전 도달 | **불가능** (비율 < 1) | ✗ 버림 |
| 세션당 런 수 | 가능 | ✓ |
| 승천 후 기록 회복 일수 | 가능 | ✓ |
| 승천 직후 광고 시청률 | 가능 | ✓ |
| D1/D7/D30 | 가능 | ✓ |

---

## 1. 1차 KPI — 목표와 경보선

| 지표 | 목표 | 경보선 | 출처 |
|---|---|---|---|
| D1 리텐션 | 35% | **< 30%** | 마스터문서 2.4 |
| D7 리텐션 | 15% | < 12% | |
| D30 리텐션 | 8% | < 5% | |
| 일 세션 수 | 6 | < 4 | |
| 일 플레이타임 | 15분 | < 10분 | |
| ARPDAU | $0.3708 | < $0.25 | 마스터문서 6.1 |
| IAP : 광고 비중 | 61 : 39 | 광고 > 55% | |
| 90일차 티어 | 6 | **≥ 7** (조기 소진) 또는 ≤ 4 (정체) | P0 실측 |

### P0에서 도출된 리스크 지표 (신규)

| 지표 | 기준선 (P0 실측) | 경보선 |
|---|---|---|
| **승천 후 기록 회복 런 수** | 1 / 3 / 7 / 20 / 40 (승천 1~5회차) | 같은 승천 회차에서 **1.5배 초과** |
| **승천 직후 광고 시청률** | 평시와 동일해야 함 | 평시 대비 **-20%** |
| **승천 직후 세션 이탈률** | 평시와 동일해야 함 | 평시 대비 **+20%** |
| **상위 1% 코호트 티어 속도** | 평균의 몇 배인가 | 3배 초과 시 소진 시점 재계산 |

승천 직후 지표를 따로 보는 이유 — P0에서 확인한 유일한 역전 지점입니다.
광고 시청자의 승천 직후 런이 **44초**로 미시청(1.36분)보다 짧습니다.

---

## 2. 이벤트 스키마 12종

### 공통 컨텍스트 (모든 이벤트에 부착)

```
ts              전송 시각 (UTC epoch ms)
session_id      세션 UUID
user_day        설치 후 경과 일수 (D0 = 설치일)
tier            현재 티어
run_index       누적 런 수
best_wave       현재 최고 웨이브
cores_log10     코어 (log10, BigNumber 대비)
gems            구슬 잔액
ads_removed     광고 제거 IAP 보유 여부
```

> **BigNumber는 반드시 `log10`으로 보냅니다.** 코인은 10^15를 쉽게 넘어
> 대부분의 애널리틱스 SDK가 정수/실수 필드로 못 받습니다.
> 그래프에서도 로그 축이 훨씬 읽기 쉽습니다.

### 이벤트 목록

| # | 이벤트 | 고유 필드 | 빈도(추정) |
|---|---|---|---|
| 1 | `session_start` | `away_hours`, `is_first_launch` | 6/일 |
| 2 | `session_end` | `duration_sec`, `runs_in_session` | 6/일 |
| 3 | `run_start` | `start_wave`, `from_offline`(bool) | 6/일 |
| 4 | `run_end` | `reached_wave`, `duration_sec`, `walled`(bool), `headroom`, `coin_log10`, **`upgrades_by_track[5]`** | 6/일 |
| 5 | `rebirth` | `reached_wave`, `cores_gained`, `runs_today`, `decay_factor` | 6/일 |
| 6 | `ascend` | `tier_before/after`, `best_wave_before`, `cores_before/after`, `atk_mul_before/after` | ~0.06/일 |
| 7 | `record_wave` | `new_best`, `prev_best`, `runs_since_ascend` | 감소 추세 |
| 8 | `offline_claim` | `away_hours`, `credited_hours`, `ratio`, `start_wave`, `prev_wave`, `watched_ad`, `gems_gained` | 6/일 |
| 9 | `ad_request` | `reward_type`, `result`(rewarded/failed/dismissed), `fail_reason`, `runs_since_ascend` | ~7/일 |
| 10 | `iap_purchase` | `sku`, `price_usd`, `verified`(bool) | 드묾 |
| 11 | `talisman_change` | `action`(equip/remove), `talisman_id`, `slot`, `loadout_hash` | 드묾 |
| 12 | `wall_hit` | `wave`, `base_dps_log10`, `required_dps_log10`, `talisman_active` | 6/일 |

**하루 유저당 약 50건.** DAU 5만이면 250만 건/일 — 무료 티어 상당수가 감당합니다.

### ⚠ `upgrade_buy`는 개별 이벤트로 만들지 않습니다

P0 실측에서 **1회차에만 총 63레벨**을 구매합니다. 후반에는 더 많습니다.
개별 이벤트로 보내면 유저당 하루 400건 이상이 되어 다른 이벤트를 압도합니다.

→ `run_end`의 `upgrades_by_track[5]` 배열로 **런 단위 집계**합니다.
트랙별 구매 분포를 알면 충분하고, 개별 구매 시각은 분석 가치가 없습니다.

---

## 3. 파생 지표 — 원시 이벤트에서 무엇을 계산하는가

원시 이벤트를 많이 모으는 것보다 **파생 지표를 계산할 수 있는 구조**가 중요합니다.

### A. 승천 후 기록 회복 (최우선)

```
ascend(tier_before=3, best_wave_before=173, ...)
      ↓  이후 record_wave 이벤트를 기다린다
record_wave(new_best > 173, runs_since_ascend = N)
      ↓
record_recovery_runs = N
record_recovery_days = N / (해당 유저의 일평균 런 수)
```

**티어별로 나눠 봅니다.** P0 기준선은 승천 1~5회차에서 1/3/7/20/40런입니다.
같은 승천 회차에서 1.5배를 넘으면 경보입니다.

> 이 지표가 P0에서 발견된 것 중 유저 경험에 가장 직접적입니다.
> 5번째 승천 후 40런 = 일반 유저 약 13일간 자기 기록을 못 깹니다.

### B. 광고의 역효과

```
ad_request(result=rewarded, runs_since_ascend=0)
      ↓
그 다음 run_end.duration_sec        ← 44초인가?
그 다음 ad_request 발생 여부         ← 광고를 다시 보는가?
그 다음 session_start 발생 여부      ← 돌아오는가?
```

**핵심 질문** — *승천 직후 44초짜리 런을 겪은 유저가 다음 광고를 덜 보는가?*
`runs_since_ascend` 를 `ad_request`와 `run_end` 양쪽에 넣는 이유입니다.

### C. 조기 소진 예측 (마스터문서 9.4)

```
상위 1% 코호트의 tier 도달 일수 ÷ 중앙값 코호트의 tier 도달 일수 = 배수 R
90일 커브에 R을 곱하면 실제 소진 시점이 나온다
```

### D. 세션 품질

```
runs_in_session       목표 1~2 (접속당 1런 설계)
duration_sec          목표 5~10분
offline 기여 비율     offline_claim.ratio 평균
```

---

## 4. 코호트 — 처음부터 데이터 구조에 넣습니다

P0에서 **플레이 빈도가 다른 코호트를 단순 비교하면 잘못된 결론이 난다**는 것을
직접 겪었습니다(헤비 유저가 런 인덱스 기준으로 뒤처져 보였던 건).

### 필수 코호트 축

| 축 | 구간 |
|---|---|
| **플레이 빈도** | 라이트(≤2런/일) / 일반(3~5) / 헤비(6+) |
| **광고 시청** | 시청자(≥1회/일) / 비시청자 |
| **결제** | 무과금 / 소액 / 고래 |
| **설치 코호트** | 설치 주차별 |
| **국가** | 소프트런치 대상 분리 |

### 비교할 때의 규칙

- **코호트 간 비교는 날짜 축** — 같은 90일에 라이트는 90런, 헤비는 540런
- **설계곡선 대조는 런 축** — `TargetWave(k)`가 런 인덱스 기준
- 두 축을 섞으면 P0에서 겪은 오판이 재현됩니다

---

## 5. 구현 방향

### 인터페이스 분리 (광고 SDK와 같은 패턴)

```
IAnalyticsSink
  ├─ EditorLogSink       에디터 콘솔 출력 (개발용)
  ├─ FileSink            로컬 JSONL (소프트런치 디버깅)
  └─ 실제 SDK Sink       Firebase / GameAnalytics / AppLovin 등 (미정)
```

`RewardedAdService`가 `IRewardedAdProvider`로 SDK를 격리한 것과 같은 구조입니다.
SDK를 나중에 바꿔도 게임 코드는 안 건드립니다.

### 기존 이벤트 훅 재사용

이미 구독 가능한 것들이 있어 계측 계층이 게임 로직을 침범할 필요가 없습니다.

```
BattleRunner.OnRunEnded / OnWaveCleared
GameController.OnRebirth / OnAscend / OnOfflineRewardReady
RewardedAdService.OnAdCompleted / OnAdFailed
TalismanSystem.OnSummoned
```

→ `AnalyticsRecorder` (MonoBehaviour) 하나가 이들을 구독해 이벤트를 만듭니다.
**게임 코드에 계측 호출을 흩뿌리지 않습니다.**

### 버퍼링과 전송

- 이벤트는 메모리 큐 → **30초 또는 20건마다 배치 전송**
- 앱 종료·백그라운드 전환 시 즉시 플러시 (`OnApplicationPause`)
- 전송 실패분은 로컬 파일에 보관 후 재시도
- **오프라인 게임이므로 유실 방지가 중요합니다** — 며칠 비행기 모드로 플레이할 수 있습니다

### 저사양·저발열 제약

`CLAUDE.md` 절대 규칙 7과 충돌하지 않아야 합니다.

- 이벤트 객체는 **풀에서 재사용** (`BattleFeedback`의 투사체 풀과 같은 이유)
- JSON 직렬화는 배치 전송 시점에 한 번만
- 매 프레임 계측 금지 — 모든 이벤트가 **상태 변화 시점**에만 발생

---

## 6. 개인정보·정책

| 항목 | 조치 |
|---|---|
| iOS ATT | 광고 ID 사용 시 동의 필요. 계측 자체는 익명 ID로 가능 |
| 유저 식별 | 기기 고유 ID 대신 **앱 설치 시 생성한 UUID**. 재설치 시 리셋됨 |
| 개인정보 | 이름·이메일·위치 수집 안 함. 국가는 스토어 제공값만 |
| GDPR / 한국 | 소프트런치(필리핀·인니) 단계에서는 범위 밖이나, 글로벌 전에 동의 흐름 필요 |
| 확률형 아이템 | 부적 도입 시 확률 공시 의무 (한국 2024.3~). 계측과 별개로 준비 |

---

## 7. 진행 순서

```
① 이벤트 스키마 확정              ← 지금 여기, 승인 필요
② IAnalyticsSink + AnalyticsRecorder 구현
③ EditorLogSink로 스키마 검증 (Play 모드에서 실제 이벤트 확인)
④ 파생 지표 계산 스크립트 (JSONL → 표)
⑤ 대시보드/KPI 정의
   ↓
⑥ 부적 1군 8종 설계
⑦ C(8,5)=56 조합 검증
⑧ 실제 계측 기준으로 재조정
⑨ 나머지 12종은 라이브옵스용 보류
```

**②③은 `GameController.Update()` 경로를 처음 실행시키는 작업이기도 합니다.**
현재 113개 테스트는 전부 EditMode라 MonoBehaviour 접착 계층이 검증 범위 밖입니다.
계측 이벤트가 실제로 나오는지 보려면 Play를 눌러야 하고, 그게 곧 첫 통합 테스트가 됩니다.

---

## 8. 승인이 필요한 것

1. **이벤트 12종 목록** — 빠진 것, 불필요한 것이 있는지
2. **`upgrade_buy`를 `run_end`에 집계**하는 방식 (개별 이벤트 대비 볼륨 1/60)
3. **BigNumber를 log10으로 전송**하는 방식
4. **경보선 수치** — 특히 승천 후 기록 회복 "1.5배 초과"가 적절한지
   (P0 기준선 1/3/7/20/40런이 실측 1회분이라, 소프트런치 초기에 재보정이 필요할 수 있습니다)
5. 애널리틱스 SDK 선택 — 지금 정할지, `IAnalyticsSink`만 만들고 나중에 정할지

**제 의견은 5번을 미루는 것입니다.** 인터페이스와 스키마만 확정하면
소프트런치 직전에 SDK를 골라도 됩니다. 광고 SDK를 `IRewardedAdProvider` 뒤에
격리해둔 것과 같은 이유입니다.

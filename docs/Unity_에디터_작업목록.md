# Unity 에디터 작업 체크리스트 — Haetae: Night Gate

> Claude Code는 코드를 쓰지만 에디터 GUI는 조작하지 못합니다.
> 아래는 사장님이 직접 하셔야 하는 작업입니다.

---

## 1. 스크립트 배치

```
Assets/
├── Scripts/
│   ├── Runtime/
│   │   ├── Core/BigNumber.cs
│   │   ├── Data/EconomyConfig.cs
│   │   ├── Economy/EconomyCore.cs
│   │   ├── Economy/BattleRunner.cs
│   │   ├── Economy/UpgradeTracks.cs
│   │   ├── Game/GameController.cs
│   │   └── Save/GameState.cs, SaveSystem.cs, SaveMigration.cs
│   └── Tests/
│       ├── EconomyTests.cs
│       └── EconomySimulationTests.cs
└── Settings/
    └── EconomyConfig.asset        ← 3번에서 생성
```

---

## 2. 테스트 실행 환경 (Assembly Definition)

테스트를 돌리려면 어셈블리 분리가 필요합니다.

**① Runtime 어셈블리**
- `Assets/Scripts/Runtime/` 우클릭 → Create → Assembly Definition
- 이름: `IdleDefense.Runtime`

**② Test 어셈블리**
- `Assets/Scripts/Tests/` 우클릭 → Create → Assembly Definition
- 이름: `IdleDefense.Tests`
- Inspector에서:
  - **Assembly Definition References** → `IdleDefense.Runtime` 추가
  - **Platforms** → Editor만 체크
  - **Override References** 체크 → `nunit.framework.dll` 추가
  - **Assembly References** → `UnityEngine.TestRunner`, `UnityEditor.TestRunner`

**③ 테스트 실행**
- Window → General → Test Runner → EditMode → Run All
- **46개 테스트가 전부 초록이어야 합니다**

---

## 3. EconomyConfig 에셋 생성

- `Assets/Settings/` 폴더 생성
- 우클릭 → Create → **IdleDefense → Economy Config**
- 파일명: `EconomyConfig`
- **값은 건드리지 마세요.** 기본값이 검증된 상태입니다.

> 인스펙터에서 값을 바꾸면 `OnValidate()`가 즉시 경고를 띄웁니다.
> 경고가 뜨면 되돌리세요.

---

## 4. 씬 구성

**① 새 씬 생성** — `Assets/Scenes/Main.unity`

**② 빈 GameObject 생성** — 이름 `GameController`

**③ 광고 서비스 GameObject 생성**
- 빈 GameObject → 이름 `AdService`
- Add Component → `Rewarded Ad Service`
- Token Lifetime Seconds: 60 (기본값)

**④ GameController 컴포넌트 추가**
- Add Component → `Game Controller`
- **Config 슬롯에 3번에서 만든 `EconomyConfig` 에셋을 드래그**
- Auto Save Interval: 30 (기본값)
- **Ad Service 슬롯에 `AdService` 오브젝트를 드래그**
- Auto Upgrade / Auto Rebirth: **체크 해제** (구슬로 해금하는 기능)

**⑤ 확인**
- Play 버튼 → Console에 오류가 없어야 함
- Inspector에서 GameController를 선택한 채로 두면 상태 변화가 보입니다

---

## 5. 임시 UI (1단계는 숫자만)

> 1단계에서는 아트를 넣지 않습니다. 숫자가 맞는지만 봅니다.

Canvas 하나에 Text 5개면 충분합니다.

| 표시 항목 | 소스 |
|---|---|
| 웨이브 | `Battle.CurrentWave` |
| 엽전 | `Battle.Coin.ToString()` |
| 체력바 | `Battle.WaveHpRatio` (Slider) |
| 벽 압력 | `Battle.WallPressure` (Slider) |
| 티어 · 도깨비불 | `State.tier`, `State.cores` |

버튼 5개 — 오방색 트랙 각각. `TryUpgrade(Track.Blue)` 등을 호출합니다.

**색상은 `UpgradeTracks.TrackColor()`를 쓰세요.** 아트기준문서와 일치시켜 뒀습니다.

---

## 6. 모바일 설정

**File → Build Settings → Player Settings**

| 항목 | 값 | 이유 |
|---|---|---|
| Color Space | Gamma | 저사양 기기 성능 |
| Auto Graphics API | 체크 해제 → OpenGLES3 우선 | 호환성 |
| Multithreaded Rendering | 체크 | |
| Static/Dynamic Batching | 둘 다 체크 | 드로우콜 감소 |
| Target Architectures | ARM64만 | APK 크기 |
| Scripting Backend | IL2CPP | |
| Managed Stripping Level | Medium | |

**Quality Settings**
- Anti Aliasing: Disabled
- Shadows: Disable Shadows
- VSync: Don't Sync (코드에서 `targetFrameRate = 60` 지정)

> 저발열·저배터리가 이 게임의 차별화입니다.
> 그림자와 안티에일리어싱은 2D 방치형에 불필요하고 발열만 만듭니다.

---

## 7. 첫 실행 검증

Play 후 다음을 확인하세요.

- [ ] 웨이브 번호가 올라간다
- [ ] 엽전이 `1.23K` 형식으로 표시된다 (`1234.567` 아님)
- [ ] 업그레이드 버튼을 누르면 엽전이 줄고 진행이 빨라진다
- [ ] 45초 넘게 못 깨는 웨이브가 오면 런이 멈춘다 (= 벽)
- [ ] Play 정지 후 다시 Play → **진행 상황이 유지된다**
- [ ] Console에 경고·오류가 없다

**실기 테스트 시 반드시 확인할 광고 이벤트 조합**

에디터에서는 재현되지 않는 것들입니다. Android/iOS 각각 확인하세요.

- [ ] 로드 실패 → 지수 백오프 재시도가 도는가
- [ ] 표시 실패 → 보상 없이 실패 처리되는가
- [ ] 정상 시청 → Reward → Hidden 순서로 보상 1회
- [ ] 광고를 중간에 닫음 → 보상 없음
- [ ] Hidden만 발생 (Reward 없음) → 보상 없음
- [ ] Reward 콜백 중복 → 보상 1회
- [ ] 4가지 보상(오프라인 2배 / 도깨비 / 배속 / 부활) 각각 1회씩

**세이브 위치 확인** (문제 생길 때)
- Windows: `%userprofile%\AppData\LocalLow\<회사명>\<제품명>\save.json`
- Mac: `~/Library/Application Support/<회사명>/<제품명>/save.json`

---

## 8. 알아두실 것

**첫 런은 11분입니다.** 웨이브 1부터 코인 0으로 시작하니까요.
2회차부터는 오프라인 보상이 대부분을 건너뛰어 6~7분이 됩니다.
첫 런이 긴 것은 튜토리얼 성격이라 의도된 것입니다.

**적은 개별 오브젝트가 아닙니다.**
웨이브 전체를 하나의 체력 풀로 다룹니다. 화면에는 대표 몇 마리만
연출로 띄우면 됩니다. 100마리를 매 프레임 돌리면 발열이 납니다.

**전투 공식을 `GameController`에 직접 쓰지 마세요.**
전부 `EconomyCore`를 거쳐야 테스트가 계속 유효합니다.

---

## 8-2. 타격감 연출 세팅

디펜스 게임은 타격감이 핵심이므로 별도 계층으로 분리했습니다.

**구조**

```
BattleRunner (계산)          BattleFeedback (연출)
  HP -= DPS x dt      →        투사체 · 데미지 숫자
  0.4초마다 발사 이벤트  →        화면 흔들림
```

**연출은 계산을 절대 바꾸지 않습니다.** 발사 이벤트를 구독하든 안 하든
도달 웨이브가 동일한지 테스트로 고정해뒀습니다.

**에디터 작업**

1. 빈 GameObject `BattleFeedback` 생성 → `Battle Feedback` 컴포넌트 추가
2. Controller 슬롯에 `GameController` 연결
3. Tower Point / Enemy Point — 빈 Transform 2개를 만들어 타워와 적 위치에 배치
4. Projectile Prefab — 작은 스프라이트 하나 (원형 또는 화살)
5. Damage Text Prefab — `TextMeshPro` 하나 (3D, 월드 스페이스)
6. Pool Size 12 (기본값 유지)

**성능 주의**

- 투사체와 텍스트는 **풀에서 재사용**합니다. 매번 Instantiate하면
  방치형처럼 몇 시간 켜두는 게임에서 GC 스파이크가 발열로 직결됩니다
- 화면 흔들림은 **크리티컬과 웨이브 클리어 때만**. 매번 흔들면
  5분도 안 돼 피로해집니다
- 발사 간격 0.4초는 고정입니다. DPS가 아무리 커져도 발사 횟수는 그대로고
  **한 발의 표시 데미지만 커집니다.** 이래야 후반에도 발열이 안 늘어납니다

**크리티컬 확률**

흑(黑) 트랙 레벨에 비례하되 **45%가 상한**입니다.
100%가 되면 크리티컬이 평범해져서 타격감이 오히려 죽습니다.

---

## 9. 세이브 보안 수준 — 현재 범위 정의

혼동을 막기 위해 명확히 해둡니다. **이것은 안티치트가 아닙니다.**

| 단계 | 내용 | 현재 상태 |
|---|---|---|
| Level 1 | 캐주얼 파일 조작 방지 (체크섬) | 완료 |
| Level 2 | 경제 붕괴값 차단 (범위 검증) | 완료 |
| Level 3 | 서버 권위 검증 | **범위 밖** |

**Level 1·2로 막는 것**
- 파일 손상·부분 기록
- 메모장으로 숫자 바꾸기
- 게임을 수학적으로 붕괴시키는 값 (tier 99 → 배수 2.5^98)

**막지 못하는 것**
- 루팅 기기의 메모리 조작
- 체크섬을 재계산한 정교한 조작 (알고리즘이 클라이언트에 있음)
- 각 값은 정상 범위인데 조합이 불가능한 세이브
  (예: tier 10인데 bestWave 1)

**`adsRemoved`는 별개 문제입니다.**
true/false 둘 다 정상값이라 범위 검증이 원리적으로 불가능합니다.
IAP 연동 시 **스토어 영수증 검증**을 반드시 함께 구현하고,
세이브 값은 캐시로만 취급하세요. 권한의 원본은 스토어 검증 결과입니다.

---

## 10. 광고 보상 — 출시 전 반드시 교체

현재 `EditorFakeAdProvider`가 붙어 있습니다. **무조건 보상을 주는 더미**입니다.
에디터에서 흐름을 확인하는 용도이며, 이대로 빌드하면 광고 없이 보상이 나갑니다.

**AppLovin MAX 연결 순서**

`AppLovinAdProvider.cs`가 이미 구현되어 있습니다. 아래만 하시면 됩니다.

**① SDK 설치**
- AppLovin 대시보드에서 계정 생성 → SDK Key 발급
- Unity Package Manager로 AppLovin MAX Unity Plugin 설치
- AppLovin → Integration Manager에서 미디에이션 네트워크 선택

**② 심볼 정의 (중요)**
- Player Settings → Other Settings → Scripting Define Symbols
- **`APPLOVIN_MAX`** 추가
- 이 심볼이 없으면 `AppLovinAdProvider`가 항상 실패를 반환합니다 (안전 장치)

**③ 광고 유닛 생성**
- 대시보드에서 리워드 광고 유닛 4개 생성 (Android/iOS 각각)
- 오프라인 2배 / 도깨비 / 배속 / 부활

**④ SDK 초기화** — 앱 시작 시 가장 먼저

```csharp
MaxSdk.SetSdkKey("«your-sdk-key»");
MaxSdk.InitializeSdk();
```

**⑤ Provider 교체** — `GameController.Awake()`

```csharp
var unitIds = new Dictionary<RewardType, string> {
    { RewardType.OfflineDouble,  "«unit-id»" },
    { RewardType.DokkaebiSummon, "«unit-id»" },
    { RewardType.SpeedBoost,     "«unit-id»" },
    { RewardType.Revive,         "«unit-id»" },
};
adService.Initialize(new AppLovinAdProvider(unitIds));
```

> **iOS 출시 시 ATT 및 개인정보 동의 설정을 별도로 검토하세요.**
> 광고 동작의 필수 조건은 아니지만, 추적 동의 여부가 광고 성과에 영향을 줍니다.
> Apple과 AppLovin의 최신 정책을 확인해 진행하세요.

**절대 하지 말 것**
- `onRewarded`를 광고 표시 시점에 호출 (= 안 봐도 보상)
- `GameController`에 SDK 코드 직접 삽입 (SDK 교체가 불가능해짐)
- 토큰 없이 보상을 주는 메서드 추가

**UI에서 반드시 지킬 것 — 광고 버튼 잠금**

리워드 광고는 한 번에 하나만 요청할 수 있습니다.
`OnRequestLockChanged`를 구독해 요청 중에는 모든 광고 버튼을 비활성화하세요.

```csharp
adService.OnRequestLockChanged += locked => {
    reviveButton.interactable   = !locked;
    dokkaebiButton.interactable = !locked;
    speedButton.interactable    = !locked;
};
```

잠그지 않으면 유저가 광고를 다 보고도 보상을 못 받는 경우가 생깁니다.

**보상 요청 흐름 (UI에서)**

```csharp
adService.RequestReward(
    RewardType.Revive,
    onVerified: token => gameController.GrantRevive(token),
    onFailed:   reason => ShowToast(reason));
```

버튼 클릭과 보상 지급 사이에 **반드시 광고 완료 콜백이 끼어야** 합니다.

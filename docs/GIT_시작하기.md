# Git 시작하기

## 1. Unity 프로젝트 설정 (커밋 전 필수)

**Edit → Project Settings → Editor**

| 항목 | 값 | 이유 |
|---|---|---|
| Version Control → Mode | **Visible Meta Files** | .meta 파일이 보여야 참조가 유지됨 |
| Asset Serialization → Mode | **Force Text** | 씬 파일이 바이너리면 충돌 시 손도 못 댐 |

이거 안 하고 커밋하면 나중에 되돌리기 어렵습니다.

---

## 2. 저장소 만들기

```bash
cd <프로젝트 폴더>
git init
git add .
git commit -m "feat: 경제 코어, 전투 루프, 광고 보안, 세이브 방어 구현"
git tag v0.1-economy-core-gate
```

**GitHub에서 저장소 생성 시 Private으로.**
나중에 SDK Key나 광고 유닛 ID가 들어갈 수 있습니다.

```bash
git remote add origin <URL>
git branch -M main
git push -u origin main --tags
```

---

## 3. 확인

```bash
git status      # working tree clean 이어야 함
git log -1      # 방금 커밋이 HEAD
```

`Library/`가 목록에 없는지 꼭 보세요. 있으면 `.gitignore`가 적용 안 된 겁니다.

---

## 4. Git LFS (아트 에셋 들어올 때)

지금은 배경 6장뿐이라 급하지 않습니다.
캐릭터 스프라이트가 늘어나면 그때 설정하세요.

```bash
git lfs install
git lfs track "*.png"
git lfs track "*.psd"
git add .gitattributes
```

---

## 5. 브랜치 전략

혼자 개발이니 **`main` 하나로 충분합니다.**
의미 있는 단계마다 커밋하고 태그를 찍으세요.

```
v0.1-economy-core-gate    ← 현재
v0.2-battle-loop
v0.3-applovin
v0.4-softlaunch
```

---

## 6. 절대 커밋하지 말 것

`.gitignore`에 이미 들어 있지만 한 번 더 확인하세요.

- `Library/` — 수 GB
- AppLovin **SDK Key**
- 광고 유닛 ID
- `*.keystore`, `*.p12`, `*.mobileprovision`

키는 `Assets/Settings/AdKeys.asset` 같은 별도 파일로 빼고
`.gitignore`에 등록해두면 안전합니다.

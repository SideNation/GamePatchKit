# Unity QuickStart 샘플

Unity client가 받아갈 release를 만들고 로컬 HTTP로 서빙한다. Unity 쪽 절차는
[Unity 10분 QuickStart](../../docs/guide/unity-quickstart.md)에 있다.

```bash
./serve.sh            # http://127.0.0.1:8080/
PORT=9000 ./serve.sh  # 다른 포트
```

```text
== Unity Inspector에 넣을 값 ==
  Base Url      : http://127.0.0.1:8080/
  Package Id    : unity-sample-data
  Data Version  : v1-f82ab476...
  Manifest Hash : 7e41ab06...
```

- 게임 데이터는 [quickstart 샘플](../quickstart/game-data)의 것을 그대로 재사용한다.
- [`gamepatchkit.yml`](gamepatchkit.yml)은 quickstart 샘플과 **`compression: none`
  하나만 다르다.** Unity 지원 범위가 아직 그것뿐이다.
- 생성물은 전부 `.work/` 아래에만 만들고 실행할 때마다 지운다.
- loopback에만 bind한다. 다른 기기에서 붙이려면 Player Settings의 **Allow downloads
  over HTTP**를 먼저 확인한다.

client 스크립트는
[`unity/GamePatchKit.Unity/Assets/PatchQuickStart.cs`](../../unity/GamePatchKit.Unity/Assets/PatchQuickStart.cs)에
있다. 저장소의 Unity 검증 프로젝트에 들어 있어 `scripts/test.sh`가 돌 때 함께
컴파일된다.

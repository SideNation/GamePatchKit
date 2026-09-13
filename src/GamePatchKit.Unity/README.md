# GamePatchKit Unity 패키지

`gpk upload`가 공개 Storage에 게시한 패치 데이터 세대를 Unity 6 클라이언트에 내려받아 원본 파일 트리로 복원한다.

```csharp
var client = new PatchClient(
    "https://<project-ref>.supabase.co/storage/v1/object/public/<bucket>/",
    Path.Combine(Application.persistentDataPath, "GamePatchKit"));
PatchSyncResult result = await client.SyncAsync(releaseVersion, cancellationToken);
// 게임 데이터는 client.DataPath 아래의 <그룹 id>/<엔트리 path>에서 읽는다.
```

설치 방법, 결과·실패 처리, 폴더 배치는 [docs/unity/patch-client.md](https://github.com/SideNation/GamePatchKit/blob/main/docs/unity/patch-client.md)를 참고한다.

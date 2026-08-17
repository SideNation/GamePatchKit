namespace GamePatchKit.Cli.Tests;

// upload와 sync 설정 테스트는 프로세스 전역 환경 변수를 바꾸고 GPK_SUPABASE_BUCKET을 공유한다. 같은
// collection에 넣어 서로 병렬로 돌지 않게 한다.
public static class SupabaseSettingsCollection
{
    public const string NAME = "SupabaseSettings";
}

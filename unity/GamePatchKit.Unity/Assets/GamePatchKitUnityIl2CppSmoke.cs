using GamePatchKit.Runtime;
using UnityEngine;
using UnityEngine.Scripting;

namespace GamePatchKit.Unity
{
    public static class GamePatchKitUnityIl2CppSmoke
    {
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void PreserveRuntimeTypes()
        {
            _ = typeof(PackageRuntime);
            _ = typeof(UnityRuntimeStorage);
            _ = typeof(UnityWebRequestArtifactTransport);
        }
    }
}


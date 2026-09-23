using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectT.DataTable.Editor
{
    /// <summary>
    /// 릴리즈 빌드 안전장치.
    ///
    /// 활성 Addressables 프로필의 Remote.LoadPath가 로컬 테스트 경로(file://, localhost)이거나
    /// 아직 채우지 않은 예시 주소(example.com)이면 플레이어 빌드를 중단합니다.
    /// 이 상태로 출시하면 유저 기기에서 테이블을 불러오지 못하기 때문입니다.
    ///
    /// Development Build는 로컬 테스트용으로 허용하고 경고만 남깁니다.
    /// 릴리즈 빌드 전에: Addressables Groups 창 > Profile을 "Release"로 바꾸고 Remote.LoadPath에 실제 CDN 주소를 넣으세요.
    /// </summary>
    public class AddressablesBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var profileId = settings.activeProfileId;
            var profileName = settings.profileSettings.GetProfileName(profileId);
            var loadPath = settings.profileSettings.GetValueByName(profileId, "Remote.LoadPath") ?? string.Empty;

            if (!IsPlaceholderOrLocal(loadPath)) return;

            var message =
                $"[AddressablesBuildGuard] Addressables 프로필 '{profileName}'의 Remote.LoadPath가 '{loadPath}'입니다. " +
                "로컬 테스트 경로이거나 아직 채우지 않은 예시 주소라, 이대로 출시하면 유저 기기에서 테이블을 불러오지 못합니다. " +
                "Addressables Groups 창에서 Profile을 'Release'로 바꾸고 Remote.LoadPath에 실제 CDN 주소를 넣은 뒤 다시 빌드하세요.";

            bool isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            if (isDevelopmentBuild)
            {
                Debug.LogWarning(message + " (Development Build라서 계속 진행합니다)");
                return;
            }

            throw new BuildFailedException(message);
        }

        public static bool IsPlaceholderOrLocal(string loadPath)
        {
            if (string.IsNullOrWhiteSpace(loadPath)) return true;
            var lower = loadPath.ToLowerInvariant();
            return lower.StartsWith("file://")
                   || lower.Contains("localhost")
                   || lower.Contains("127.0.0.1")
                   || lower.Contains("example.com");
        }
    }
}

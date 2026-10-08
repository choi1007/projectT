using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
namespace ProjectT.DataTable.Editor
{
    [InitializeOnLoad]
    public static class TableAddressableRegistration
    {
        private const string AddressableGroupName = "Tables";
        private const string AddressableLabel = "DataTable";
        static TableAddressableRegistration() { DataTableCsvImporter.Imported += MarkAddressable; }
        private static void MarkAddressable(string assetPath, string tableName)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                // 프로젝트에 Addressables 설정이 아직 없으면(최초 1회) 기본 설정을 만듭니다.
                settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            }

            var group = settings.FindGroup(AddressableGroupName);
            if (group == null)
            {
                group = settings.CreateGroup(AddressableGroupName, false, false, true, null,
                    typeof(BundledAssetGroupSchema),
                    typeof(ContentUpdateGroupSchema));

                // 패치 가능한 원격 그룹 + 테이블별 개별 번들 (바뀐 테이블만 다시 받도록)
                var bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
                bundleSchema.BuildPath.SetVariableByName(settings, "Remote.BuildPath");
                bundleSchema.LoadPath.SetVariableByName(settings, "Remote.LoadPath");
                bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

                // StaticContent(Prevent Updates)가 켜져 있으면 패치 대상에서 빠지므로 반드시 false
                group.GetSchema<ContentUpdateGroupSchema>().StaticContent = false;
                EditorUtility.SetDirty(group);
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = tableName;

            if (!settings.GetLabels().Contains(AddressableLabel))
            {
                settings.AddLabel(AddressableLabel);
            }
            entry.SetLabel(AddressableLabel, true);

            EditorUtility.SetDirty(settings);
        }

    }
}

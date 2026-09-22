using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace ProjectT.DataTable.Editor
{
    /// <summary>
    /// CSV -> DataTableAsset 임포터.
    ///
    /// CSV 규칙:
    ///   1행: 컬럼 이름 (예: Id, Name, Hp, MoveSpeed, IsBoss)
    ///   2행: 컬럼 타입 (int / float / bool / string, 대소문자 무관)
    ///   3행부터: 실제 데이터
    ///
    /// 사용법: Tools > Data Table > Import CSV... 로 파일 하나 선택
    ///         Tools > Data Table > Import All In RawTables 로 Assets/RawTables 안의 모든 csv 일괄 임포트
    /// </summary>
    public static class DataTableCsvImporter
    {
        private const string RawTablesFolder = "Assets/RawTables";
        private const string OutputFolder = "Assets/Data/Tables";
        private const string AddressableGroupName = "Tables";
        private const string AddressableLabel = "DataTable";

        [MenuItem("Tools/Data Table/Import CSV...")]
        public static void ImportSingle()
        {
            var path = EditorUtility.OpenFilePanel("Import Data Table CSV", RawTablesFolder, "csv");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ImportCsvFile(path);
            if (asset != null)
            {
                EditorUtility.DisplayDialog("Data Table Import",
                    $"'{asset.tableName}' 테이블을 임포트했습니다. ({asset.RowCount}행, {asset.columns.Count}컬럼)", "OK");
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        [MenuItem("Tools/Data Table/Import All In RawTables")]
        public static void ImportAll()
        {
            if (!Directory.Exists(RawTablesFolder))
            {
                Debug.LogWarning($"[DataTableCsvImporter] 폴더가 없습니다: {RawTablesFolder}");
                return;
            }

            var files = Directory.GetFiles(RawTablesFolder, "*.csv", SearchOption.AllDirectories);
            var results = new List<DataTableAsset>();
            foreach (var file in files)
            {
                var asset = ImportCsvFile(file);
                if (asset != null) results.Add(asset);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[DataTableCsvImporter] {results.Count}개 테이블 임포트 완료: {string.Join(", ", results.Select(r => r.tableName))}");
        }

        public static DataTableAsset ImportCsvFile(string absoluteOrProjectPath)
        {
            string fullPath = absoluteOrProjectPath;
            if (!Path.IsPathRooted(fullPath))
            {
                fullPath = Path.Combine(Directory.GetCurrentDirectory(), fullPath);
            }

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[DataTableCsvImporter] 파일을 찾을 수 없습니다: {fullPath}");
                return null;
            }

            var text = File.ReadAllText(fullPath, Encoding.UTF8);
            var rows = ParseCsv(text);

            if (rows.Count < 2)
            {
                Debug.LogError($"[DataTableCsvImporter] CSV에 최소 헤더 2줄(이름/타입)이 필요합니다: {fullPath}");
                return null;
            }

            var nameRow = rows[0];
            var typeRow = rows[1];
            var columns = new List<ColumnDef>();
            for (int i = 0; i < nameRow.Length; i++)
            {
                var colName = nameRow[i].Trim();
                if (string.IsNullOrEmpty(colName)) continue;
                var typeStr = i < typeRow.Length ? typeRow[i].Trim() : "string";
                columns.Add(new ColumnDef { name = colName, type = ParseColumnType(typeStr) });
            }

            var tableName = Path.GetFileNameWithoutExtension(fullPath);

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                CreateFoldersRecursive(OutputFolder);
            }

            var assetPath = $"{OutputFolder}/{tableName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<DataTableAsset>(assetPath);
            bool isNew = asset == null;
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<DataTableAsset>();
            }

            asset.tableName = tableName;
            asset.columns = columns;
            asset.rows = new List<DataRow>();

            for (int r = 2; r < rows.Count; r++)
            {
                var raw = rows[r];
                if (raw.Length == 1 && string.IsNullOrWhiteSpace(raw[0])) continue; // 빈 줄 스킵

                var row = new DataRow();
                for (int c = 0; c < columns.Count; c++)
                {
                    row.cells.Add(c < raw.Length ? raw[c] : string.Empty);
                }
                asset.rows.Add(row);
            }

            asset.BuildIndex();

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            MarkAddressable(assetPath, tableName);

            return asset;
        }

        private static ColumnType ParseColumnType(string typeStr)
        {
            switch (typeStr.ToLowerInvariant())
            {
                case "int": return ColumnType.Int;
                case "float": return ColumnType.Float;
                case "bool": return ColumnType.Bool;
                default: return ColumnType.String;
            }
        }

        private static void CreateFoldersRecursive(string folderPath)
        {
            var parts = folderPath.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

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
                    typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema),
                    typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema));
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

        /// <summary>
        /// 간단한 RFC4180 스타일 CSV 파서. 따옴표로 감싼 필드 안의 콤마/줄바꿈을 지원합니다.
        /// </summary>
        private static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var field = new StringBuilder();
            var fields = new List<string>();
            bool inQuotes = false;

            void EndField()
            {
                fields.Add(field.ToString());
                field.Clear();
            }

            void EndRow()
            {
                EndField();
                rows.Add(fields.ToArray());
                fields.Clear();
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        EndField();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        EndRow();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }

            // 마지막 줄 처리 (파일이 개행으로 안 끝나는 경우)
            if (field.Length > 0 || fields.Count > 0)
            {
                EndRow();
            }

            // 완전히 빈 줄(끝의 개행 등) 제거
            rows.RemoveAll(r => r.Length == 0 || (r.Length == 1 && string.IsNullOrWhiteSpace(r[0])));

            return rows;
        }
    }
}

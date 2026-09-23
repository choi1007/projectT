using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace ProjectT.DataTable.Editor
{
    /// <summary>
    /// CSV -> DataTableAsset 임포터.
    ///
    /// CSV 규칙:
    ///   1행: 컬럼 이름 (예: Id, Name, Hp, MoveSpeed, IsBoss) — int 타입 "Id" 컬럼 필수
    ///   2행: 컬럼 타입 (int / float / bool / string, 대소문자 무관)
    ///   3행부터: 실제 데이터
    ///
    /// 임포트 전에 데이터를 검증합니다. 에러가 하나라도 있으면 임포트를 중단하고
    /// 기존 테이블 에셋은 그대로 둡니다 (잘못된 데이터가 빌드/패치에 섞이지 않도록).
    ///   에러: 타입 불일치, 알 수 없는 타입, 중복/빈 컬럼 이름, Id 누락/중복/빈 값,
    ///         헤더보다 많은 칸에 값이 있음, 파일이 UTF-8이 아님
    ///   경고: Id가 아닌 숫자/bool 칸이 비어 있음 (기본값으로 처리)
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
        private const string IdColumn = "Id";
        private const int MaxReportedIssues = 50;

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
            else
            {
                EditorUtility.DisplayDialog("Data Table Import",
                    "데이터에 오류가 있어 임포트하지 않았습니다. 기존 테이블은 그대로입니다.\n콘솔에서 오류 위치를 확인하세요.", "OK");
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
            var succeeded = new List<string>();
            var failed = new List<string>();
            foreach (var file in files)
            {
                var asset = ImportCsvFile(file);
                if (asset != null) succeeded.Add(asset.tableName);
                else failed.Add(Path.GetFileName(file));
            }

            AssetDatabase.SaveAssets();
            if (failed.Count > 0)
            {
                Debug.LogError($"[DataTableCsvImporter] {failed.Count}개 테이블 임포트 실패 (기존 데이터 유지): {string.Join(", ", failed)}");
            }
            Debug.Log($"[DataTableCsvImporter] {succeeded.Count}개 테이블 임포트 완료: {string.Join(", ", succeeded)}");
        }

        /// <summary>CSV 파일 하나를 검증 후 임포트합니다. 검증 실패 시 null을 반환하고 기존 에셋은 건드리지 않습니다.</summary>
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

            var fileName = Path.GetFileName(fullPath);
            var tableName = Path.GetFileNameWithoutExtension(fullPath);
            var text = File.ReadAllText(fullPath, Encoding.UTF8);

            var errors = new List<string>();
            var warnings = new List<string>();
            bool ok = TryBuildTable(text, errors, warnings, out var columns, out var rowCells);

            ReportIssues(fileName, warnings, isError: false);
            if (!ok)
            {
                ReportIssues(fileName, errors, isError: true);
                return null;
            }

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
            asset.idColumn = IdColumn;
            asset.columns = columns;
            asset.rows = rowCells.Select(cells => new DataRow { cells = cells }).ToList();
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

        /// <summary>
        /// CSV 텍스트를 파싱하고 검증합니다. 에셋은 만들지 않으므로 테스트나 CI 검증에도 쓸 수 있습니다.
        /// 에러가 없으면 true.
        /// </summary>
        public static bool TryBuildTable(string csvText, List<string> errors, List<string> warnings,
            out List<ColumnDef> columns, out List<List<string>> rows)
        {
            columns = new List<ColumnDef>();
            rows = new List<List<string>>();

            if (csvText.IndexOf('�') >= 0)
            {
                errors.Add("파일이 UTF-8 인코딩이 아닙니다 (한글이 깨집니다). 엑셀에서 저장할 때 'CSV UTF-8(쉼표로 분리)'를 선택하세요.");
                return false;
            }

            var parsed = ParseCsv(csvText);
            if (parsed.Count < 2)
            {
                errors.Add("헤더 2줄(1행: 컬럼 이름, 2행: 타입)이 필요합니다.");
                return false;
            }

            var (nameLine, nameRow) = parsed[0];
            var (typeLine, typeRow) = parsed[1];

            // 헤더 오른쪽 끝의 빈 칸은 무시
            int colCount = nameRow.Length;
            while (colCount > 0
                   && string.IsNullOrWhiteSpace(nameRow[colCount - 1])
                   && (colCount - 1 >= typeRow.Length || string.IsNullOrWhiteSpace(typeRow[colCount - 1])))
            {
                colCount--;
            }

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            for (int c = 0; c < colCount; c++)
            {
                var name = nameRow[c].Trim();
                var typeStr = c < typeRow.Length ? typeRow[c].Trim() : string.Empty;

                if (string.IsNullOrEmpty(name))
                {
                    errors.Add($"{CellRef(nameLine, c)}: 컬럼 이름이 비어 있습니다.");
                }
                else if (!seenNames.Add(name))
                {
                    errors.Add($"{CellRef(nameLine, c)}: 컬럼 이름 '{name}'이(가) 중복됩니다.");
                }

                if (!TryParseColumnType(typeStr, out var type))
                {
                    errors.Add($"{CellRef(typeLine, c)}: '{name}' 컬럼의 타입 '{typeStr}'을(를) 알 수 없습니다. (int, float, bool, string 중 하나)");
                    type = ColumnType.String;
                }

                columns.Add(new ColumnDef { name = name, type = type });
            }

            int idIndex = columns.FindIndex(col => col.name == IdColumn);
            if (idIndex < 0)
            {
                errors.Add($"'{IdColumn}' 컬럼이 없습니다. 행을 구분할 int 타입 '{IdColumn}' 컬럼이 필요합니다.");
            }
            else if (columns[idIndex].type != ColumnType.Int)
            {
                errors.Add($"{CellRef(typeLine, idIndex)}: '{IdColumn}' 컬럼은 int 타입이어야 합니다.");
            }

            var firstLineById = new Dictionary<int, int>();
            for (int r = 2; r < parsed.Count; r++)
            {
                var (line, raw) = parsed[r];

                // 헤더보다 오른쪽 칸에 값이 있으면 콤마/열 위치가 어긋난 것
                for (int c = columns.Count; c < raw.Length; c++)
                {
                    if (!string.IsNullOrWhiteSpace(raw[c]))
                    {
                        errors.Add($"{CellRef(line, c)}: 헤더에 없는 칸에 값 '{raw[c]}'이(가) 있습니다. 콤마 개수나 열 위치를 확인하세요.");
                        break;
                    }
                }

                var cells = new List<string>(columns.Count);
                for (int c = 0; c < columns.Count; c++)
                {
                    var col = columns[c];
                    var value = c < raw.Length ? raw[c] : string.Empty;
                    if (col.type != ColumnType.String) value = value.Trim();

                    if (col.type != ColumnType.String && value.Length == 0)
                    {
                        if (c == idIndex)
                            errors.Add($"{CellRef(line, c)}: '{IdColumn}' 값이 비어 있습니다.");
                        else
                            warnings.Add($"{CellRef(line, c)}: '{col.name}' 값이 비어 있어 기본값({DefaultValueText(col.type)})으로 처리됩니다.");
                    }
                    else if (!IsValidValue(col.type, value))
                    {
                        errors.Add($"{CellRef(line, c)}: '{col.name}' 값 '{value}'은(는) {TypeName(col.type)} 형식이 아닙니다.");
                    }

                    cells.Add(value);
                }

                if (idIndex >= 0 && columns[idIndex].type == ColumnType.Int &&
                    int.TryParse(cells[idIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    if (firstLineById.TryGetValue(id, out var firstLine))
                        errors.Add($"{line}행: Id {id}이(가) {firstLine}행과 중복됩니다.");
                    else
                        firstLineById[id] = line;
                }

                rows.Add(cells);
            }

            return errors.Count == 0;
        }

        private static bool TryParseColumnType(string typeStr, out ColumnType type)
        {
            switch (typeStr.ToLowerInvariant())
            {
                case "int": type = ColumnType.Int; return true;
                case "float": type = ColumnType.Float; return true;
                case "bool": type = ColumnType.Bool; return true;
                case "string": type = ColumnType.String; return true;
                default: type = ColumnType.String; return false;
            }
        }

        private static bool IsValidValue(ColumnType type, string value)
        {
            switch (type)
            {
                case ColumnType.Int:
                    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                case ColumnType.Float:
                    return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                           && !float.IsNaN(f) && !float.IsInfinity(f);
                case ColumnType.Bool:
                    return bool.TryParse(value, out _) || value == "1" || value == "0";
                default:
                    return true;
            }
        }

        private static string TypeName(ColumnType type)
        {
            switch (type)
            {
                case ColumnType.Int: return "int(정수)";
                case ColumnType.Float: return "float(실수)";
                case ColumnType.Bool: return "bool(TRUE/FALSE/1/0)";
                default: return "string";
            }
        }

        private static string DefaultValueText(ColumnType type) => type == ColumnType.Bool ? "FALSE" : "0";

        /// <summary>엑셀과 같은 방식의 위치 표기: "5행 C열"</summary>
        private static string CellRef(int line, int colIndex) => $"{line}행 {ColumnLetter(colIndex)}열";

        private static string ColumnLetter(int index)
        {
            var sb = new StringBuilder();
            int n = index + 1;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }

        private static void ReportIssues(string fileName, List<string> issues, bool isError)
        {
            if (issues.Count == 0) return;

            var sb = new StringBuilder();
            if (isError)
                sb.Append($"[DataTableCsvImporter] {fileName}: 에러 {issues.Count}건 — 임포트 중단, 기존 테이블 데이터는 그대로 유지됩니다.");
            else
                sb.Append($"[DataTableCsvImporter] {fileName}: 경고 {issues.Count}건");

            foreach (var issue in issues.Take(MaxReportedIssues))
            {
                sb.Append("\n  - ").Append(issue);
            }
            if (issues.Count > MaxReportedIssues)
            {
                sb.Append($"\n  ... 외 {issues.Count - MaxReportedIssues}건");
            }

            if (isError) Debug.LogError(sb.ToString());
            else Debug.LogWarning(sb.ToString());
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

        /// <summary>
        /// RFC4180 스타일 CSV 파서. 따옴표로 감싼 필드 안의 콤마/줄바꿈을 지원하고,
        /// 에러 메시지에 쓰기 위해 각 행이 시작된 줄 번호(1부터)를 함께 돌려줍니다.
        /// 완전히 빈 행(",,,," 포함)은 건너뜁니다.
        /// </summary>
        private static List<(int line, string[] cells)> ParseCsv(string text)
        {
            var rows = new List<(int line, string[] cells)>();
            var field = new StringBuilder();
            var fields = new List<string>();
            bool inQuotes = false;
            int currentLine = 1;
            int rowStartLine = 1;

            void EndField()
            {
                fields.Add(field.ToString());
                field.Clear();
            }

            void EndRow()
            {
                EndField();
                rows.Add((rowStartLine, fields.ToArray()));
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
                        if (c == '\n') currentLine++;
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
                        currentLine++;
                        rowStartLine = currentLine;
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

            rows.RemoveAll(r => r.cells.All(string.IsNullOrWhiteSpace));
            return rows;
        }
    }
}

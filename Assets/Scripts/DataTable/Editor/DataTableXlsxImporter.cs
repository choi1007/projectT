using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace ProjectT.DataTable.Editor
{
    /// <summary>
    /// 실제 .xlsx 파일(엑셀로 만든 원본)을 읽어서 CSV로 변환하고,
    /// 그 CSV를 DataTableCsvImporter로 넘겨 검증 후 ScriptableObject/Addressable까지 한 번에 적용하는 툴.
    ///
    /// 규칙은 CSV 임포터와 동일: 첫 번째 시트 기준으로
    ///   1행: 컬럼 이름 / 2행: 타입(int, float, bool, string) / 3행부터: 데이터
    ///
    /// 변환된 CSV의 줄 번호는 엑셀 행 번호와 같습니다 (중간에 빈 행이 있어도 유지).
    /// 그래서 검증 에러의 "5행 C열"을 엑셀에서 그대로 찾아가면 됩니다.
    ///
    /// 사용법:
    ///   Tools > Data Table > Import XLSX...              (파일 하나 선택해서 변환+적용)
    ///   Tools > Data Table > Import All In ExcelTables    (Assets/ExcelTables 안의 모든 .xlsx 일괄 처리)
    /// </summary>
    public static class DataTableXlsxImporter
    {
        private const string ExcelFolder = "Assets/ExcelTables";
        private const string RawTablesFolder = "Assets/RawTables";

        [MenuItem("Tools/Data Table/Import XLSX...")]
        public static void ImportSingle()
        {
            var path = EditorUtility.OpenFilePanel("Import Data Table XLSX", ExcelFolder, "xlsx");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ConvertAndImport(path);
            if (asset != null)
            {
                EditorUtility.DisplayDialog("Data Table Import (XLSX)",
                    $"'{asset.tableName}' 테이블을 xlsx에서 csv로 변환 후 임포트했습니다. ({asset.RowCount}행, {asset.columns.Count}컬럼)", "OK");
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            else
            {
                EditorUtility.DisplayDialog("Data Table Import (XLSX)",
                    "데이터에 오류가 있어 임포트하지 않았습니다. 기존 테이블은 그대로입니다.\n콘솔에서 오류 위치(엑셀 행/열)를 확인하세요.", "OK");
            }
        }

        [MenuItem("Tools/Data Table/Import All In ExcelTables")]
        public static void ImportAll()
        {
            if (!Directory.Exists(ExcelFolder))
            {
                Debug.LogWarning($"[DataTableXlsxImporter] 폴더가 없습니다: {ExcelFolder}");
                return;
            }

            // 엑셀이 열려 있을 때 생기는 임시 잠금 파일(~$로 시작)은 제외
            var files = Directory.GetFiles(ExcelFolder, "*.xlsx", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                .ToArray();

            var succeeded = new List<string>();
            var failed = new List<string>();
            foreach (var file in files)
            {
                var asset = ConvertAndImport(file);
                if (asset != null) succeeded.Add(asset.tableName);
                else failed.Add(Path.GetFileName(file));
            }

            AssetDatabase.SaveAssets();
            if (failed.Count > 0)
            {
                Debug.LogError($"[DataTableXlsxImporter] {failed.Count}개 테이블 임포트 실패 (기존 데이터 유지): {string.Join(", ", failed)}");
            }
            Debug.Log($"[DataTableXlsxImporter] {succeeded.Count}개 테이블 임포트 완료: {string.Join(", ", succeeded)}");
        }

        /// <summary>xlsx -> Assets/RawTables/&lt;이름&gt;.csv 로 변환 후, CSV 임포터(검증 포함)를 호출합니다.</summary>
        public static DataTableAsset ConvertAndImport(string xlsxPath)
        {
            if (!File.Exists(xlsxPath))
            {
                Debug.LogError($"[DataTableXlsxImporter] 파일을 찾을 수 없습니다: {xlsxPath}");
                return null;
            }

            var tableName = Path.GetFileNameWithoutExtension(xlsxPath);

            List<string[]> rows;
            try
            {
                rows = ReadXlsxRows(xlsxPath);
            }
            catch (IOException e)
            {
                Debug.LogError($"[DataTableXlsxImporter] '{Path.GetFileName(xlsxPath)}'을(를) 열 수 없습니다. 엑셀에서 파일을 열어두셨다면 닫고 다시 시도하세요. ({e.Message})");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataTableXlsxImporter] xlsx 파싱 실패 ({xlsxPath}): {e.Message}");
                return null;
            }

            if (rows.Count < 2)
            {
                Debug.LogError($"[DataTableXlsxImporter] 시트에 최소 헤더 2줄(이름/타입)이 필요합니다: {xlsxPath}");
                return null;
            }

            var csvText = RowsToCsv(rows);
            var errors = new List<string>();
            var warnings = new List<string>();
            if (!DataTableCsvImporter.TryBuildTable(csvText, errors, warnings, out _, out _))
            {
                Debug.LogError($"[DataTableXlsxImporter] '{tableName}' 검증 실패. 기존 CSV와 테이블을 유지합니다.\n{string.Join("\n", errors)}");
                return null;
            }

            var rawTablesAbsolute = Path.Combine(Directory.GetCurrentDirectory(), RawTablesFolder);
            Directory.CreateDirectory(rawTablesAbsolute);
            var csvAbsolutePath = Path.Combine(rawTablesAbsolute, tableName + ".csv");

            File.WriteAllText(csvAbsolutePath, csvText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var csvProjectPath = $"{RawTablesFolder}/{tableName}.csv";
            AssetDatabase.ImportAsset(csvProjectPath);

            Debug.Log($"[DataTableXlsxImporter] '{tableName}.xlsx' -> '{csvProjectPath}' 변환 완료, 검증 및 임포트 중...");
            return DataTableCsvImporter.ImportCsvFile(csvAbsolutePath);
        }

        // ---------------------------------------------------------------
        // xlsx 파싱 (zip 안의 xl/worksheets/sheet1.xml + xl/sharedStrings.xml)
        // ---------------------------------------------------------------

        /// <summary>
        /// 첫 번째 시트를 읽어 행 목록으로 돌려줍니다. 결과의 인덱스 i는 엑셀 (i+1)행에 해당하며,
        /// 중간의 빈 행도 빈 배열로 유지해 줄 번호가 어긋나지 않게 합니다. 끝쪽 빈 행만 잘라냅니다.
        /// </summary>
        private static List<string[]> ReadXlsxRows(string xlsxPath)
        {
            using var fs = File.OpenRead(xlsxPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

            var sharedStrings = ReadSharedStrings(archive);
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
                              ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml"));

            if (sheetEntry == null)
            {
                throw new InvalidDataException("xl/worksheets/sheet1.xml을 찾을 수 없습니다 (첫 번째 시트 기준으로만 동작합니다).");
            }

            var doc = new XmlDocument();
            using (var sheetStream = sheetEntry.Open())
            {
                doc.Load(sheetStream);
            }

            var rowNodes = doc.GetElementsByTagName("row");
            var rowsByNumber = new Dictionary<int, Dictionary<int, string>>();
            int maxCol = 0;
            int maxRow = 0;
            int nextRowNumber = 1;

            foreach (XmlNode rowNode in rowNodes)
            {
                // <row r="5"> 의 r이 실제 엑셀 행 번호. 없으면 이전 행 다음 번호로 간주.
                int rowNumber = nextRowNumber;
                var rAttr = rowNode.Attributes?["r"]?.Value;
                if (!string.IsNullOrEmpty(rAttr) &&
                    int.TryParse(rAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRow) &&
                    parsedRow > 0)
                {
                    rowNumber = parsedRow;
                }
                nextRowNumber = rowNumber + 1;

                var cellDict = new Dictionary<int, string>();
                foreach (XmlNode cellNode in rowNode.ChildNodes)
                {
                    if (cellNode.Name != "c") continue;
                    var refAttr = cellNode.Attributes?["r"]?.Value ?? "";
                    var colIndex = ColumnLettersToIndex(refAttr);
                    var typeAttr = cellNode.Attributes?["t"]?.Value;
                    var value = ExtractCellValue(cellNode, typeAttr, sharedStrings);
                    cellDict[colIndex] = value;
                    if (colIndex + 1 > maxCol) maxCol = colIndex + 1;
                }

                rowsByNumber[rowNumber] = cellDict;
                if (rowNumber > maxRow) maxRow = rowNumber;
            }

            var rows = new List<string[]>(maxRow);
            for (int rowNumber = 1; rowNumber <= maxRow; rowNumber++)
            {
                var arr = new string[maxCol];
                rowsByNumber.TryGetValue(rowNumber, out var cellDict);
                for (int i = 0; i < maxCol; i++)
                {
                    arr[i] = cellDict != null && cellDict.TryGetValue(i, out var v) ? v : string.Empty;
                }
                rows.Add(arr);
            }

            // 끝쪽 빈 행만 제거 (중간 빈 행은 줄 번호 유지를 위해 남겨둠 — CSV 임포터가 건너뜀)
            while (rows.Count > 0 && rows[rows.Count - 1].All(string.IsNullOrWhiteSpace))
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return rows;
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var list = new List<string>();
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;

            var doc = new XmlDocument();
            using (var stream = entry.Open())
            {
                doc.Load(stream);
            }

            var siNodes = doc.GetElementsByTagName("si");
            foreach (XmlNode si in siNodes)
            {
                // <si><t>text</t></si> 또는 서식이 섞인 <si><r><t>a</t></r><r><t>b</t></r></si>
                var sb = new StringBuilder();
                CollectText(si, sb);
                list.Add(sb.ToString());
            }
            return list;
        }

        private static void CollectText(XmlNode node, StringBuilder sb)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name == "t")
                {
                    sb.Append(child.InnerText);
                }
                else if (child.HasChildNodes)
                {
                    CollectText(child, sb);
                }
            }
        }

        private static string ExtractCellValue(XmlNode cellNode, string typeAttr, List<string> sharedStrings)
        {
            if (typeAttr == "inlineStr")
            {
                var isNode = cellNode["is"];
                if (isNode == null) return string.Empty;
                var sb = new StringBuilder();
                CollectText(isNode, sb);
                return sb.ToString();
            }

            var vNode = cellNode["v"];
            if (vNode == null) return string.Empty;
            var raw = vNode.InnerText;

            switch (typeAttr)
            {
                case "s": // shared string
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) &&
                        idx >= 0 && idx < sharedStrings.Count)
                    {
                        return sharedStrings[idx];
                    }
                    return string.Empty;
                case "b": // boolean
                    return raw == "1" ? "TRUE" : "FALSE";
                case "str": // 수식 결과 문자열
                default:
                    return raw; // 숫자거나(number), 타입 지정이 없는 경우 raw 값 그대로 (2번째 헤더 행 타입으로 검증됨)
            }
        }

        private static int ColumnLettersToIndex(string cellRef)
        {
            int col = 0;
            foreach (var ch in cellRef)
            {
                if (!char.IsLetter(ch)) break;
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return Mathf.Max(0, col - 1);
        }

        // ---------------------------------------------------------------
        // CSV 쓰기 (RFC4180 스타일 최소 이스케이핑)
        // ---------------------------------------------------------------

        private static string RowsToCsv(List<string[]> rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                for (int i = 0; i < row.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(EscapeCsvField(row[i]));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string EscapeCsvField(string field)
        {
            if (field == null) return string.Empty;
            bool needsQuote = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
            if (!needsQuote) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
    }
}

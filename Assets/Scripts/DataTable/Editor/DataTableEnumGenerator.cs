using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ProjectT.DataTable.Editor
{
    /// <summary>enum 컬럼에서 스크립트를 만들고 컴파일 후 중단된 임포트를 재개합니다.</summary>
    [InitializeOnLoad]
    public static class DataTableEnumGenerator
    {
        private const string Marker = "// 테이블 임포터 자동 생성 enum";
        private const string DefaultNamespace = "ProjectT.DataTable.Enums";
        private static string QueueKey => "DataTable.EnumImports." + Application.dataPath;
        public static bool IsPending { get; private set; }
        [Serializable] private class Pending { public List<string> paths = new List<string>(); }
        static DataTableEnumGenerator() { EditorApplication.update += Resume; }

        // 일반 이름은 그대로 쓰고, C# 예약어에만 @를 붙입니다.
        private static readonly HashSet<string> Keywords = new HashSet<string>(
            ("abstract as base bool break byte case catch char checked class const continue decimal default " +
             "delegate do double else enum event explicit extern false finally fixed float for foreach goto " +
             "if implicit in int interface internal is lock long namespace new null object operator out override " +
             "params private protected public readonly ref return sbyte sealed short sizeof stackalloc static " +
             "string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual " +
             "void volatile while __arglist __makeref __reftype __refvalue").Split(' '), StringComparer.Ordinal);

        private static string EscapeIdentifier(string name) => Keywords.Contains(name) ? "@" + name : name;

        private static bool Identifier(string name) => Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");

        public static bool Prepare(string csv, string sourcePath)
        {
            IsPending = false;
            try { return PrepareCore(csv, Path.GetFullPath(sourcePath)); }
            catch (Exception e) { Debug.LogError($"[DataTableEnumGenerator] {e.Message}"); return false; }
        }

        private static bool PrepareCore(string csv, string sourcePath)
        {
            var rows = DataTableCsvImporter.ParseCsv(csv);
            if (rows.Count < 2) return true;
            // 일반 컬럼 검증에 실패하면 enum 파일도 생성하지 않습니다.
            var validationRows = rows.Select(r => (string[])r.cells.Clone()).ToList();
            for (int i = 0; i < validationRows[1].Length; i++)
                if (validationRows[1][i].Trim().StartsWith("enum:", StringComparison.OrdinalIgnoreCase))
                    validationRows[1][i] = "string";
            string validationCsv = string.Join("\n", validationRows.Select(r => string.Join(",", r.Select(v => "\"" + v.Replace("\"", "\"\"") + "\""))));
            var errors = new List<string>();
            if (!DataTableCsvImporter.TryBuildTable(validationCsv, errors, new List<string>(), out _, out _))
                throw new InvalidOperationException(string.Join("\n", errors));

            string generatorPath = AssetDatabase.FindAssets("DataTableEnumGenerator t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).Single(p => p.EndsWith("/DataTableEnumGenerator.cs"));
            string folder = Path.GetDirectoryName(Path.GetDirectoryName(generatorPath)).Replace('\\', '/') + "/Enum";
            var definitions = new Dictionary<string, HashSet<string>>();
            var runtimeTypes = DataTableCsvImporter.GetEnumTypes().ToArray();
            for (int c = 0; c < rows[1].cells.Length; c++)
            {
                string header = rows[1].cells[c].Trim();
                if (!header.StartsWith("enum:", StringComparison.OrdinalIgnoreCase)) continue;
                string requested = header.Substring(5).Trim();
                if (!requested.Split('.').All(Identifier)) throw new InvalidOperationException($"잘못된 enum 타입 이름: {requested}");
                var matches = runtimeTypes.Where(t => t.FullName == requested || t.Name == requested).ToArray();
                if (matches.Length > 1) throw new InvalidOperationException($"enum 이름 중복: {requested}. 네임스페이스를 포함하세요.");
                string fullName = matches.Length == 1 ? matches[0].FullName : requested.Contains(".") ? requested : DefaultNamespace + "." + requested;
                if (!definitions.TryGetValue(fullName, out var values)) definitions[fullName] = values = new HashSet<string>();
                foreach (var row in rows.Skip(2))
                {
                    string value = c < row.cells.Length ? row.cells[c].Trim() : "";
                    if (!Identifier(value)) throw new InvalidOperationException($"{row.line}행: enum 항목 '{value}'은(는) 올바른 이름이 아닙니다.");
                    if (value == fullName.Split('.').Last()) throw new InvalidOperationException($"enum 항목은 타입 이름과 같을 수 없습니다: {value}");
                    values.Add(value);
                }
            }

            var writes = new Dictionary<string, string>();
            var fileOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions)
            {
                string fullName = definition.Key;
                int dot = fullName.LastIndexOf('.');
                string ns = dot >= 0 ? fullName.Substring(0, dot) : "";
                string name = fullName.Substring(dot + 1);
                string path = folder + "/" + name + ".cs";
                if (fileOwners.TryGetValue(path, out var owner) && owner != fullName)
                    throw new InvalidOperationException($"enum 파일 이름이 중복됩니다: {name}. 서로 다른 타입 이름을 사용하세요.");
                fileOwners[path] = fullName;
                var existing = runtimeTypes.SingleOrDefault(t => t.FullName == fullName);
                // 기존 수동 작성 enum은 덮어쓰지 않습니다.
                if (!File.Exists(path) && existing != null)
                {
                    if (definition.Value.All(v => Enum.IsDefined(existing, v))) continue;
                    throw new InvalidOperationException($"수동 enum {fullName}에 없는 항목입니다. 기존 코드를 자동으로 덮어쓰지 않습니다.");
                }
                var members = new Dictionary<string, int>();
                if (File.Exists(path))
                {
                    string old = File.ReadAllText(path);
                    if (!old.StartsWith(Marker)) throw new InvalidOperationException($"자동 생성 파일이 아니므로 수정하지 않습니다: {path}");
                    var namespaceMatch = Regex.Match(old, @"\bnamespace\s+([\w@.]+)");
                    string oldNamespace = namespaceMatch.Success ? namespaceMatch.Groups[1].Value.Replace("@", "") : "";
                    if (oldNamespace != ns)
                        throw new InvalidOperationException($"같은 이름의 enum 파일이 다른 네임스페이스에서 사용 중입니다: {path}");
                    foreach (Match m in Regex.Matches(old, @"@?(\w+)\s*=\s*(\d+)\s*,"))
                        members.Add(m.Groups[1].Value, int.Parse(m.Groups[2].Value));
                }
                int next = members.Count == 0 ? 0 : checked(members.Values.Max() + 1);
                foreach (string value in definition.Value.OrderBy(v => v, StringComparer.Ordinal))
                    if (!members.ContainsKey(value)) { members.Add(value, next); next = checked(next + 1); }
                if (members.Count == 0) throw new InvalidOperationException($"enum {fullName}을 생성하려면 데이터가 한 개 이상 필요합니다.");
                string source = Marker + "\n// 기존 항목과 숫자값은 유지하며 새 항목만 추가합니다.\n";
                if (ns.Length > 0) source += "namespace " + string.Join(".", ns.Split('.').Select(EscapeIdentifier)) + "\n{\n";
                source += "    public enum " + EscapeIdentifier(name) + "\n    {\n";
                foreach (var member in members.OrderBy(p => p.Value)) source += $"        {EscapeIdentifier(member.Key)} = {member.Value},\n";
                source += "    }\n" + (ns.Length > 0 ? "}\n" : "");
                if (!File.Exists(path) || File.ReadAllText(path) != source) writes[path] = source;
            }
            bool requiresCompilation = definitions.Any(d =>
            {
                var type = runtimeTypes.SingleOrDefault(t => t.FullName == d.Key);
                return type == null || d.Value.Any(v => !Enum.IsDefined(type, v));
            });
            if (writes.Count == 0 && !requiresCompilation) return true;
            var pending = JsonUtility.FromJson<Pending>(SessionState.GetString(QueueKey, "{}"));
            if (!pending.paths.Contains(sourcePath)) pending.paths.Add(sourcePath);
            SessionState.SetString(QueueKey, JsonUtility.ToJson(pending));
            Directory.CreateDirectory(folder);
            foreach (var write in writes) File.WriteAllText(write.Key, write.Value, new UTF8Encoding(false));
            IsPending = true;
            Debug.Log("[DataTableEnumGenerator] enum 생성 완료. 컴파일 후 테이블 임포트를 자동으로 이어갑니다.");
            AssetDatabase.Refresh();
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            return false;
        }

        [DidReloadScripts]
        private static void OnReload()
        {
            EditorApplication.update -= Resume;
            EditorApplication.update += Resume;
        }

        private static void Resume()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { return; }
            if (EditorUtility.scriptCompilationFailed) return;
            string json = SessionState.GetString(QueueKey, "");
            if (string.IsNullOrEmpty(json)) return;
            SessionState.EraseString(QueueKey);
            foreach (string path in JsonUtility.FromJson<Pending>(json).paths)
            {
                var asset = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? DataTableXlsxImporter.ConvertAndImport(path) : DataTableCsvImporter.ImportCsvFile(path);
                if (asset != null) Debug.Log($"[DataTableEnumGenerator] 자동 임포트 완료: {asset.tableName}");
            }
        }
    }
}

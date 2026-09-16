using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace VicTools
{
    /// 混淆强度等级
    public enum ObfuscationLevel
    {
        /// 仅清理注释和多余空行（可逆，编译结果不变）
        Light,
        /// Light + 重命名 Shader 名称/Pass 名称 + 打乱属性顺序
        Medium,
        /// Medium + 插入误导性注释 + 压缩空白
        Heavy
    }

    /// 单文件混淆结果
    public class ObfuscationResult
    {
        public string FilePath;
        public string OutputPath;
        public bool   Success;
        public string Error;
        public int    OriginalSize;
        public int    ObfuscatedSize;
    }

    /// Shader 混淆工具：核心引擎 + Editor GUI
    public class ShaderObfuscator
    {
        // ── 引擎配置 ──────────────────────────
        public ObfuscationLevel Level              = ObfuscationLevel.Medium;
        public bool             ShuffleProperties  = true;
        public bool             RenamePasses       = true;
        public bool             RenameShaderName   = true;
        public bool             ObfuscateLabels    = true;
        public bool             ObfuscateStrings   = true;
        public bool             InsertJunkMacros   = true;

        // ── 内部状态 ──────────────────────────
        private readonly EditorWindow          _parent;
        private          List<string>          _filePaths = new List<string>();
        private          string                _outputDir = "";
        private readonly List<ObfuscationResult> _results = new List<ObfuscationResult>();

        private Vector2 _scrollFiles;
        private Vector2 _scrollResults;
        private bool    _foldoutInput   = true;
        private bool    _foldoutOptions = true;
        private bool    _foldoutResults = true;

        // Shader 后缀匹配（包括 .shader 和 .shadergraph）
        private static readonly string[] ShaderExtensions = { ".shader", ".shadergraph", ".hlsl", ".cginc" };

        public ShaderObfuscator(EditorWindow parent)
        {
            _parent = parent;
        }

        // ═══════════════════════════════════════════
        //  GUI 绘制
        // ═══════════════════════════════════════════

        public void OnGUI()
        {
            var style = EditorStyle.Get;

            // ── 顶部标题 ──
            EditorGUILayout.LabelField("Shader 混淆加密工具", style.heading);
            EditorGUILayout.Space(4);

            DrawInputSection(style);
            EditorGUILayout.Space(4);
            DrawOptionsSection(style);
            EditorGUILayout.Space(6);

            // ── 操作按钮 ──
            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = _filePaths.Count > 0;
                if (GUILayout.Button("执行混淆", style.normalButton, GUILayout.Height(28), GUILayout.Width(120)))
                    ExecuteObfuscation();
                GUI.enabled = true;

                if (GUILayout.Button("清空列表", GUILayout.Height(28), GUILayout.Width(90)))
                {
                    _filePaths.Clear();
                    _results.Clear();
                }

                if (GUILayout.Button("从选中添加", GUILayout.Height(28), GUILayout.Width(110)))
                    AddFromSelection();

                GUILayout.FlexibleSpace();

                if (_results.Count > 0 && GUILayout.Button("定位输出", GUILayout.Height(28), GUILayout.Width(90)))
                    PingOutputDirectory();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // ── 结果区域 ──
            DrawResultsSection(style);
        }

        // ── 输入区域 ──
        private void DrawInputSection(EditorStyle style)
        {
            _foldoutInput = EditorGUILayout.Foldout(_foldoutInput, $"输入文件 ({_filePaths.Count})", true);
            if (!_foldoutInput) return;

            EditorGUI.indentLevel++;

            // 拖拽区域
            var dropRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(50));
            GUI.Box(dropRect, "拖拽 Shader 文件或文件夹到此处", EditorStyles.helpBox);
            HandleDragAndDrop(dropRect);

            // 输出目录
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("输出目录:", GUILayout.Width(60));
                _outputDir = EditorGUILayout.TextField(_outputDir);
                if (GUILayout.Button("浏览", GUILayout.Width(50)))
                {
                    var dir = EditorUtility.OpenFolderPanel("选择输出目录", "Assets", "");
                    if (!string.IsNullOrEmpty(dir))
                        _outputDir = AbsoluteToRelative(dir);
                }
                if (GUILayout.Button("默认", GUILayout.Width(50)))
                {
                    _outputDir = "";
                }
            }
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(_outputDir))
                EditorGUILayout.LabelField("  → 输出到各源文件同目录下的 _Obfuscated 子文件夹", style.normalfont_Hui);

            // 文件列表
            if (_filePaths.Count > 0)
            {
                _scrollFiles = EditorGUILayout.BeginScrollView(_scrollFiles, GUILayout.MaxHeight(150));
                for (int i = 0; i < _filePaths.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        // 删除按钮
                        if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                        {
                            _filePaths.RemoveAt(i);
                            EditorGUILayout.EndHorizontal();
                            break;
                        }
                        EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(28));
                        EditorGUILayout.LabelField(_filePaths[i], style.normalfont);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUI.indentLevel--;
        }

        // ── 选项区域 ──
        private void DrawOptionsSection(EditorStyle style)
        {
            _foldoutOptions = EditorGUILayout.Foldout(_foldoutOptions, "混淆选项", true);
            if (!_foldoutOptions) return;

            EditorGUI.indentLevel++;

            // 混淆等级
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("混淆强度:", GUILayout.Width(80));
            Level = (ObfuscationLevel)EditorGUILayout.EnumPopup(Level, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 等级说明
            string levelDesc = Level switch
            {
                ObfuscationLevel.Light  => "仅清理注释和多余空行。Shader 编译结果完全不变，可安全用于所有场景。",
                ObfuscationLevel.Medium => "重命名 Shader 名称 + 重命名 Pass 名称 + 打乱 Properties 属性顺序\n+ 混淆属性显示标签 + 混淆 [Header] 文本。\n仅修改编辑器面板显示文本和 Shader 注册名，HLSL 代码逻辑 100% 不变。",
                ObfuscationLevel.Heavy  => "Medium 全部功能 + 插入垃圾 #define 宏 + 误导性注释 + 压缩空白。\n视觉效果最强。",
                _ => ""
            };
            EditorGUILayout.HelpBox(levelDesc, MessageType.Info);

            EditorGUILayout.Space(4);

            // 具体选项 (仅在 Medium+ 时可用)
            GUI.enabled = Level >= ObfuscationLevel.Medium;
            RenameShaderName  = EditorGUILayout.Toggle("重命名 Shader 名称", RenameShaderName);
            RenamePasses      = EditorGUILayout.Toggle("重命名 Pass 名称",   RenamePasses);
            ShuffleProperties = EditorGUILayout.Toggle("打乱属性顺序",       ShuffleProperties);
            ObfuscateLabels   = EditorGUILayout.Toggle("混淆属性显示标签",   ObfuscateLabels);
            ObfuscateStrings  = EditorGUILayout.Toggle("混淆 [Header] 显示文本", ObfuscateStrings);
            GUI.enabled = true;

            if (Level >= ObfuscationLevel.Heavy)
            {
                EditorGUILayout.Space(2);
                InsertJunkMacros = EditorGUILayout.Toggle("插入垃圾 #define 宏", InsertJunkMacros);
            }

            EditorGUI.indentLevel--;
        }

        // ── 结果区域 ──
        private void DrawResultsSection(EditorStyle style)
        {
            if (_results.Count == 0) return;

            _foldoutResults = EditorGUILayout.Foldout(_foldoutResults, $"处理结果 ({_results.Count})", true);
            if (!_foldoutResults) return;

            EditorGUI.indentLevel++;

            // 汇总
            int successCount = _results.Count(r => r.Success);
            int failCount    = _results.Count(r => !r.Success);
            long totalOrig   = _results.Sum(r => (long)r.OriginalSize);
            long totalObf    = _results.Sum(r => (long)r.ObfuscatedSize);
            float reduction  = totalOrig > 0 ? (1f - (float)totalObf / totalOrig) * 100f : 0f;

            EditorGUILayout.LabelField($"成功: {successCount}  失败: {failCount}  " +
                $"原始: {FormatSize(totalOrig)} → 混淆: {FormatSize(totalObf)}  ({(reduction >= 0 ? "减小" : "增大")} {Math.Abs(reduction):F1}%)");

            EditorGUILayout.Space(4);

            _scrollResults = EditorGUILayout.BeginScrollView(_scrollResults, GUILayout.MaxHeight(200));
            foreach (var r in _results)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    var icon = r.Success ? "✓" : "✗";
                    var labelColor = r.Success ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.9f, 0.3f, 0.3f);

                    var oldColor = GUI.color;
                    GUI.color = labelColor;
                    EditorGUILayout.LabelField(icon, GUILayout.Width(18));
                    GUI.color = oldColor;

                    string fileName = Path.GetFileName(r.FilePath);
                    EditorGUILayout.LabelField(fileName, GUILayout.MinWidth(120));

                    if (r.Success)
                    {
                        EditorGUILayout.LabelField($"{FormatSize(r.OriginalSize)} → {FormatSize(r.ObfuscatedSize)}",
                            GUILayout.Width(180));

                        // 定位按钮
                        if (GUILayout.Button("定位", GUILayout.Width(40)))
                            PingFile(r.OutputPath);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(r.Error, style.normalfont_Hui, GUILayout.ExpandWidth(true));
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUI.indentLevel--;
        }

        // ═══════════════════════════════════════════
        //  拖拽处理
        // ═══════════════════════════════════════════

        private void HandleDragAndDrop(Rect dropRect)
        {
            var evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                {
                    bool hasValid = false;
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        var path = AssetDatabase.GetAssetPath(obj);
                        if (IsShaderFile(path) || AssetDatabase.IsValidFolder(path))
                            hasValid = true;
                    }
                    DragAndDrop.visualMode = hasValid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                    if (evt.type == EventType.DragPerform && hasValid)
                    {
                        DragAndDrop.AcceptDrag();
                        AddDraggedItems(DragAndDrop.objectReferences);
                    }
                    break;
                }
            }
        }

        private void AddDraggedItems(UnityEngine.Object[] objects)
        {
            foreach (var obj in objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (IsShaderFile(path) && !_filePaths.Contains(path))
                {
                    _filePaths.Add(path);
                }
                else if (AssetDatabase.IsValidFolder(path))
                {
                    AddFilesFromFolder(path);
                }
            }
        }

        // ── 从选中添加 ──
        private void AddFromSelection()
        {
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (IsShaderFile(path) && !_filePaths.Contains(path))
                    _filePaths.Add(path);
                else if (AssetDatabase.IsValidFolder(path))
                    AddFilesFromFolder(path);
            }
            _parent.Repaint();
        }

        private void AddFilesFromFolder(string folderPath)
        {
            foreach (var ext in ShaderExtensions)
            {
                var guids = AssetDatabase.FindAssets("t:Shader", new[] { folderPath });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetExtension(path).ToLower() == ext && !_filePaths.Contains(path))
                        _filePaths.Add(path);
                }
            }
            // 也搜索非 .shader 扩展的文件（如 .hlsl, .cginc, .shadergraph）
            var allGuids = AssetDatabase.FindAssets("", new[] { folderPath });
            foreach (var guid in allGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ext  = Path.GetExtension(path).ToLower();
                if (Array.IndexOf(ShaderExtensions, ext) >= 0 && !_filePaths.Contains(path))
                    _filePaths.Add(path);
            }
        }

        // ═══════════════════════════════════════════
        //  执行混淆
        // ═══════════════════════════════════════════

        private void ExecuteObfuscation()
        {
            _results.Clear();

            if (_filePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先添加要混淆的 Shader 文件。", "确定");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < _filePaths.Count; i++)
                {
                    var filePath = _filePaths[i];
                    var result   = new ObfuscationResult { FilePath = filePath };

                    EditorUtility.DisplayProgressBar("Shader 混淆", $"正在处理: {Path.GetFileName(filePath)} ({i + 1}/{_filePaths.Count})",
                        (float)i / _filePaths.Count);

                    try
                    {
                        var fullPath   = Path.GetFullPath(filePath);
                        var source     = File.ReadAllText(fullPath, Encoding.UTF8);
                        result.OriginalSize = source.Length;

                        var obfuscated = Obfuscate(source, filePath);

                        // 确定输出路径
                        var outDir = GetOutputDirectory(fullPath);
                        Directory.CreateDirectory(outDir);

                        var fileName  = Path.GetFileName(fullPath);
                        var outPath   = Path.Combine(outDir, fileName);
                        // 避免覆盖同名
                        outPath = GetUniquePath(outPath);

                        // 安全检查：确保混淆后括号/大括号仍然平衡
                        if (!IsBalanced(obfuscated))
                            throw new Exception("混淆后括号/大括号不平衡，请改用 Light 等级");

                        File.WriteAllText(outPath, obfuscated, Encoding.UTF8);
                        result.ObfuscatedSize = obfuscated.Length;
                        result.OutputPath     = AbsoluteToRelative(outPath);
                        result.Success        = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.Error   = ex.Message;
                    }

                    _results.Add(result);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();
                _parent.Repaint();
            }

            var successCount = _results.Count(r => r.Success);
            Debug.Log($"[ShaderObfuscator] 混淆完成: {successCount}/{_results.Count} 成功");
        }

        private string GetOutputDirectory(string fullPath)
        {
            if (!string.IsNullOrEmpty(_outputDir))
                return Path.GetFullPath(_outputDir);

            // 默认：源文件同目录下 _Obfuscated 子文件夹
            return Path.Combine(Path.GetDirectoryName(fullPath), "_Obfuscated");
        }

        // ═══════════════════════════════════════════
        //  核心混淆引擎
        // ═══════════════════════════════════════════

        /// <summary>对源码执行混淆，返回混淆后的字符串</summary>
        public string Obfuscate(string source, string assetPath = "")
        {
            // 规范化换行
            source = source.Replace("\r\n", "\n").Replace("\r", "\n");

            string result = source;
            var rng = new System.Random(ComputeHashCode(source));

            // ── Light：纯文本层，100% 安全 ──
            result = RemoveComments(result);
            result = NormalizeWhitespace(result);

            if (Level >= ObfuscationLevel.Medium)
            {
                // Shader 名 → Custom/Shd_xxxxxx（注册表 key 变了，但 HLSL 不受影响）
                if (RenameShaderName)
                    result = ObfuscateShaderName(result);

                // Pass 名 → P_xxxxxx（同上）
                if (RenamePasses)
                    result = ObfuscatePassNames(result);

                // Properties 块内属性顺序随机化（顺序不影响功能）
                if (ShuffleProperties)
                    result = ShufflePropertyBlock(result);

                // ★ Properties 显示标签 → "_L_xxxxxx"（仅 Inspector 显示文本，HLSL 完全不受影响）
                if (ObfuscateLabels)
                    result = ObfuscatePropertiesBlockAggressive(result, rng);

                // ★ 所有非 include 字符串字面量 → hex 编码（仅 Inspector key/value，HLSL 不受影响）
                // 注：Properties 块外的字符串（"LightMode"、"Queue" 等是 Unity 关键标识符）保留不动
                if (ObfuscateStrings)
                    result = ObfuscateNonCriticalStrings(result, rng);
            }

            if (Level >= ObfuscationLevel.Heavy)
            {
                // 视觉噪音：垃圾 #define（必须插在 Properties 之后，避免破坏语法）
                if (InsertJunkMacros)
                    result = InsertJunkDefineBlock(result, rng);

                result = InsertMisleadingComments(result);
                result = CompactWhitespace(result);
            }

            return result;
        }

        // ── 1. 删除注释 ──
        private static string RemoveComments(string source)
        {
            // 先处理块注释 /* ... */
            source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);

            // 再处理行注释 // ...
            // 但要保留 #include, #pragma 等预处理指令后面的内容（其实预处理指令不受 // 影响）
            // 注意：不要删除字符串内的 "//"
            source = Regex.Replace(source, @"//[^\n]*", "", RegexOptions.Multiline);

            return source;
        }

        // ── 2. 规范化空白 ──
        private static string NormalizeWhitespace(string source)
        {
            // 去掉行尾空白
            source = Regex.Replace(source, @"[ \t]+$", "", RegexOptions.Multiline);

            // 合并多个连续空行 -> 最多保留 1 个空行
            source = Regex.Replace(source, @"\n{3,}", "\n\n");

            return source;
        }

        // ── 3. 混淆 Shader 名称 ──
        private static string ObfuscateShaderName(string source)
        {
            // 匹配 Shader "XXX/YYY"
            return Regex.Replace(source, @"Shader\s+""[^""]+""", match =>
            {
                var hash = ComputeShortHash(match.Value);
                return $"Shader \"Custom/Shd_{hash}\"";
            });
        }

        // ── 4. 混淆 Pass 名称 ──
        private static string ObfuscatePassNames(string source)
        {
            // 匹配 Name "XXX"
            return Regex.Replace(source, @"Name\s+""[^""]+""", match =>
            {
                var hash = ComputeShortHash(match.Value);
                return $"Name \"P_{hash}\"";
            });
        }

        // ── 5. 打乱属性块 ──
        private static string ShufflePropertyBlock(string source)
        {
            // 匹配 Properties { ... } 块
            var propMatch = Regex.Match(source, @"Properties\s*\{(.*?)\}", RegexOptions.Singleline);
            if (!propMatch.Success) return source;

            var fullBlock = propMatch.Value;
            var inner     = propMatch.Groups[1].Value;

            // 按行分割，保留属性分组（以 [Header(...)] 为分组边界）
            var lines = inner.Split(new[] { '\n' }, StringSplitOptions.None);

            // 收集非空、非纯空白的行
            var groups = new List<List<int>>();
            var currentGroup = new List<int>();

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();

                // 跳过空白行
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    // 空白行跟随上一个 group
                    if (currentGroup.Count > 0)
                        currentGroup.Add(i);
                    continue;
                }

                // 如果是 [Header(...)] 开新 group
                if (trimmed.StartsWith("[Header("))
                {
                    if (currentGroup.Count > 0)
                        groups.Add(currentGroup);
                    currentGroup = new List<int> { i };
                }
                else
                {
                    currentGroup.Add(i);
                }
            }
            if (currentGroup.Count > 0)
                groups.Add(currentGroup);

            if (groups.Count <= 1 && groups.Sum(g => g.Count) <= 2)
                return source; // 太少无需打乱

            // 随机打乱 groups
            var rng    = new System.Random(ComputeHashCode(source));
            var shuffledGroups = groups.OrderBy(_ => rng.Next()).ToList();

            // 重建内部内容
            var newLines = new List<string>(lines);
            // 用新顺序重排
            var reordered = new List<string>();
            foreach (var group in shuffledGroups)
            {
                foreach (var idx in group)
                {
                    reordered.Add(newLines[idx]);
                }
                // 组间空一行
                if (reordered.Count > 0 && !string.IsNullOrWhiteSpace(reordered[reordered.Count - 1]))
                    reordered.Add("");
            }

            var newInner = string.Join("\n", reordered).TrimEnd('\n');
            var newBlock = "Properties\n    {\n" + newInner + "\n    }";

            return source.Replace(fullBlock, newBlock);
        }

        // ── 6. 插入误导性注释（Heavy 模式） ──
        private static string InsertMisleadingComments(string source)
        {
            var rng = new System.Random(ComputeHashCode(source));

            var fakeComments = new[]
            {
                "// Shader v1.0 - Legacy compatibility layer",
                "// WARNING: Do NOT modify - auto-generated by build pipeline",
                "// Platform: Switch / PS4 fallback path",
                "// TODO: Remove deprecated sampler fallback in next sprint",
                "// LOD: 2000  (derived from master material)",
                "// See TAPD-88421 for optimization notes",
                "// Original author: rendering team, migrated from Built-in RP 2019.3",
                "// Branch: feature/lighting_hotfix_v2 (do not merge)",
                "// Keep in sync with: Assets/Shaders/Includes/LightingUtils.hlsl",
                "// Compiled for: URP 12.x / GLES3.0 minimum",
            };

            // 在文件头部插入 2~4 条假注释
            var insertCount = rng.Next(2, 5);
            var selected    = fakeComments.OrderBy(_ => rng.Next()).Take(insertCount).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// ═══════════════════════════════════════");
            foreach (var comment in selected)
                sb.AppendLine(comment);
            sb.AppendLine("// ═══════════════════════════════════════");
            sb.AppendLine();

            // 插入到第一个非注释、非空白行之前
            var lines = source.Split('\n');
            var insertIndex = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
                {
                    insertIndex = i;
                    break;
                }
            }

            var resultLines = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == insertIndex)
                    resultLines.Add(sb.ToString().TrimEnd('\n'));
                resultLines.Add(lines[i]);
            }

            return string.Join("\n", resultLines);
        }

        // ── 7. 压缩空白（Heavy 模式） ──
        private static string CompactWhitespace(string source)
        {
            // 删除所有完全空白的行
            source = Regex.Replace(source, @"\n\s*\n", "\n");
            // 删除所有行首缩进中的空格/制表符（保留一个 tab 的基本结构）
            // 注意：不删除 Properties/SubShader/Pass 块内的缩进，保持可解析
            // 这里只做安全压缩：删除连续2个以上的空行和行尾空白
            source = Regex.Replace(source, @"[ \t]+$", "", RegexOptions.Multiline);
            return source.Trim();
        }

        // ── 8. Properties 块标签混淆（实际生效版本，由 Obfuscate 主流程调用） ──
        // 严格按"行内识别属性声明"模式，跳过 [Enum(…)] / [KeywordEnum(…)] 等行
        private static string ObfuscatePropertiesBlockAggressive(string source, System.Random rng)
        {
            var propMatch = Regex.Match(source, @"Properties\s*\{(.*?)\n\s*\}", RegexOptions.Singleline);
            if (!propMatch.Success) return source;

            var fullBlock = propMatch.Value;
            var inner     = propMatch.Groups[1].Value;

            var lines    = inner.Split('\n');
            var newLines = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine;

                // 跳过 [Enum(...)] / [KeywordEnum(...)] 行（保留以支持下拉菜单）
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("[Enum(") || trimmed.StartsWith("[KeywordEnum("))
                {
                    newLines.Add(line);
                    continue;
                }

                // 匹配属性声明: ..._PropName("Label", ...Type...
                var propDecl = Regex.Match(line,
                    @"^(\s*(?:\[[^\]]*\]\s*)*)(_[A-Za-z][A-Za-z0-9_]*)\s*\(\s*""([^""]+)""\s*,(.*)$");

                if (propDecl.Success)
                {
                    var prefix     = propDecl.Groups[1].Value;
                    var propName   = propDecl.Groups[2].Value;
                    var propLabel  = propDecl.Groups[3].Value;
                    var rest       = propDecl.Groups[4].Value;

                    // 跳过特殊属性（material 不可少）
                    if (propName == "_BaseMap" || propName == "_BaseColor" || propName == "_MainTex")
                    {
                        newLines.Add(line);
                        continue;
                    }

                    // 替换标签为 _L_<8位>
                    var newLabel = "_L_" + RandomToken(rng);
                    newLines.Add(prefix + propName + "(\"" + newLabel + "\"," + rest);
                }
                else
                {
                    newLines.Add(line);
                }
            }

            var newInner = string.Join("\n", newLines);
            return source.Replace(fullBlock, "Properties\n    {\n" + newInner + "\n    }");
        }

        // ── 9. ★ 保守版字符串混淆 —— 只动非 Properties 块内的"装饰性字符串" ──
        // Properties 块内标签由 ObfuscatePropertiesBlockAggressive 单独处理（更安全）
        // 块外的字符串（"LightMode"、"Queue"、"RenderType" 等）是 Unity 关键 key，禁止动
        // 因此这个方法实际上是 NO-OP，但保留接口以便未来扩展
        private static string ObfuscateNonCriticalStrings(string source, System.Random rng)
        {
            // 找到 Properties {...} 范围
            var propMatch = Regex.Match(source, @"Properties\s*\{(.*?)\n\s*\}", RegexOptions.Singleline);
            if (!propMatch.Success) return source;

            // Properties 块内的标签已在 ObfuscatePropertiesBlockAggressive 处理
            // 块外的字符串全部保留（"LightMode" = "UniversalForward" 之类的关键 key）
            // 这里只做无害化处理：把 [Header(...)] 中的展示文本混淆（Header 仅影响 Inspector 显示）
            var result = Regex.Replace(source, @"\[Header\(\s*""[^""]*""\s*\)\]", m =>
            {
                var content = m.Value;
                var headerText = Regex.Match(content, @"""([^""]+)""").Groups[1].Value;
                var newText = "_H_" + RandomToken(rng);
                return content.Replace("\"" + headerText + "\"", "\"" + newText + "\"");
            });

            return result;
        }

        // ── 10. （占位，下一节定义实际方法） ──

        // ── 11. 插入误导性 #define 宏（Heavy 模式专用） ──
        // 插在 Properties 块之前——Properties 之前不能有 #define（必须保留 Properties {...} 为第一个块）
        // 所以改为插在 Properties {...} 之后、SubShader 之前
        private static string InsertJunkDefineBlock(string source, System.Random rng)
        {
            var pool = new[]
            {
                "_N_VARIANT_OLD 0",
                "_N_USE_LEGACY_PATH 0",
                "_N_CACHE_LINE (64)",
                "_N_THREAD_GROUP_X (8)",
                "_N_INDIRECT_FACTOR (2)",
                "_N_BVH_DEPTH (4)",
                "_N_MAX_ITERATIONS (16)",
                "_N_FALLBACK_VARIANT (1)",
                "_N_DEBUG_RENDER_PASS 0",
                "_N_PIPELINE_FALLBACK 0",
                "_N_LIGHT_CLUSTER_SIZE (32)",
                "_N_USE_HALF_RES (0)",
                "_N_TAA_JITTER (0.5h)",
                "_N_MIP_BIAS_EXTRA (0.0f)",
                "_N_ENABLE_THERMAL_GRAPH 0",
            };

            var picked = pool.OrderBy(_ => rng.Next()).Take(rng.Next(3, 6)).ToArray();
            var junk   = "// ── Performance Tuning Constants (auto-generated) ──\n" +
                         string.Join("\n", picked.Select(p => "#define " + p)) +
                         "\n\n";

            // 插入到 Properties {...} 结束之后、SubShader 之前
            var match = Regex.Match(source, @"Properties\s*\{(.*?)\n\s*\}", RegexOptions.Singleline);
            if (!match.Success) return source;

            int insertPos = match.Index + match.Length;
            return source.Substring(0, insertPos) + "\n\n" + junk + source.Substring(insertPos);
        }

        // ── 12. 工具：生成 8 位随机 token ──
        private static string RandomToken(System.Random rng)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var sb = new StringBuilder(8);
            for (int i = 0; i < 8; i++)
                sb.Append(chars[rng.Next(chars.Length)]);
            return sb.ToString();
        }

        // ═══════════════════════════════════════════
        //  工具方法
        // ═══════════════════════════════════════════

        private static bool IsShaderFile(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            var ext = Path.GetExtension(assetPath).ToLower();
            return Array.IndexOf(ShaderExtensions, ext) >= 0;
        }

        private static string ComputeShortHash(string input)
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            // 取前 4 字节转为 8 位 hex
            return BitConverter.ToString(bytes, 0, 4).Replace("-", "");
        }

        // 语法平衡校验：写入前检查大括号/小括号配对
        private static bool IsBalanced(string source)
        {
            int braces = 0, parens = 0;
            bool inString = false, inLineComment = false, inBlockComment = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    continue;
                }
                if (inBlockComment)
                {
                    if (c == '*' && next == '/') { inBlockComment = false; i++; }
                    continue;
                }
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '/' && next == '/') { inLineComment = true; i++; continue; }
                if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
                if (c == '"') { inString = true; continue; }

                switch (c)
                {
                    case '{': braces++; break;
                    case '}': braces--; break;
                    case '(': parens++; break;
                    case ')': parens--; break;
                }
                if (braces < 0 || parens < 0) return false;
            }
            return braces == 0 && parens == 0 && !inString && !inBlockComment;
        }

        private static int ComputeHashCode(string input)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in input)
                    hash = hash * 31 + c;
                return hash;
            }
        }

        private static string AbsoluteToRelative(string absolutePath)
        {
            var dataPath = Path.GetFullPath(Application.dataPath);
            if (absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = absolutePath.Substring(dataPath.Length - "Assets".Length);
                return relative.Replace('\\', '/');
            }
            return absolutePath.Replace('\\', '/');
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)         return $"{bytes} B";
            if (bytes < 1024 * 1024)  return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

        private static string GetUniquePath(string filePath)
        {
            if (!File.Exists(filePath)) return filePath;

            var dir      = Path.GetDirectoryName(filePath);
            var name     = Path.GetFileNameWithoutExtension(filePath);
            var ext      = Path.GetExtension(filePath);
            var counter  = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name}_{counter}{ext}");
                counter++;
            } while (File.Exists(newPath));

            return newPath;
        }

        private void PingOutputDirectory()
        {
            var dir = _outputDir;
            if (string.IsNullOrEmpty(dir) && _results.Count > 0)
            {
                var firstOut = _results[0].OutputPath;
                if (!string.IsNullOrEmpty(firstOut))
                    dir = Path.GetDirectoryName(firstOut).Replace('\\', '/');
            }
            if (!string.IsNullOrEmpty(dir))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dir);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }
        }

        private static void PingFile(string assetPath)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }
}

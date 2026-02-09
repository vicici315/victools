using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;


namespace VicTools
{

    /// 着色器材质查找工具
    /// 功能：通过着色器查找所有使用该着色器的材质球
    // [UnityEditor.InitializeOnLoad]
    public class ShaderMaterialFinder : SubWindow
    {
    private Shader _targetShader;
    private readonly List<Material> _foundMaterials = new List<Material>();
    private readonly Dictionary<Material, int> _materialUsageCount = new Dictionary<Material, int>(); // 缓存材质使用数量
    private Vector2 _scrollPosition;
    private Vector2 _shaderScrollPosition;
    private bool _showProgress = false;
    private string _searchStatus = "";
    private int _selectCount = 0;
    private bool _hasCachedUsageData = false; // 标记是否已缓存使用数据
    private readonly List<Shader> _foundShaders = new List<Shader>();
    private readonly Dictionary<Shader, int> _shaderUsageCount = new Dictionary<Shader, int>(); // 缓存Shader使用数量
    private bool _showShaderList = false; // 是否显示Shader列表
        public ShaderMaterialFinder(string name, EditorWindow parent) : base("[材质查找 v1.3]", parent)
        {
            // 初始化搜索历史记录管理器
                
        }
        public override void OnGUI()
        {
            var style = EditorStyle.Get;
            
            EditorGUILayout.BeginVertical(style.area);
            
            // 标题
            EditorGUILayout.LabelField("★ 着色器材质查找器", style.subheading);
            EditorGUILayout.Space();
            
            // 着色器选择区域
            EditorGUILayout.BeginVertical(style.subheading2);
            EditorGUILayout.LabelField("选择目标着色器：", style.normalfont);
            EditorGUILayout.BeginHorizontal();
            // 获取选中对象材质的shader按钮
            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("获取Shader →", GUILayout.Width(100)))
            {
                GetShaderFromSelectedObjects();
            }
            GUI.backgroundColor = Color.white;
            _targetShader = (Shader)EditorGUILayout.ObjectField(_targetShader, typeof(Shader), false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space();
            
            // 搜索按钮
            EditorGUILayout.BeginHorizontal();
            
            // 使用局部禁用组替代全局 GUI.enabled
            EditorGUI.BeginDisabledGroup(!_targetShader);
            if (GUILayout.Button("查找使用该着色器的材质", style.normalButton))
            {
                FindMaterialsByShader();
            }
            EditorGUI.EndDisabledGroup();

            // 查找所有Shader按钮
            GUI.backgroundColor = new Color(0.8f, 0.6f, 1f); // 淡紫色
            if (GUILayout.Button("查找所有Shader", style.normalButton))
            {
                FindAllShadersInScene();
            }
            GUI.backgroundColor = Color.white;

            EditorGUI.BeginDisabledGroup(_foundMaterials.Count == 0);
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("选择所有材质", style.normalButton))
            {
                SelectAllMaterials();
                
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndHorizontal();
            
            // 显示搜索状态
            if (_showProgress)
            {
                EditorGUILayout.HelpBox(_searchStatus, MessageType.Info);
            }
            
            EditorGUILayout.Space();
            
            // 显示结果
            if (_foundMaterials.Count > 0)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField($"找到 {_foundMaterials.Count} 个材质：", style.subheading, GUILayout.Width(150));
                // EditorGUILayout.LabelField("【选择模型】可选中场景中使用该材质的物体", style.Normalfont_Hui);
                if (_selectCount > 0)
                {
                    GUI.contentColor = Color.cyan;
                    EditorGUILayout.LabelField($"（选中 {_selectCount} 个物体）", style.subheading2);
                    GUI.contentColor = Color.white;
                }
                // GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, false, false);
                GUIStyle numberStyle = new GUIStyle(style.normalfont_Hui);
                numberStyle.alignment = TextAnchor.MiddleRight;
                
                // 根据材质数量动态计算最小高度
                // 每个材质项大约25像素高度，最小保持90像素，最大不超过500像素
                int dynamicMaterialHeight = Mathf.Clamp(_foundMaterials.Count * 23, 70, 230);
                EditorGUILayout.BeginVertical(EditorStyles.textArea, GUILayout.MinHeight(dynamicMaterialHeight));
                for (int i = 0; i < _foundMaterials.Count; i++)
                {
                    Material material = _foundMaterials[i];
                    if (!material) continue;
                    EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                    
                    // 直接从缓存中获取使用数量，避免重复的场景遍历
                    int mCount = _hasCachedUsageData && _materialUsageCount.ContainsKey(material) 
                        ? _materialUsageCount[material] 
                        : 0;

                    // 使用固定宽度区域来确保按钮和计数文本对齐
                    // EditorGUILayout.BeginHorizontal();
                    if (mCount > 0)
                    {
                        GUI.backgroundColor = Color.cyan;
                        if (GUILayout.Button("选择模型", GUILayout.Width(60)))
                        {
                            // 选中场景中使用该材质的所有物体
                            SelectObjectsUsingMaterial(material, true);
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.Label($"({mCount})", style.normalfont, GUILayout.ExpandWidth(true), GUILayout.MinWidth(30), GUILayout.MaxWidth(45));
                    }
                    else
                    {
                        GUILayout.Label("", GUILayout.Width(106)); // 使用空标签保持对齐
                    }
                    // EditorGUILayout.EndHorizontal();

                    // 显示材质信息 - 使用ExpandWidth让ObjectField自动扩展
                    EditorGUILayout.ObjectField(material, typeof(Material), false, GUILayout.ExpandWidth(true));
                    // 添加赋予按钮
                    GUI.backgroundColor = Color.green;
                    if (GUILayout.Button("←", GUILayout.Width(20), GUILayout.ExpandWidth(false)))
                    {
                        SceneTools.AssignMaterialToSelectedModels(material);
                        FindMaterialsByShader();
                    }
                    GUI.backgroundColor = Color.white;
                    
                    GUI.backgroundColor = Color.cyan;
                    // 选择按钮 - 移除固定宽度，让按钮根据文本长度自动调整
                    if (GUILayout.Button("选择", GUILayout.Width(40)))
                    {
                        // 检查是否按住Ctrl键
                        Event currentEvent = Event.current;
                        bool isCtrlPressed = currentEvent.control || currentEvent.command; // command键用于Mac
                        if (isCtrlPressed)
                        {
                            // Ctrl+点击：添加到当前选择
                            List<Object> currentSelection = new List<Object>(Selection.objects);
                            if (!currentSelection.Contains(material))
                            {
                                currentSelection.Add(material);
                                Selection.objects = currentSelection.ToArray();
                            }
                        }
                        else
                        {
                            Selection.activeObject = null;
                            Selection.activeObject = material;
                            EditorGUIUtility.PingObject(material);
                        }
                    }
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.LabelField($"{i + 1}", style.normalfont, GUILayout.MinWidth(25), GUILayout.MaxWidth(35));
                    // 在项目中定位按钮
                    // if (GUILayout.Button("定位"))
                    // {
                    //     EditorGUIUtility.PingObject(material);
                    // }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                
            }
            else if (_targetShader != null && !_showProgress)
            {
                EditorGUILayout.HelpBox("点击\"查找使用该着色器的材质\"按钮开始搜索", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("提示：Project中在Shader文件右键菜单 \"查找使用该着色器的材质\" 可快速载入目标着色器。", style.normalfont_Hui_Wrap);
            }
            
            EditorGUILayout.Space();
            
            // 显示Shader列表
            if (_showShaderList && _foundShaders.Count > 0)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField($"找到 {_foundShaders.Count} 个Shader：", style.subheading, GUILayout.Width(150));
                
                // 选择所有Shader按钮
                EditorGUI.BeginDisabledGroup(_foundShaders.Count == 0);
                GUI.backgroundColor = new Color(0.8f, 0.6f, 1f); // 淡紫色
                if (GUILayout.Button("选择所有Shader", style.normalButton, GUILayout.Width(120)))
                {
                    SelectAllShaders();
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();
                
                EditorGUILayout.EndHorizontal();
                
                // 根据Shader数量动态计算最小高度
                // 每个Shader项大约25像素高度，最小保持90像素，最大不超过500像素
                int dynamicShaderHeight = Mathf.Clamp(_foundShaders.Count * 23, 70, 230);
                _shaderScrollPosition = EditorGUILayout.BeginScrollView(_shaderScrollPosition, GUILayout.MinHeight(dynamicShaderHeight));
                EditorGUILayout.BeginVertical(EditorStyles.textArea, GUILayout.ExpandHeight(true));   //这里不添加MinHeight限制，避免滚动条出现
                for (int i = 0; i < _foundShaders.Count; i++)
                {
                    Shader shader = _foundShaders[i];
                    if (!shader) continue;
                    
                    EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                    
                    // 显示Shader使用次数
                    int usageCount = _shaderUsageCount.ContainsKey(shader) ? _shaderUsageCount[shader] : 0;
                    if (usageCount > 0)
                    {
                        GUI.backgroundColor = new Color(0.8f, 0.6f, 1f); // 淡紫色
                        if (GUILayout.Button("选择模型", GUILayout.Width(60)))
                        {
                            // 选中场景中使用该Shader的所有物体
                            SelectObjectsUsingShader(shader);
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.Label($"({usageCount})", style.normalfont, GUILayout.ExpandWidth(true), GUILayout.MinWidth(30), GUILayout.MaxWidth(49));
                    }
                    else
                    {
                        GUILayout.Label("", GUILayout.Width(106)); // 使用空标签保持对齐
                    }
                    
                    // 显示Shader信息
                    EditorGUILayout.ObjectField(shader, typeof(Shader), false, GUILayout.ExpandWidth(true));
                    
                    // 设置为目标Shader按钮
                    GUI.backgroundColor = Color.yellow;
                    if (GUILayout.Button("设为目标", GUILayout.Width(60)))
                    {
                        _targetShader = shader;
                        FindMaterialsByShader();
                    }
                    GUI.backgroundColor = Color.white;
                    
                    // 选择按钮
                    GUI.backgroundColor = Color.cyan;
                    if (GUILayout.Button("选择", GUILayout.Width(40)))
                    {
                        // 检查是否按住Ctrl键
                        Event currentEvent = Event.current;
                        bool isCtrlPressed = currentEvent.control || currentEvent.command; // command键用于Mac
                        if (isCtrlPressed)
                        {
                            // Ctrl+点击：添加到当前选择
                            List<Object> currentSelection = new List<Object>(Selection.objects);
                            if (!currentSelection.Contains(shader))
                            {
                                currentSelection.Add(shader);
                                Selection.objects = currentSelection.ToArray();
                            }
                        }
                        else
                        {
                            Selection.activeObject = null;
                            Selection.activeObject = shader;
                            EditorGUIUtility.PingObject(shader);
                        }
                    }
                    GUI.backgroundColor = Color.white;
                    
                    EditorGUILayout.LabelField($"{i + 1}", style.normalfont, GUILayout.MinWidth(25), GUILayout.MaxWidth(35));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
            }
            else if (_showShaderList && _foundShaders.Count == 0)
            {
                EditorGUILayout.HelpBox("没有在场景中找到任何Shader", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
        }
        

        /// 查找使用指定着色器的所有材质
        private void FindMaterialsByShader()
        {
            if (_targetShader == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个着色器", "确定");
                return;
            }
            
            // 检查是否为ShaderGraph着色器并显示警告
            if (IsShaderGraphShader(_targetShader))
            {
                bool continueSearch = EditorUtility.DisplayDialog(
                    "ShaderGraph着色器检测",
                    $"检测到您选择了ShaderGraph着色器 '{_targetShader.name}'。\n\n" +
                    "ShaderGraph着色器可能会有自定义UI创建问题，可能导致编辑器错误。\n" +
                    "是否继续搜索？",
                    "继续搜索",
                    "取消"
                );
                
                if (!continueSearch)
                {
                    return;
                }
            }
            
            _showProgress = true;
            _searchStatus = "正在搜索项目中所有材质...";
            Parent?.Repaint();
            
            // 获取项目中所有材质
            string[] materialGuids = AssetDatabase.FindAssets("t:Material");
            _foundMaterials.Clear();
            _materialUsageCount.Clear();
            _hasCachedUsageData = false;
            
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string guid = materialGuids[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                
                if (material != null && material.shader == _targetShader)
                {
                    _foundMaterials.Add(material);
                }
                
                // 更新进度显示
                if (i % 100 == 0)
                {
                    _searchStatus = $"正在搜索材质... ({i}/{materialGuids.Length}) 已找到 {_foundMaterials.Count} 个匹配材质";
                    Parent?.Repaint();
                }
            }
            
            // 预缓存材质使用数据
            if (_foundMaterials.Count > 0)
            {
                _searchStatus = "正在分析场景中材质使用情况...";
                Parent?.Repaint();
                CacheMaterialUsageData();
            }
            
            _showProgress = false;
            _searchStatus = $"搜索完成！找到 {_foundMaterials.Count} 个使用着色器 \"{_targetShader.name}\" 的材质";
            
            // 如果没有找到材质，显示提示
            if (_foundMaterials.Count == 0)
            {
                EditorUtility.DisplayDialog("搜索结果", $"没有找到使用着色器 \"{_targetShader.name}\" 的材质", "确定");
            }
            
            Parent?.Repaint();
        }
        

        /// 选择所有找到的材质
        private void SelectAllMaterials()
        {
            if (_foundMaterials.Count > 0)
            {
                Selection.objects = _foundMaterials.ToArray();
                // EditorUtility.DisplayDialog("选择完成", $"已选择 {foundMaterials.Count} 个材质", "确定");
            }
        }

        /// 从场景中选中的对象获取材质使用的Shader
        private void GetShaderFromSelectedObjects()
        {
            // 获取当前选中的游戏对象
            var selectedObjects = Selection.gameObjects;
            
            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在场景中选择一个或多个游戏对象", "确定");
                return;
            }

            // 收集所有唯一的Shader
            var uniqueShaders = new HashSet<Shader>();
            var objectNames = new List<string>();

            foreach (var obj in selectedObjects)
            {
                // 获取对象的所有Renderer组件
                var renderers = obj.GetComponentsInChildren<Renderer>(true);
                
                foreach (var renderer in renderers)
                {
                    if (!renderer) continue;
                    var materials = renderer.sharedMaterials;
                        
                    foreach (var material in materials)
                    {
                        if (!material || !material.shader) continue;
                        uniqueShaders.Add(material.shader);
                        if (!objectNames.Contains(obj.name))
                        {
                            objectNames.Add(obj.name);
                        }
                    }
                }
            }

            switch (uniqueShaders.Count)
            {
                // 处理结果
                case 0:
                    EditorUtility.DisplayDialog("结果", "在选中的对象中没有找到有效的材质或Shader", "确定");
                    return;
                case 1:
                    // 如果只有一个Shader，直接设置到targetShader
                    _targetShader = uniqueShaders.First();
                    // EditorUtility.DisplayDialog("成功",
                    //     $"已从 {objectNames.Count} 个对象中获取Shader:\n\"{targetShader.name}\"",
                    //     "确定");
                    Debug.Log($"已从 {objectNames.Count} 个对象中获取Shader:\n\"{_targetShader.name}\"");
                    break;
                default:
                {
                    // 如果有多个Shader，让用户选择
                    var shaderList = uniqueShaders.ToList();
                    var shaderNames = shaderList.Select(s => s.name).ToArray();
                
                    EditorUtility.DisplayDialog("发现多个Shader", 
                        $"在选中的对象中发现了 {shaderList.Count} 个不同的Shader，请手动选择目标Shader", 
                        "确定");
                
                    // 这里可以添加一个下拉选择框，但为了简单起见，我们只设置第一个Shader
                    // 用户可以在界面上手动选择其他Shader
                    _targetShader = shaderList[0];
                    break;
                }
            }

            // 刷新界面
            Parent?.Repaint();
        }
        

        /// 从当前选择的着色器开始搜索
        [MenuItem("Assets/查找使用该着色器的材质", false, 300)]
        private static void FindMaterialsFromSelection()
        {
            var selectedShader = Selection.activeObject as Shader;
            if (selectedShader)
            {
                // 打开 VicTools 主窗口并切换到材质查找工具
                var window = EditorWindow.GetWindow<VicToolsWindow>();
                window.Show();
                window.SetCurrentWindowByTypeName("ShaderMaterialFinder");
                
                // 获取当前活动的 ShaderMaterialFinder 实例并设置着色器
                var currentWindow = window.CurrentWindow as ShaderMaterialFinder;
                if (currentWindow == null) return;
                currentWindow._targetShader = selectedShader;
                currentWindow.FindMaterialsByShader();
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个着色器文件", "确定");
            }
        }
        

        /// 验证菜单项是否可用
        [MenuItem("Assets/查找使用该着色器的材质", true, 300)]
        private static bool ValidateFindMaterialsFromSelection()
        {
            return Selection.activeObject is Shader;
        }
        

        /// 从当前选择的材质查找使用的着色器
        [MenuItem("Assets/查找该材质使用的着色器", false, 301)]
        private static void FindShaderFromMaterial()
        {
            Material selectedMaterial = Selection.activeObject as Material;
            // 打开 VicTools 主窗口并切换到材质查找工具
            VicToolsWindow window = EditorWindow.GetWindow<VicToolsWindow>();
            window.Show();
            window.SetCurrentWindowByTypeName("ShaderMaterialFinder");
            // 获取当前活动的 ShaderMaterialFinder 实例并设置着色器
            var currentWindow = window.CurrentWindow as ShaderMaterialFinder;
            if (selectedMaterial && selectedMaterial.shader)
            {
                if (currentWindow != null)
                {
                    currentWindow._targetShader = selectedMaterial.shader;
                    // EditorGUIUtility.PingObject(selectedMaterial.shader);   //跳转到着色器位置
                    currentWindow.FindMaterialsByShader();
                }

                Debug.Log($"材质 \"{selectedMaterial.name}\" 使用的着色器：{selectedMaterial.shader.name}");
                // EditorUtility.DisplayDialog("着色器信息", 
                //     $"材质 \"{selectedMaterial.name}\" 使用的着色器：\n{selectedMaterial.shader.name}", 
                //     "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个材质文件", "确定");
            }
        }
        

        /// 验证菜单项是否可用
        [MenuItem("Assets/查找该材质使用的着色器", true, 301)]
        private static bool ValidateFindShaderFromMaterial()
        {
            return Selection.activeObject is Material;
        }

        /// 缓存材质使用数据
        private void CacheMaterialUsageData()
        {
            _materialUsageCount.Clear();
            
            // 获取场景中所有Renderer组件
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            
            // 创建材质到使用数量的映射
            foreach (var renderer in allRenderers)
            {
                if (!renderer || renderer.sharedMaterials == null) continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || !_foundMaterials.Contains(material)) continue;
                    if (!_materialUsageCount.TryAdd(material, 1))
                    {
                        _materialUsageCount[material]++;
                    }
                }
            }
            
            // 对于没有在场景中使用的材质，设置使用数量为0
            foreach (var material in _foundMaterials.Where(material => !_materialUsageCount.ContainsKey(material)))
            {
                _materialUsageCount[material] = 0;
            }
            
            _hasCachedUsageData = true;
        }

        /// 选择场景中使用指定材质的所有物体
        private void SelectObjectsUsingMaterial(Material material, bool selObj = false)
        {
            if (!material)
            {
                return;
            }

            // 如果已经缓存了使用数据，直接返回缓存的数量
            if (_hasCachedUsageData && _materialUsageCount.TryGetValue(material, out var count))
            {
                if (!selObj || count <= 0) return;
                // 需要实际选择物体时，执行完整的查找
                var objectsUsingMaterial = FindObjectsUsingMaterial(material);
                if (objectsUsingMaterial.Count <= 0) return;
                Selection.objects = objectsUsingMaterial.ToArray();
                _selectCount = objectsUsingMaterial.Count;
                        
                // 更新ScenesTools的selectedObjectsCount
                UpdateScenesToolsSelectedCount(objectsUsingMaterial.Count);

                return;
            }

            // 如果没有缓存，执行完整的查找
            var objectsUsingMaterialFull = FindObjectsUsingMaterial(material);

            if (objectsUsingMaterialFull.Count <= 0 || !selObj) return;
            Selection.objects = objectsUsingMaterialFull.ToArray();
            _selectCount = objectsUsingMaterialFull.Count;
                
            // 更新ScenesTools的selectedObjectsCount
            UpdateScenesToolsSelectedCount(objectsUsingMaterialFull.Count);
        }

        /// 查找场景中使用指定材质的所有物体
        private static List<GameObject> FindObjectsUsingMaterial(Material material)
        {
            // 获取场景中所有Renderer组件
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            return (from renderer in allRenderers where renderer && renderer.sharedMaterials != null where renderer.sharedMaterials.Any(mat => mat == material) select renderer.gameObject).ToList();
        }

        /// 更新全局选中对象数量
        private void UpdateScenesToolsSelectedCount(int count)
        {
            // 获取VicToolsWindow实例并更新全局选中数量
            var mainWindow = Parent as VicToolsWindow;
            if (!mainWindow) return;
            // 更新全局选中数量字段
            mainWindow.globalSelectedObjectsCount = count;
            // 强制重绘窗口以更新显示
            mainWindow.Repaint();
            Debug.Log($"已更新全局选中数量: {count}");
        }

        /// 检测是否为ShaderGraph着色器
        private static bool IsShaderGraphShader(Shader shader)
        {
            if (!shader) return false;
            
            // 方法1：检查着色器名称是否包含ShaderGraph相关关键词
            var shaderName = shader.name.ToLower();
            if (shaderName.Contains("shadergraph") || 
                shaderName.Contains("graph") ||
                shaderName.Contains("syntystudios")) // 针对用户反馈的SyntyStudios着色器
            {
                return true;
            }
            
            // 方法2：检查着色器描述（如果有）
            var shaderDescription = shader.name; // Unity着色器没有直接的描述属性
            // 但我们可以通过其他方式判断
            
            // 方法3：检查着色器文件路径（如果可用）
            var shaderPath = AssetDatabase.GetAssetPath(shader);
            return !string.IsNullOrEmpty(shaderPath) && 
                   (shaderPath.Contains("ShaderGraph") || shaderPath.Contains("Shaders/Graph"));
        }

        /// 查找场景中所有物体使用的Shader
        private void FindAllShadersInScene()
        {
            _showProgress = true;
            _searchStatus = "正在搜索场景中所有物体使用的Shader...";
            Parent?.Repaint();
            
            _foundShaders.Clear();
            _shaderUsageCount.Clear();
            
            // 获取场景中所有Renderer组件
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var uniqueShaders = new HashSet<Shader>();
            
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var renderer = allRenderers[i];
                if (!renderer || renderer.sharedMaterials == null) continue;
                
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || !material.shader) continue;
                    
                    var shader = material.shader;
                    uniqueShaders.Add(shader);
                    
                    // 统计Shader使用次数
                    if (!_shaderUsageCount.TryAdd(shader, 1))
                    {
                        _shaderUsageCount[shader]++;
                    }
                }
                
                // 更新进度显示
                if (i % 100 == 0)
                {
                    _searchStatus = $"正在分析场景物体... ({i}/{allRenderers.Length}) 已找到 {uniqueShaders.Count} 个不同的Shader";
                    Parent?.Repaint();
                }
            }
            
            // 将HashSet中的Shader添加到列表并排序（按使用次数降序）
            _foundShaders.Clear();
            _foundShaders.AddRange(uniqueShaders);
            _foundShaders.Sort((a, b) => 
            {
                int countA = _shaderUsageCount.ContainsKey(a) ? _shaderUsageCount[a] : 0;
                int countB = _shaderUsageCount.ContainsKey(b) ? _shaderUsageCount[b] : 0;
                return countB.CompareTo(countA); // 降序排序
            });
            
            _showProgress = false;
            _showShaderList = true;
            _searchStatus = $"搜索完成！找到 {_foundShaders.Count} 个不同的Shader在场景中使用";
            
            // 如果没有找到Shader，显示提示
            if (_foundShaders.Count == 0)
            {
                EditorUtility.DisplayDialog("搜索结果", "没有在场景中找到任何Shader", "确定");
            }
            
            Parent?.Repaint();
        }

        /// 选择所有找到的Shader
        private void SelectAllShaders()
        {
            if (_foundShaders.Count > 0)
            {
                Selection.objects = _foundShaders.ToArray();
            }
        }

        /// 选择场景中使用指定Shader的所有物体
        private void SelectObjectsUsingShader(Shader shader)
        {
            if (!shader) return;
            
            var objectsUsingShader = new List<GameObject>();
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            
            foreach (var renderer in allRenderers)
            {
                if (!renderer || renderer.sharedMaterials == null) continue;
                
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || material.shader != shader) continue;
                    
                    objectsUsingShader.Add(renderer.gameObject);
                    break; // 同一个Renderer可能使用多个材质，但我们已经找到了一个使用该Shader的材质
                }
            }
            
            if (objectsUsingShader.Count > 0)
            {
                Selection.objects = objectsUsingShader.ToArray();
                _selectCount = objectsUsingShader.Count;
                UpdateScenesToolsSelectedCount(objectsUsingShader.Count);
            }
        }
    }
}

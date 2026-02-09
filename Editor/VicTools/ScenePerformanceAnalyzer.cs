using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace VicTools
{
    /// <summary>
    /// 场景性能分析器 - 提供详细的场景性能分析报告
    /// </summary>
    public class ScenePerformanceAnalyzer : SubWindow
    {
        // 性能数据
        private PerformanceData _performanceData;
        private Vector2 _scrollPosition;
        private bool _autoRefresh = true;
        private float _lastRefreshTime;
        private const float RefreshInterval = 2.0f; // 自动刷新间隔（秒）

        // 分类统计
        private readonly Dictionary<string, int> _objectCountByType = new();
        private readonly Dictionary<string, long> _memoryUsageByType = new();
        private readonly Dictionary<string, List<GameObject>> _objectsByType = new();
        private readonly Dictionary<string, List<GameObject>> _componentObjects = new();
        
        // 资源统计
        private readonly List<Material> _allMaterials = new();
        private readonly List<Texture> _allTextures = new();
        
        // 渲染统计
        private int _totalTriangles;
        private int _totalVertices;
        private long _totalTextureMemory;
        
        // 性能警告
        private readonly List<PerformanceWarning> _performanceWarnings = new List<PerformanceWarning>();
        
        // 全局光照检查相关
        private readonly List<GameObject> _contributeGIObjects = new();
        private readonly List<GameObject> _receiveGIObjects = new();
        private readonly List<GameObject> _staticBatchingObjects = new();
        private readonly Dictionary<string, List<GameObject>> _giObjectsByType = new();

        // 资源利用率检查相关
        private readonly List<string> _unusedResources = new();
        private readonly Dictionary<string, List<string>> _unusedResourcesByType = new();
        private readonly Dictionary<string, bool> _resourceTypeFilters = new(); // 资源类型筛选状态
        private bool _isScanningResources = false;
        private Vector2 _resourceScrollPosition;
        private System.DateTime _lastResourceScanTime = System.DateTime.MinValue;
        private bool _isSingleSelectMode = false; // 单选模式状态
        
        // 用户可配置的排除列表
        private readonly List<string> _excludedPaths = new(); // 排除的路径列表
        private readonly List<string> _excludedPatterns = new(); // 排除的模式（通配符）
        private bool _showExclusionSettings = false; // 是否显示排除设置
        private Vector2 _exclusionScrollPosition;
        private string _newExclusionPath = ""; // 新排除路径输入
        private string _newExclusionPattern = ""; // 新排除模式输入
        
        // 资源使用情况缓存 - 智能缓存系统
        private readonly Dictionary<string, ResourceCacheEntry> _resourceUsageCache = new();
        internal const float CACHE_EXPIRY_TIME = 300f; // 缓存过期时间（秒）- 5分钟
        private const float CACHE_CLEANUP_INTERVAL = 60f; // 缓存清理间隔（秒）
        private float _lastCacheCleanupTime = 0f;

        // 模块显示开关
        private bool _showSelectedObjectInfo = true;
        private bool _showSceneInfo = true;
        private bool _showMemoryUsage = true;
        private bool _showObjectStatistics = true;
        private bool _showGlobalIllumination = true;
        private bool _showPerformanceWarnings = true;
        private bool _showDetailedStatisticsSection = true;
        private bool _showResourceUtilization = true;

        
        public ScenePerformanceAnalyzer(string name, EditorWindow parent) : base(VicToolsConfig.PerformanceAnalyzerWindowName, parent)
        {
            _performanceData = new PerformanceData();
        }

        // ReSharper disable Unity.PerformanceAnalysis
        public override void OnEnable()
        {
            base.OnEnable();
            // 加载模块显示开关的存档设置
            LoadModuleToggleStates();
            // 加载排除列表设置
            LoadExclusionSettings();
            RefreshPerformanceData();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            // 保存模块显示开关的存档设置
            SaveModuleToggleStates();
        }

        public override void OnFocus()
        {
            base.OnFocus();
        }

        public override void OnLostFocus()
        {
            base.OnLostFocus();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
        }

        public override void OnHierarchyChange()
        {
            base.OnHierarchyChange();
            // 当场景层级发生变化时，可以触发性能数据刷新
            if (_autoRefresh)
            {
                RefreshPerformanceData();
            }
        }

        // ReSharper disable Unity.PerformanceAnalysis
        public override void OnGUI()
        {
            var style = EditorStyle.Get;

            // 获取主窗口宽度用于布局
            var windowWidth = Parent ? Parent.position.width : 460;
            var contentWidth = Mathf.Max(windowWidth - 30, 200); // 减少边距到20，最小宽度260

            // 主容器 - 设置精确宽度避免滚动条
            EditorGUILayout.BeginVertical(style.area, GUILayout.Width(contentWidth));
            
            // 标题行 - 精确宽度控制
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("★ 场景性能分析报告", style.subheading, GUILayout.Width(contentWidth - 190));
            
            // 自动刷新开关
            _autoRefresh = base.CreateToggleWithStyle("自动刷新", _autoRefresh, 
                (newValue) => {
                    _autoRefresh = newValue;
                }, null, null, null, 60);
            
            // 手动刷新按钮
            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("刷新数据", style.normalButton, GUILayout.Width(80)))
            {
                RefreshPerformanceData();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.EndHorizontal();
            
            DrawModuleToggleControls(style, contentWidth);

            // 自动刷新逻辑
            if (_autoRefresh && (Time.realtimeSinceStartup - _lastRefreshTime) > RefreshInterval)
            {
                RefreshPerformanceData();
            }

            // 滚动视图 - 精确宽度控制，禁用水平滚动
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUIStyle.none, GUIStyle.none);
            // EditorGUILayout.BeginVertical();
            // 选中对象三角面数信息
            if (_showSelectedObjectInfo)
                DrawSelectedObjectTriangleInfo(style.area, style.subheading, style.normalfont, contentWidth);

            // 场景基本信息
            if (_showSceneInfo)
                DrawSceneInfoSection(style.area, style.subheading, style.normalfont, contentWidth);
            
            // 渲染管线信息
            // DrawRenderPipelineSection(style.area, style.subheading, style.normalfont, contentWidth);
            
            // 内存使用情况
            if (_showMemoryUsage)
                DrawMemoryUsageSection(style.area, style.subheading, style.normalfont, contentWidth);
            
            // 对象统计
            if (_showObjectStatistics)
                DrawObjectStatisticsSection(style.area, style.subheading, style.normalfont, contentWidth);
            
            // 全局光照检查
            if (_showGlobalIllumination)
                DrawGlobalIlluminationSection(style.area, style.subheading, style.normalfont, contentWidth);

            // 性能警告
            if (_showPerformanceWarnings)
                DrawPerformanceWarningsSection(style.area, style.subheading, style.normalfont, contentWidth);
            
            // 资源利用率检查
            if (_showResourceUtilization)
                DrawResourceUtilizationSection(style.area, style.subheading, style.normalfont, contentWidth);
            
            // 详细统计和三角面数信息 - 水平布局
            // EditorGUILayout.BeginHorizontal(GUILayout.Width(400));
            // 详细统计
            if (_showDetailedStatisticsSection)
                DrawDetailedStatisticsSection(style.area, style.subheading, style.normalfont, contentWidth);
            
            // EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
            // EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        /// 绘制模块开关控制
        private void DrawModuleToggleControls(EditorStyle style, float contentWidth)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            // EditorGUILayout.LabelField("模块显示控制", style.subheading);

            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));

            // 第一行开关
            _showSelectedObjectInfo = CreateToggleWithStyle("选中对象信息", _showSelectedObjectInfo,
                (newValue) => { _showSelectedObjectInfo = newValue; }, null, null, null, 90);

            _showSceneInfo = CreateToggleWithStyle("场景基本信息", _showSceneInfo,
                (newValue) => { _showSceneInfo = newValue; }, null, null, null, 90, 20);

            _showMemoryUsage = CreateToggleWithStyle("内存使用情况", _showMemoryUsage,
                (newValue) => { _showMemoryUsage = newValue; }, null, null, null, 90, 20);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));

            // 第二行开关
            _showObjectStatistics = CreateToggleWithStyle("对象统计", _showObjectStatistics,
                (newValue) => { _showObjectStatistics = newValue; }, null, null, null, 60, 20);

            _showGlobalIllumination = CreateToggleWithStyle("全局光照检查", _showGlobalIllumination,
                (newValue) => { _showGlobalIllumination = newValue; }, null, null, null, 90, 20);

            _showPerformanceWarnings = CreateToggleWithStyle("性能警告", _showPerformanceWarnings,
                (newValue) => { _showPerformanceWarnings = newValue; }, null, null, null, 60, 20);
            
            _showDetailedStatisticsSection = CreateToggleWithStyle("详细统计", _showDetailedStatisticsSection,
                (newValue) => { _showDetailedStatisticsSection = newValue; }, null, null, null, 60, 20);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));

            // 第三行开关 - 资源利用率检查
            _showResourceUtilization = CreateToggleWithStyle("资源利用率检查", _showResourceUtilization,
                (newValue) => { _showResourceUtilization = newValue; }, null, null, null, 100, 20);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        /// <summary>
        /// 刷新性能数据
        /// </summary>
        private void RefreshPerformanceData()
        {
            _performanceData = new PerformanceData();
            _objectCountByType.Clear();
            _memoryUsageByType.Clear();
            _objectsByType.Clear();
            _componentObjects.Clear();
            _performanceWarnings.Clear();
            
            _allMaterials.Clear();
            _allTextures.Clear();
            
            _totalTriangles = 0;
            _totalVertices = 0;
            _totalTextureMemory = 0;

            CollectSceneData();
            AnalyzePerformance();
            _lastRefreshTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 收集场景数据
        /// </summary>
        private void CollectSceneData()
        {
            var allGameObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            _performanceData.totalObjects = allGameObjects.Length;

            var uniqueMaterials = new HashSet<Material>();
            var uniqueTextures = new HashSet<Texture>();

            // 清空全局光照相关数据
            _contributeGIObjects.Clear();
            _receiveGIObjects.Clear();
            _staticBatchingObjects.Clear();
            _giObjectsByType.Clear();
            _performanceData.contributeGICount = 0;

            foreach (var gameObject in allGameObjects)
            {
                var objectType = GetObjectType(gameObject);
                
                // 统计对象类型数量
                if (!_objectCountByType.TryAdd(objectType, 1))
                    _objectCountByType[objectType]++;

                // 存储对象引用以便交互选择
                if (!_objectsByType.ContainsKey(objectType))
                    _objectsByType[objectType] = new List<GameObject>();
                _objectsByType[objectType].Add(gameObject);

                // 收集渲染数据
                var sceneDataRenderer = gameObject.GetComponent<Renderer>();
                if (sceneDataRenderer)
                {
                    var meshFilter = gameObject.GetComponent<MeshFilter>();
                    if (meshFilter && meshFilter.sharedMesh)
                    {
                        _totalTriangles += meshFilter.sharedMesh.triangles.Length / 3;
                        _totalVertices += meshFilter.sharedMesh.vertexCount;
                    }

                    // 收集材质
                    foreach (var material in sceneDataRenderer.sharedMaterials)
                    {
                        if (material)
                        {
                            uniqueMaterials.Add(material);
                        }
                    }

                    // 检查全局光照设置
                    CheckGlobalIlluminationSettings(gameObject, sceneDataRenderer);
                }

                // 特殊组件统计
                if (gameObject.GetComponent<Light>())
                {
                    _performanceData.lightCount++;
                    AddComponentObject("Light", gameObject);
                }
                if (gameObject.GetComponent<ParticleSystem>())
                {
                    _performanceData.particleSystemCount++;
                    AddComponentObject("ParticleSystem", gameObject);
                }
                if (gameObject.GetComponent<Camera>())
                {
                    _performanceData.cameraCount++;
                    AddComponentObject("Camera", gameObject);
                }
                if (gameObject.GetComponent<Collider>())
                {
                    _performanceData.colliderCount++;
                    AddComponentObject("Collider", gameObject);
                }
                if (gameObject.GetComponent<Rigidbody>())
                {
                    _performanceData.rigidbodyCount++;
                    AddComponentObject("Rigidbody", gameObject);
                }

                if (!gameObject.GetComponent<AudioSource>()) continue;
                _performanceData.audioSourceCount++;
                AddComponentObject("AudioSource", gameObject);
            }

            // 收集所有纹理并计算内存（避免重复计算）
            foreach (var material in uniqueMaterials)
            {
                if (!material) continue;
                var shader = material.shader;
                if (!shader) continue;
                for (var i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    var texture = material.GetTexture(ShaderUtil.GetPropertyName(shader, i));
                    if (texture)
                    {
                        uniqueTextures.Add(texture);
                    }
                }
            }

            // 计算纹理内存（只计算一次，避免重复）
            foreach (var texture in uniqueTextures)
            {
                if (texture is Texture2D tex2D)
                {
                    _totalTextureMemory += EstimateTextureMemory(tex2D);
                }
            }

            _performanceData.meshCount = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Length;
            _performanceData.materialCount = uniqueMaterials.Count;
            _performanceData.textureCount = uniqueTextures.Count;
            _performanceData.totalTriangles = _totalTriangles;
            _performanceData.totalVertices = _totalVertices;
            _performanceData.textureMemory = _totalTextureMemory;
            
            // 存储材质和纹理引用以便交互选择
            _allMaterials.AddRange(uniqueMaterials);
            _allTextures.AddRange(uniqueTextures);
        }

        /// <summary>
        /// 检查全局光照设置
        /// </summary>
        private void CheckGlobalIlluminationSettings(GameObject gameObject, Renderer renderer)
        {
            var isContributeGI = false;
            var isReceiveGI = false;
            
            // 使用GameObjectUtility.GetStaticEditorFlags获取精确的静态标志信息
            #if UNITY_EDITOR
            try
            {
                // 在编辑器模式下，使用最精确的方法获取静态标志
                var flags = GameObjectUtility.GetStaticEditorFlags(gameObject);
                if ((flags & StaticEditorFlags.ContributeGI) != 0)
                {
                    isContributeGI = true;
                }
                if ((flags & StaticEditorFlags.ReflectionProbeStatic) != 0)
                {
                    isReceiveGI = true;
                }
            }
            catch (System.Exception ex)
            {
                // 如果获取静态标志失败，记录警告但不使用备用方法
                // 因为启发式方法可能不准确
                Debug.LogWarning($"获取对象 {gameObject.name} 的静态标志失败: {ex.Message}");
            }
            #endif
            
            // 记录ContributeGI对象（只有通过精确方法确认的）
            if (isContributeGI)
            {
                _contributeGIObjects.Add(gameObject);
                _performanceData.contributeGICount++;
                
                // 按类型分类
                var objectType = GetObjectType(gameObject);
                if (!_giObjectsByType.ContainsKey(objectType))
                    _giObjectsByType[objectType] = new List<GameObject>();
                _giObjectsByType[objectType].Add(gameObject);
                
                // 添加到组件对象以便选择
                AddComponentObject("ContributeGI", gameObject);
            }
            
            // 记录ReceiveGI对象（只有通过精确方法确认的）
            if (isReceiveGI)
            {
                _receiveGIObjects.Add(gameObject);
                AddComponentObject("ReceiveGI", gameObject);
            }
            
            // 检查静态批处理
            if (!gameObject.isStatic || renderer.allowOcclusionWhenDynamic != false) return;
            _staticBatchingObjects.Add(gameObject);
            AddComponentObject("StaticBatching", gameObject);
        }

        /// <summary>
        /// 分析性能问题
        /// </summary>
        private void AnalyzePerformance()
        {
            // 检查灯光数量
            if (_performanceData.lightCount > 8)
            {
                _performanceWarnings.Add(new PerformanceWarning(
                    "灯光数量过多",
                    $"当前场景有 {_performanceData.lightCount} 个灯光，建议控制在8个以内以获得最佳性能",
                    PerformanceWarningLevel.Warning
                ));
            }

            // 检查粒子系统数量
            if (_performanceData.particleSystemCount > 20)
            {
                _performanceWarnings.Add(new PerformanceWarning(
                    "粒子系统过多",
                    $"当前场景有 {_performanceData.particleSystemCount} 个粒子系统，可能影响性能",
                    PerformanceWarningLevel.Warning
                ));
            }

            // 检查三角形数量
            if (_performanceData.totalTriangles > 1000000)
            {
                _performanceWarnings.Add(new PerformanceWarning(
                    "三角形数量过多",
                    $"当前场景有 {_performanceData.totalTriangles:N0} 个三角形，建议优化模型细节",
                    PerformanceWarningLevel.Warning
                ));
            }

            // 检查纹理内存
            if (_performanceData.textureMemory > 100 * 1024 * 1024) // 100MB
            {
                _performanceWarnings.Add(new PerformanceWarning(
                    "纹理内存占用过高",
                    $"纹理内存占用: {FormatMemorySize(_performanceData.textureMemory)}，建议压缩纹理",
                    PerformanceWarningLevel.Warning
                ));
            }

            // 检查材质数量
            if (_performanceData.materialCount > 100)
            {
                _performanceWarnings.Add(new PerformanceWarning(
                    "材质数量过多",
                    $"当前场景有 {_performanceData.materialCount} 个材质，建议合并材质批次",
                    PerformanceWarningLevel.Info
                ));
            }

            // 检查全局光照贡献对象数量
            if (_performanceData.contributeGICount > 50)
            {
                _performanceWarnings.Add(new PerformanceWarning(
                    "全局光照贡献对象过多",
                    $"当前场景有 {_performanceData.contributeGICount} 个对象开启了ContributeGlobalIllumination，可能影响光照烘焙性能",
                    PerformanceWarningLevel.Warning
                ));
            }
        }

        /// <summary>
        /// 绘制场景信息部分
        /// </summary>
        private void DrawSceneInfoSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            EditorGUILayout.BeginVertical(areaStyle);
            EditorGUILayout.LabelField("场景基本信息", subheadingStyle);
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("场景名称:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, normalStyle);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("总对象数:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(_performanceData.totalObjects.ToString("N0"), normalStyle);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("更新时间:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(System.DateTime.Now.ToString("HH:mm:ss"), normalStyle);
            EditorGUILayout.EndHorizontal();

            var renderPipeline = "内置渲染管线";
            #if UNITY_2019_3_OR_NEWER
            try
            {
                // 安全地获取渲染管线信息，避免访问可能引起断言失败的对象
                var currentPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (currentPipeline && !currentPipeline.Equals(null))
                {
                    renderPipeline = currentPipeline.GetType().Name;
                }
            }
            catch (System.Exception)
            {
                // 如果获取渲染管线信息失败，保持默认值
                renderPipeline = "内置渲染管线";
            }
            #endif
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("渲染管线:", normalStyle, GUILayout.Width(80));
            GUI.contentColor = Color.yellow;
            EditorGUILayout.LabelField(renderPipeline, normalStyle);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("目标帧率:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(Application.targetFrameRate == -1 ? "无限制" : Application.targetFrameRate.ToString(), normalStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 绘制渲染管线信息部分
        /// </summary>
        private void DrawRenderPipelineSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            EditorGUILayout.BeginVertical(areaStyle);
            // EditorGUILayout.LabelField("渲染管线信息", subheadingStyle);
            
            string renderPipeline = "内置渲染管线";
            #if UNITY_2019_3_OR_NEWER
            try
            {
                // 安全地获取渲染管线信息，避免访问可能引起断言失败的对象
                var currentPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (currentPipeline != null && !currentPipeline.Equals(null))
                {
                    renderPipeline = currentPipeline.GetType().Name;
                }
            }
            catch (System.Exception)
            {
                // 如果获取渲染管线信息失败，保持默认值
                renderPipeline = "内置渲染管线";
            }
            #endif
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("渲染管线:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(renderPipeline, normalStyle);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("目标帧率:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(Application.targetFrameRate == -1 ? "无限制" : Application.targetFrameRate.ToString(), normalStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 绘制对象统计部分
        /// </summary>
        private void DrawObjectStatisticsSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            EditorGUILayout.BeginVertical(areaStyle);
            EditorGUILayout.LabelField("对象统计 (点击数字选择对象)", subheadingStyle);
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("网格:", normalStyle, GUILayout.Width(50));
            DrawSelectableCount("MeshRenderer", _performanceData.meshCount.ToString("N0"));
            
            EditorGUILayout.LabelField("材质:", normalStyle, GUILayout.Width(40));
            DrawSelectableCount("Material", _performanceData.materialCount.ToString("N0"));
            
            EditorGUILayout.LabelField("纹理:", normalStyle, GUILayout.Width(40));
            DrawSelectableCount("Texture", _performanceData.textureCount.ToString("N0"));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("灯光:", normalStyle, GUILayout.Width(50));
            DrawSelectableCount("Light", _performanceData.lightCount.ToString("N0"));
            
            EditorGUILayout.LabelField("相机:", normalStyle, GUILayout.Width(40));
            DrawSelectableCount("Camera", _performanceData.cameraCount.ToString("N0"));
            
            EditorGUILayout.LabelField("粒子:", normalStyle, GUILayout.Width(40));
            DrawSelectableCount("ParticleSystem", _performanceData.particleSystemCount.ToString("N0"));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("碰撞体:", normalStyle, GUILayout.Width(50));
            DrawSelectableCount("Collider", _performanceData.colliderCount.ToString("N0"));
            
            EditorGUILayout.LabelField("刚体:", normalStyle, GUILayout.Width(40));
            DrawSelectableCount("Rigidbody", _performanceData.rigidbodyCount.ToString("N0"));
            
            EditorGUILayout.LabelField("音频:", normalStyle, GUILayout.Width(40));
            DrawSelectableCount("AudioSource", _performanceData.audioSourceCount.ToString("N0"));
            EditorGUILayout.EndHorizontal();
            
            // 添加快速选择按钮
            EditorGUILayout.Space();
            DrawQuickSelectionButtons(contentWidth);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// 绘制快速选择按钮
        private void DrawQuickSelectionButtons(float contentWidth)
        {
            var style = EditorStyle.Get;
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("快速选择:", style.normalfont, GUILayout.Width(80));
            
            // 选择所有网格对象
            bool hasMeshObjects = HasObjectsOfType("MeshRenderer");
            GUI.enabled = hasMeshObjects;
            if (GUILayout.Button("所有网格", style.normalButton, GUILayout.Width(80)))
            {
                SelectAllObjectsOfType("MeshRenderer");
            }
            GUI.enabled = true;
            
            // 选择所有灯光
            bool hasLightObjects = HasObjectsOfType("Light");
            GUI.enabled = hasLightObjects;
            if (GUILayout.Button("所有灯光", style.normalButton, GUILayout.Width(80)))
            {
                SelectAllObjectsOfType("Light");
            }
            GUI.enabled = true;
            
            // 选择所有粒子系统
            bool hasParticleObjects = HasObjectsOfType("ParticleSystem");
            GUI.enabled = hasParticleObjects;
            if (GUILayout.Button("所有粒子", style.normalButton, GUILayout.Width(80)))
            {
                SelectAllObjectsOfType("ParticleSystem");
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("", GUILayout.Width(80)); // 占位符
            // 选择所有碰撞体
            bool hasColliderObjects = HasObjectsOfType("Collider");
            GUI.enabled = hasColliderObjects;
            if (GUILayout.Button("所有碰撞体", style.normalButton, GUILayout.Width(80)))
            {
                SelectAllObjectsOfType("Collider");
            }
            GUI.enabled = true;
            
            // 选择所有刚体
            bool hasRigidbodyObjects = HasObjectsOfType("Rigidbody");
            GUI.enabled = hasRigidbodyObjects;
            if (GUILayout.Button("所有刚体", style.normalButton, GUILayout.Width(80)))
            {
                SelectAllObjectsOfType("Rigidbody");
            }
            GUI.enabled = true;
            
            // 选择所有音频源
            bool hasAudioSourceObjects = HasObjectsOfType("AudioSource");
            GUI.enabled = hasAudioSourceObjects;
            if (GUILayout.Button("所有音频", style.normalButton, GUILayout.Width(80)))
            {
                SelectAllObjectsOfType("AudioSource");
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            // 全局光照选择按钮已移至专门的全局光照操作区域
        }

        /// 检查指定类型的对象是否存在
        private bool HasObjectsOfType(string objectType)
        {
            bool hasObjects = (_objectsByType.ContainsKey(objectType) && _objectsByType[objectType].Count > 0) ||
                             (_componentObjects.ContainsKey(objectType) && _componentObjects[objectType].Count > 0);
            return hasObjects;
        }

        /// 选择指定类型的所有对象
        private void SelectAllObjectsOfType(string objectType)
        {
            List<GameObject> allObjects = new List<GameObject>();
            
            if (_objectsByType.ContainsKey(objectType))
                allObjects.AddRange(_objectsByType[objectType]);
            if (_componentObjects.ContainsKey(objectType))
                allObjects.AddRange(_componentObjects[objectType]);
            
            allObjects = allObjects.Distinct().ToList();
            
            if (allObjects.Count > 0)
            {
                Selection.objects = allObjects.ToArray();
                EditorGUIUtility.PingObject(allObjects[0]);
                Debug.Log($"已选择 {allObjects.Count} 个 {objectType} 对象");
            }
            else
            {
                Debug.Log($"场景中没有找到 {objectType} 对象");
            }
        }

        /// 绘制内存使用情况部分
        private void DrawMemoryUsageSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            EditorGUILayout.BeginVertical(areaStyle);
            EditorGUILayout.LabelField("内存使用情况", subheadingStyle);
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("三角形:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(_performanceData.totalTriangles.ToString("N0"), normalStyle);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("顶点:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(_performanceData.totalVertices.ToString("N0"), normalStyle);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("纹理内存:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(FormatMemorySize(_performanceData.textureMemory), normalStyle);
            EditorGUILayout.EndHorizontal();
            
            // 系统内存信息
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("系统内存:", normalStyle, GUILayout.Width(80));
            EditorGUILayout.LabelField(FormatMemorySize(System.GC.GetTotalMemory(false)), normalStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// 绘制性能警告部分
        private void DrawPerformanceWarningsSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            if (_performanceWarnings.Count == 0)
                return;

            EditorGUILayout.BeginVertical(areaStyle);
            GUI.contentColor = Color.red;
            EditorGUILayout.LabelField("性能警告", subheadingStyle);
            GUI.contentColor = Color.white;
            foreach (var warning in _performanceWarnings)
            {
                Color warningColor = GetWarningColor(warning.level);
                GUI.contentColor = warningColor;

                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(warning.level.ToString() + ":", normalStyle, GUILayout.Width(60));
                GUILayout.FlexibleSpace();
                // 添加快速修复按钮
                if (warning.level == PerformanceWarningLevel.Warning || warning.level == PerformanceWarningLevel.Critical)
                {
                    if (GUILayout.Button("选择相关对象", GUILayout.Width(100)))
                    {
                        SelectObjectsForWarning(warning);
                    }
                }
                EditorGUILayout.EndHorizontal();
                GUIStyle customLabelStyle = new GUIStyle(EditorStyles.label);
                customLabelStyle.fontSize = 14;
                customLabelStyle.wordWrap = true;
                EditorGUILayout.LabelField(warning.message, customLabelStyle);
                EditorGUILayout.EndVertical();
                
                GUI.contentColor = Color.white;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// 根据性能警告选择相关对象
        private void SelectObjectsForWarning(PerformanceWarning warning)
        {
            List<GameObject> objectsToSelect = new List<GameObject>();
            
            if (warning.title.Contains("灯光"))
            {
                objectsToSelect.AddRange(GetAllObjectsOfType("Light"));
            }
            else if (warning.title.Contains("粒子系统"))
            {
                objectsToSelect.AddRange(GetAllObjectsOfType("ParticleSystem"));
            }
            else if (warning.title.Contains("三角形"))
            {
                // 选择三角形数量最多的网格对象
                var meshObjects = GetAllObjectsOfType("MeshRenderer");
                var skinnedMeshObjects = GetAllObjectsOfType("SkinnedMeshRenderer");
                var allMeshObjects = meshObjects.Concat(skinnedMeshObjects).ToList();
                
                // 按三角形数量排序并选择前10个
                var sortedObjects = allMeshObjects
                    .Where(obj => {
                        var meshFilter = obj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                            return meshFilter.sharedMesh.triangles.Length / 3 > 1000;
                        return false;
                    })
                    .OrderByDescending(obj => {
                        var meshFilter = obj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                            return meshFilter.sharedMesh.triangles.Length / 3;
                        return 0;
                    })
                    .Take(10)
                    .ToList();
                
                objectsToSelect.AddRange(sortedObjects);
            }
            else if (warning.title.Contains("纹理内存"))
            {
                // 选择使用大纹理的对象
                var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                var objectsWithLargeTextures = allObjects
                    .Where(obj => {
                        var warningRenderer = obj.GetComponent<Renderer>();
                        if (!warningRenderer) return false;
                        foreach (var material in warningRenderer.sharedMaterials)
                        {
                            if (!material) continue;
                            var shader = material.shader;
                            if (!shader) continue;
                            for (var i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
                            {
                                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                                    continue;
                                var texture = material.GetTexture(ShaderUtil.GetPropertyName(shader, i));
                                if (texture is Texture2D tex2D && (tex2D.width > 1024 || tex2D.height > 1024))
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    })
                    .Take(10)
                    .ToList();
                
                objectsToSelect.AddRange(objectsWithLargeTextures);
            }
            else if (warning.title.Contains("全局光照贡献对象"))
            {
                // 选择所有贡献GI的对象
                objectsToSelect.AddRange(_contributeGIObjects);
            }
            
            if (objectsToSelect.Count > 0)
            {
                Selection.objects = objectsToSelect.ToArray();
                EditorGUIUtility.PingObject(objectsToSelect[0]);
                Debug.Log($"已选择 {objectsToSelect.Count} 个与警告相关的对象");
            }
            else
            {
                Debug.Log("未找到与警告相关的对象");
            }
        }

        /// 获取指定类型的所有对象
        private List<GameObject> GetAllObjectsOfType(string objectType)
        {
            var allObjects = new List<GameObject>();
            
            if (_objectsByType.TryGetValue(objectType, out var value))
                allObjects.AddRange(value);
            if (_componentObjects.TryGetValue(objectType, out var o))
                allObjects.AddRange(o);
            
            return allObjects.Distinct().ToList();
        }

        /// 绘制详细统计部分
        private void DrawDetailedStatisticsSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            // 设置详细统计区域的宽度为200像素，可以根据需要调整这个值
            EditorGUILayout.BeginVertical(areaStyle, GUILayout.Width(contentWidth - 460));
            EditorGUILayout.LabelField("详细对象类型统计", subheadingStyle);
            EditorGUILayout.BeginVertical(EditorStyles.textArea);
            // 按对象类型排序显示
            var sortedTypes = _objectCountByType.OrderByDescending(pair => pair.Value);

            foreach (var typePair in sortedTypes)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(typePair.Key + ":", normalStyle);
                DrawSelectableCount(typePair.Key, typePair.Value.ToString("N0"));
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(7);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        /// 绘制可选择的计数按钮
        private void DrawSelectableCount(string objectType, string countText)
        {
            var style = EditorStyle.Get;
            
            // 设置按钮样式为链接样式
            var buttonStyle = new GUIStyle(style.normalButton);
            buttonStyle.normal.textColor = Color.cyan;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.alignment = TextAnchor.MiddleLeft;
            buttonStyle.padding = new RectOffset(4, 4, 2, 2);
            var buttonStyle2 = new GUIStyle(style.normalButton);
            buttonStyle2.normal.textColor = Color.gray;
            buttonStyle2.hover.textColor = Color.gray;
            buttonStyle2.alignment = TextAnchor.MiddleLeft;
            
            // 检查是否有可用的对象或资源
            bool hasObjects = false;
            
            if (objectType == "Material")
            {
                hasObjects = _allMaterials.Count > 0;
            }
            else if (objectType == "Texture")
            {
                hasObjects = _allTextures.Count > 0;
            }
            else
            {
                hasObjects = (_objectsByType.ContainsKey(objectType) && _objectsByType[objectType].Count > 0) ||
                             (_componentObjects.ContainsKey(objectType) && _componentObjects[objectType].Count > 0);
            }
            
            // 如果没有对象，显示普通文本
            if (!hasObjects)
            {
                // EditorGUILayout.LabelField(countText, style.normalfont);
                // if(countText != "0")
                if (GUILayout.Button("empty", buttonStyle2, GUILayout.Width(60), GUILayout.Height(18)))
                return;
            } else if (GUILayout.Button(countText, buttonStyle, GUILayout.Width(60), GUILayout.Height(18)))
            {
                // 根据对象类型显示不同的选择菜单
                if (objectType == "Material")
                {
                    ShowMaterialSelectionMenu();
                }
                else if (objectType == "Texture")
                {
                    ShowTextureSelectionMenu();
                }
                else
                {
                    ShowObjectSelectionMenu(objectType);
                }
            }
        }

        /// 显示对象选择菜单
        private void ShowObjectSelectionMenu(string objectType)
        {
            GenericMenu menu = new GenericMenu();
            
            // 获取所有相关对象
            List<GameObject> allObjects = new List<GameObject>();
            
            if (_objectsByType.ContainsKey(objectType))
                allObjects.AddRange(_objectsByType[objectType]);
            if (_componentObjects.ContainsKey(objectType))
                allObjects.AddRange(_componentObjects[objectType]);
            
            // 去重
            allObjects = allObjects.Distinct().ToList();
            
            // 添加选择所有对象的选项
            menu.AddItem(new GUIContent($"选择所有 {objectType} ({allObjects.Count}个)"), false, () => {
                Selection.objects = allObjects.ToArray();
                if (allObjects.Count > 0)
                    EditorGUIUtility.PingObject(allObjects[0]);
            });
            
            menu.AddSeparator("");
            
            // 添加选择单个对象的选项
            foreach (var obj in allObjects.Take(20)) // 限制显示数量避免菜单过长
            {
                string objectName = obj.name;
                if (objectName.Length > 30)
                    objectName = objectName.Substring(0, 27) + "...";
                
                menu.AddItem(new GUIContent($"{objectName}"), false, (userData) => {
                    GameObject selectedObj = (GameObject)userData;
                    Selection.activeGameObject = selectedObj;
                    EditorGUIUtility.PingObject(selectedObj);
                }, obj);
            }
            
            // 如果对象数量超过20个，添加查看更多选项
            if (allObjects.Count > 20)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"... 还有 {allObjects.Count - 20} 个对象"), false, () => {
                    // 在控制台输出所有对象名称
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"所有 {objectType} 对象 ({allObjects.Count}个):");
                    foreach (var obj in allObjects)
                    {
                        sb.AppendLine($"  - {obj.name} (位置: {obj.transform.position})");
                    }
                    Debug.Log(sb.ToString());
                });
            }
            
            menu.ShowAsContext();
        }

        /// 显示材质选择菜单
        private void ShowMaterialSelectionMenu()
        {
            GenericMenu menu = new GenericMenu();
            
            // 添加选择所有材质的选项
            menu.AddItem(new GUIContent($"选择所有材质 ({_allMaterials.Count}个)"), false, () => {
                Selection.objects = _allMaterials.ToArray();
                if (_allMaterials.Count > 0)
                    EditorGUIUtility.PingObject(_allMaterials[0]);
            });
            
            menu.AddSeparator("");
            
            // 添加选择单个材质的选项
            foreach (var material in _allMaterials.Take(20)) // 限制显示数量避免菜单过长
            {
                string materialName = material.name;
                if (materialName.Length > 30)
                    materialName = materialName.Substring(0, 27) + "...";
                
                menu.AddItem(new GUIContent($"{materialName}"), false, (userData) => {
                    Material selectedMaterial = (Material)userData;
                    Selection.activeObject = selectedMaterial;
                    EditorGUIUtility.PingObject(selectedMaterial);
                }, material);
            }
            
            // 如果材质数量超过20个，添加查看更多选项
            if (_allMaterials.Count > 20)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"... 还有 {_allMaterials.Count - 20} 个材质"), false, () => {
                    // 在控制台输出所有材质名称
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"所有材质 ({_allMaterials.Count}个):");
                    foreach (var material in _allMaterials)
                    {
                        sb.AppendLine($"  - {material.name} (着色器: {material.shader?.name ?? "无"})");
                    }
                    Debug.Log(sb.ToString());
                });
            }
            
            menu.ShowAsContext();
        }

        /// 显示纹理选择菜单
        private void ShowTextureSelectionMenu()
        {
            GenericMenu menu = new GenericMenu();
            
            // 添加选择所有纹理的选项
            menu.AddItem(new GUIContent($"选择所有纹理 ({_allTextures.Count}个)"), false, () => {
                Selection.objects = _allTextures.ToArray();
                if (_allTextures.Count > 0)
                    EditorGUIUtility.PingObject(_allTextures[0]);
            });
            
            menu.AddSeparator("");
            
            // 添加选择单个纹理的选项
            foreach (var texture in _allTextures.Take(20)) // 限制显示数量避免菜单过长
            {
                string textureName = texture.name;
                if (textureName.Length > 30)
                    textureName = textureName.Substring(0, 27) + "...";
                
                menu.AddItem(new GUIContent($"{textureName}"), false, (userData) => {
                    Texture selectedTexture = (Texture)userData;
                    Selection.activeObject = selectedTexture;
                    EditorGUIUtility.PingObject(selectedTexture);
                }, texture);
            }
            
            // 如果纹理数量超过20个，添加查看更多选项
            if (_allTextures.Count > 20)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"... 还有 {_allTextures.Count - 20} 个纹理"), false, () => {
                    // 在控制台输出所有纹理名称
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"所有纹理 ({_allTextures.Count}个):");
                    foreach (var texture in _allTextures)
                    {
                        string textureType = texture.GetType().Name;
                        string sizeInfo = "";
                        if (texture is Texture2D tex2D)
                        {
                            sizeInfo = $" ({tex2D.width}x{tex2D.height})";
                        }
                        sb.AppendLine($"  - {texture.name}{sizeInfo} (类型: {textureType})");
                    }
                    Debug.Log(sb.ToString());
                });
            }
            
            menu.ShowAsContext();
        }

        /// <summary>
        /// 添加组件对象到字典
        /// </summary>
        private void AddComponentObject(string componentType, GameObject gameObject)
        {
            if (!_componentObjects.ContainsKey(componentType))
                _componentObjects[componentType] = new List<GameObject>();
            _componentObjects[componentType].Add(gameObject);
        }

        /// <summary>
        /// 选择指定类型的所有对象
        /// </summary>
        private void SelectObjectsOfType(string objectType)
        {
            if (_objectsByType.ContainsKey(objectType) && _objectsByType[objectType].Count > 0)
            {
                Selection.objects = _objectsByType[objectType].ToArray();
                EditorGUIUtility.PingObject(_objectsByType[objectType][0]);
            }
        }

        /// <summary>
        /// 选择指定组件的所有对象
        /// </summary>
        private void SelectObjectsWithComponent(string componentType)
        {
            if (_componentObjects.ContainsKey(componentType) && _componentObjects[componentType].Count > 0)
            {
                Selection.objects = _componentObjects[componentType].ToArray();
                EditorGUIUtility.PingObject(_componentObjects[componentType][0]);
            }
        }

        /// <summary>
        /// 获取对象类型
        /// </summary>
        private string GetObjectType(GameObject gameObject)
        {
            // 特殊组件类型检测
            if (gameObject.GetComponent<Terrain>() != null) return "Terrain";
            if (gameObject.GetComponent<Camera>() != null) return "Camera";
            if (gameObject.GetComponent<Light>() != null) return "Light";
            if (gameObject.GetComponent<ParticleSystem>() != null) return "ParticleSystem";
            
            // 渲染器类型检测 - 按渲染器类型优先级排序
            if (gameObject.GetComponent<SkinnedMeshRenderer>() != null) return "SkinnedMeshRenderer";
            if (gameObject.GetComponent<MeshRenderer>() != null) return "MeshRenderer";
            if (gameObject.GetComponent<SpriteRenderer>() != null) return "SpriteRenderer";
            if (gameObject.GetComponent<LineRenderer>() != null) return "LineRenderer";
            
            // UI组件检测
            if (gameObject.GetComponent<Canvas>() != null) return "Canvas";
            
            // 物理组件检测
            if (gameObject.GetComponent<Collider>() != null) return "Collider";
            if (gameObject.GetComponent<Rigidbody>() != null) return "Rigidbody";
            
            // 音频组件检测
            if (gameObject.GetComponent<AudioSource>() != null) return "AudioSource";
            
            return "GameObject";
        }


        /// 估算纹理内存
        private long EstimateTextureMemory(Texture2D texture)
        {
            if (texture == null) return 0;
            
            // 使用Unity内置的纹理内存估算方法
            long memory = 0;
            
            try
            {
                // 使用Unity的Profiler获取准确的内存大小
                #if UNITY_EDITOR
                memory = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                #else
                memory = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                #endif
                
                // 如果无法获取准确值，则使用估算方法
                if (memory <= 0)
                {
                    memory = texture.width * texture.height;
                    
                    // 根据纹理格式估算内存占用
                    switch (texture.format)
                    {
                        case TextureFormat.RGBA32:
                        case TextureFormat.ARGB32:
                        case TextureFormat.BGRA32:
                            memory *= 4; // 32位 = 4字节
                            break;
                        case TextureFormat.RGB24:
                            memory *= 3; // 24位 = 3字节
                            break;
                        case TextureFormat.RGBAFloat:
                        case TextureFormat.RGBAHalf:
                            memory *= 8; // 浮点格式占用更多内存
                            break;
                        case TextureFormat.RGB565:
                        case TextureFormat.RGBA4444:
                            memory *= 2; // 16位 = 2字节
                            break;
                        case TextureFormat.Alpha8:
                            memory *= 1; // 8位 = 1字节
                            break;
                        case TextureFormat.DXT1:
                            memory = memory / 2; // DXT1压缩
                            break;
                        case TextureFormat.DXT5:
                            // DXT5不压缩大小，保持原值
                            break;
                        case TextureFormat.BC7:
                        case TextureFormat.BC6H:
                            // BC格式保持原大小
                            break;
                        default:
                            memory *= 4; // 默认按32位估算
                            break;
                    }
                    
                    // 考虑Mipmaps
                    if (texture.mipmapCount > 1)
                    {
                        memory = (long)(memory * 1.33f); // Mipmaps增加约33%内存
                    }
                }
            }
            catch (System.Exception)
            {
                // 如果出现异常，使用保守估算
                memory = texture.width * texture.height * 4; // 默认按RGBA32估算
            }
            
            return memory;
        }


        /// 格式化内存大小显示
        private static string FormatMemorySize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            var order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }


        // ReSharper disable Unity.PerformanceAnalysis
        /// 绘制选中对象三角面数信息
        private void DrawSelectedObjectTriangleInfo(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            // float sectionWidth = Mathf.Min(230, contentWidth * 0.5f - 5);
            EditorGUILayout.BeginVertical(areaStyle);

            // 标题行 - 包含"选择所有层级"按钮
            EditorGUILayout.BeginHorizontal();
            GUI.contentColor = Color.cyan;
            EditorGUILayout.LabelField("选中对象详细信息", subheadingStyle);
            GUI.contentColor = Color.white;
            GUILayout.FlexibleSpace();
            // 添加"选择所有层级"按钮
            var selectedObjects = Selection.gameObjects;
            bool hasSelection = selectedObjects.Length > 0;
            GUI.enabled = hasSelection;
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("选择所有层级", GUILayout.Width(100)))
            {
                SceneTools.SelectAllHierarchy();
            }
            GUILayout.Space(20);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            // 使用EditorStyles.textArea包裹内容
            EditorGUILayout.BeginVertical(EditorStyles.textArea);
            {
                // 获取选中对象 - 支持独立窗口的实时更新
                GameObject[] selectedObjectsInner = Selection.gameObjects;
                
                // 如果是独立窗口，检查是否有独立窗口的选中数量
                if (Parent is TestScenePerformanceAnalyzer standaloneWindow)
                {
                    // 使用独立窗口的选中数量来确保实时更新
                    int standaloneCount = standaloneWindow.StandaloneSelectedCount;
                    if (standaloneCount != selectedObjectsInner.Length)
                    {
                        // 如果数量不一致，强制更新显示
                        selectedObjectsInner = Selection.gameObjects;
                    }
                }
                
                if (selectedObjectsInner.Length == 0)
                {
                    EditorGUILayout.LabelField("未选中任何对象", normalStyle);
                }
                else
                {
                    var totalTriangles = 0;
                    var totalVertices = 0;
                    var meshCount = 0;
                    var skinnedMeshCount = 0;
                    Bounds? combinedBounds = null;
                    
                    // 统计选中对象的三角面数、顶点数和尺寸
                    foreach (var gameObject in selectedObjectsInner)
                    {
                        var meshFilter = gameObject.GetComponent<MeshFilter>();
                        if (meshFilter && meshFilter.sharedMesh)
                        {
                            totalTriangles += meshFilter.sharedMesh.triangles.Length / 3;
                            totalVertices += meshFilter.sharedMesh.vertexCount;
                            meshCount++;
                        }
                        
                        var skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
                        if (skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
                        {
                            totalTriangles += skinnedMeshRenderer.sharedMesh.triangles.Length / 3;
                            totalVertices += skinnedMeshRenderer.sharedMesh.vertexCount;
                            skinnedMeshCount++;
                        }
                        
                        // 计算所有选中对象的整体边界框
                        var boundsRenderer = gameObject.GetComponent<Renderer>();
                        if (!boundsRenderer) continue;
                        var worldBounds = boundsRenderer.bounds;
                            
                        if (combinedBounds == null)
                        {
                            combinedBounds = worldBounds;
                        }
                        else
                        {
                            // 正确使用Bounds.Encapsulate方法
                            Bounds tempBounds = combinedBounds.Value;
                            tempBounds.Encapsulate(worldBounds);
                            combinedBounds = tempBounds;
                        }
                    }
                    
                    // 显示基本信息 - 实时更新选中对象数量
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("选中对象数:", normalStyle, GUILayout.Width(88));
                    EditorGUILayout.LabelField(selectedObjectsInner.Length.ToString("N0"), normalStyle);
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("网格对象:", normalStyle, GUILayout.Width(88));
                    EditorGUILayout.LabelField(meshCount.ToString("N0"), normalStyle);
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("蒙皮网格:", normalStyle, GUILayout.Width(88));
                    EditorGUILayout.LabelField(skinnedMeshCount.ToString("N0"), normalStyle);
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.Space();
                    
                    // 显示三角面数信息
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("总三角面数:", normalStyle, GUILayout.Width(88));
                    
                    // 根据三角面数设置字体颜色
                    GUIStyle triangleCountStyle = new GUIStyle(normalStyle);
                    if (totalTriangles > 15000)
                    {
                        triangleCountStyle.normal.textColor = Color.red; // 超过15000显示红色
                    }
                    else if (totalTriangles > 8000)
                    {
                        triangleCountStyle.normal.textColor = Color.yellow; // 超过8000显示黄色
                    }
                    else if (totalTriangles == 0 && (meshCount + skinnedMeshCount) > 0)
                    {
                        triangleCountStyle.normal.textColor = Color.cyan; // 有网格但三角面数为0显示青色
                    }
                    
                    EditorGUILayout.LabelField(totalTriangles.ToString("N0"), triangleCountStyle);
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("总顶点数:", normalStyle, GUILayout.Width(88));
                    EditorGUILayout.LabelField(totalVertices.ToString("N0"), normalStyle);
                    EditorGUILayout.EndHorizontal();
                    
                    // 计算平均值
                    if (meshCount + skinnedMeshCount > 0)
                    {
                        int avgTriangles = totalTriangles / (meshCount + skinnedMeshCount);
                        int avgVertices = totalVertices / (meshCount + skinnedMeshCount);
                        
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("平均三角面:", normalStyle, GUILayout.Width(88));
                        
                        // 根据平均三角面数设置字体颜色
                        GUIStyle avgTriangleCountStyle = new GUIStyle(normalStyle);
                        if (avgTriangles > 5000)
                        {
                            avgTriangleCountStyle.normal.textColor = Color.red; // 超过5000显示红色
                        }
                        else if (avgTriangles > 2000)
                        {
                            avgTriangleCountStyle.normal.textColor = Color.yellow; // 超过2000显示黄色
                        }
                        
                        EditorGUILayout.LabelField(avgTriangles.ToString("N0"), avgTriangleCountStyle);
                        EditorGUILayout.EndHorizontal();
                        
                        // EditorGUILayout.BeginHorizontal();
                        // EditorGUILayout.LabelField("平均顶点:", normalStyle, GUILayout.Width(88));
                        // EditorGUILayout.LabelField(avgVertices.ToString("N0"), normalStyle);
                        // EditorGUILayout.EndHorizontal();
                    }
                    
                    // 显示尺寸信息
                    if (combinedBounds != null)
                    {
                        Vector3 combinedSize = combinedBounds.Value.size;
                        
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField("整体边界框尺寸 (米)", normalStyle);
                        
                        // 使用紧凑的固定宽度布局，避免整行过宽
                        EditorGUILayout.BeginHorizontal(GUILayout.Width(235));
                        {
                            // 长(X) - 紧凑布局
                            EditorGUILayout.LabelField("宽(X):", normalStyle, GUILayout.Width(39));
                            EditorGUILayout.LabelField($"{combinedSize.x:F1}", normalStyle, GUILayout.Width(75));
                            // 高(Z) - 紧凑布局
                            EditorGUILayout.LabelField("长(Z):", normalStyle, GUILayout.Width(39));
                            EditorGUILayout.LabelField($"{combinedSize.z:F1}", normalStyle, GUILayout.Width(75));
                            // 宽(Y) - 紧凑布局
                            EditorGUILayout.LabelField("高(Y):", normalStyle, GUILayout.Width(39));
                            EditorGUILayout.LabelField($"{combinedSize.y:F1}", normalStyle, GUILayout.Width(75));
                        }
                        EditorGUILayout.EndHorizontal();
                        
                        // 显示体积估算
                        float totalVolume = combinedSize.x * combinedSize.y * combinedSize.z;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("总体积估算:", normalStyle, GUILayout.Width(80));
                        EditorGUILayout.LabelField(totalVolume.ToString("F1") + " m³", normalStyle);
                        EditorGUILayout.EndHorizontal();
                        
                        // 调试信息：显示计算的对象数量
                        int objectsWithBounds = 0;
                        foreach (var gameObject in selectedObjectsInner)
                        {
                            var countRenderer = gameObject.GetComponent<Renderer>();
                            if (countRenderer != null) objectsWithBounds++;
                        }
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("计算对象数:", normalStyle, GUILayout.Width(80));
                        EditorGUILayout.LabelField($"{objectsWithBounds}/{selectedObjectsInner.Length}", normalStyle);
                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField("尺寸信息: 无渲染器组件", normalStyle);
                    }
                    
                    // 显示性能建议
                    EditorGUILayout.Space();
                    if (totalTriangles > 8000)
                    {
                        GUI.contentColor = Color.yellow;
                        EditorGUILayout.LabelField("⚠ 选中对象三角面数量较多，建议优化", normalStyle);
                        GUI.contentColor = Color.white;
                    }
                    else if (totalTriangles > 15000)
                    {
                        GUI.contentColor = Color.red;
                        EditorGUILayout.LabelField("⚠ 选中对象三角面数量过多，严重影响性能", normalStyle);
                        GUI.contentColor = Color.white;
                    }
                    else if (totalTriangles == 0 && (meshCount + skinnedMeshCount) > 0)
                    {
                        GUI.contentColor = Color.cyan;
                        EditorGUILayout.LabelField("ℹ 选中对象包含网格但无法获取三角面数", normalStyle);
                        GUI.contentColor = Color.white;
                    }
                }
            }
            EditorGUILayout.EndVertical();
            
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }


        // ReSharper disable Unity.PerformanceAnalysis
        /// <summary>
        /// 绘制全局光照检查部分
        /// </summary>
        private void DrawGlobalIlluminationSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            // EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("全局光照检查", subheadingStyle);
            EditorGUILayout.BeginVertical(EditorStyles.textField);
            // 全局光照统计
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("贡献GI:","收集勾选了(ContributeGlobalIllumination)的静态网格"), normalStyle, GUILayout.Width(60));
            DrawSelectableCount("ContributeGI", _contributeGIObjects.Count.ToString("N0"));
            
            EditorGUILayout.LabelField("接收GI:", normalStyle, GUILayout.Width(60));
            DrawSelectableCount("ReceiveGI", _receiveGIObjects.Count.ToString("N0"));
            
            EditorGUILayout.LabelField(new GUIContent("静态批处理:","收集取消了(DynamicOcclusion)的静态网格，会不参与合批影响DrawCall"), normalStyle, GUILayout.Width(80));
            DrawSelectableCount("StaticBatching", _staticBatchingObjects.Count.ToString("N0"));
            EditorGUILayout.EndHorizontal();
            
            // 全局光照详细统计
            if (_giObjectsByType.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("按类型统计:", normalStyle);
                
                // 使用文本区域包裹详细统计
                EditorGUILayout.BeginVertical();
                var sortedGITypes = _giObjectsByType.OrderByDescending(pair => pair.Value.Count);
                
                foreach (var typePair in sortedGITypes)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(typePair.Key + ":", normalStyle, GUILayout.Width(120));
                    DrawSelectableCount($"ContributeGI_{typePair.Key}", typePair.Value.Count.ToString("N0"));
                    EditorGUILayout.EndHorizontal();
                }
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
            }
            
            // 全局光照操作按钮
            EditorGUILayout.Space();
            DrawGlobalIlluminationActions(contentWidth);
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 绘制全局光照操作按钮
        /// </summary>
        private void DrawGlobalIlluminationActions(float contentWidth)
        {
            var style = EditorStyle.Get;
            
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            EditorGUILayout.LabelField("快速操作:", style.normalfont, GUILayout.Width(66));
            
            // 选择所有贡献GI对象
            bool hasContributeGI = _contributeGIObjects.Count > 0;
            GUI.enabled = hasContributeGI;
            if (GUILayout.Button("选择贡献GI", style.normalButton, GUILayout.Width(100)))
            {
                SelectAllContributeGIObjects();
            }
            GUI.enabled = true;
            
            // 选择所有接收GI对象
            bool hasReceiveGI = _receiveGIObjects.Count > 0;
            GUI.enabled = hasReceiveGI;
            if (GUILayout.Button("选择接收GI", style.normalButton, GUILayout.Width(100)))
            {
                SelectAllReceiveGIObjects();
            }
            GUI.enabled = true;
            
            // 选择所有静态批处理对象
            bool hasStaticBatching = _staticBatchingObjects.Count > 0;
            GUI.enabled = hasStaticBatching;
            if (GUILayout.Button("选择静态批处理", style.normalButton, GUILayout.Width(120)))
            {
                SelectAllStaticBatchingObjects();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            // 全局光照优化建议
            if (_contributeGIObjects.Count > 50)
            {
                EditorGUILayout.Space();
                GUI.contentColor = Color.yellow;
                EditorGUILayout.LabelField("⚠ 全局光照贡献对象较多，建议优化光照烘焙设置", style.normalfont);
                GUI.contentColor = Color.white;
            }
        }

        /// <summary>
        /// 选择所有贡献GI对象
        /// </summary>
        private void SelectAllContributeGIObjects()
        {
            if (_contributeGIObjects.Count > 0)
            {
                Selection.objects = _contributeGIObjects.ToArray();
                EditorGUIUtility.PingObject(_contributeGIObjects[0]);
                Debug.Log($"已选择 {_contributeGIObjects.Count} 个贡献全局光照的对象");
            }
            else
            {
                Debug.Log("场景中没有找到贡献全局光照的对象");
            }
        }

        /// <summary>
        /// 选择所有接收GI对象
        /// </summary>
        private void SelectAllReceiveGIObjects()
        {
            if (_receiveGIObjects.Count > 0)
            {
                Selection.objects = _receiveGIObjects.ToArray();
                EditorGUIUtility.PingObject(_receiveGIObjects[0]);
                Debug.Log($"已选择 {_receiveGIObjects.Count} 个接收全局光照的对象");
            }
            else
            {
                Debug.Log("场景中没有找到接收全局光照的对象");
            }
        }

        /// <summary>
        /// 选择所有静态批处理对象
        /// </summary>
        private void SelectAllStaticBatchingObjects()
        {
            if (_staticBatchingObjects.Count > 0)
            {
                Selection.objects = _staticBatchingObjects.ToArray();
                EditorGUIUtility.PingObject(_staticBatchingObjects[0]);
                Debug.Log($"已选择 {_staticBatchingObjects.Count} 个静态批处理对象");
            }
            else
            {
                Debug.Log("场景中没有找到静态批处理对象");
            }
        }

        /// <summary>
        /// 绘制资源利用率检查部分
        /// </summary>
        private void DrawResourceUtilizationSection(GUIStyle areaStyle, GUIStyle subheadingStyle, GUIStyle normalStyle, float contentWidth)
        {
            EditorGUILayout.BeginVertical(areaStyle);
            EditorGUILayout.LabelField("资源利用率检查", subheadingStyle);
            
            // 显示上次扫描时间
            if (_lastResourceScanTime != System.DateTime.MinValue)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
                EditorGUILayout.LabelField("上次扫描时间:", normalStyle, GUILayout.Width(88));
                EditorGUILayout.LabelField(_lastResourceScanTime.ToString("HH:mm:ss"), normalStyle);
                EditorGUILayout.EndHorizontal();
            }
            
            // 扫描按钮
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            
            GUI.enabled = !_isScanningResources;
            GUI.backgroundColor = _isScanningResources ? Color.gray : Color.green;
            if (GUILayout.Button(_isScanningResources ? "扫描中..." : "扫描未使用资源", GUILayout.Width(120)))
            {
                ScanUnusedResources();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            // 排除列表设置
            EditorGUILayout.Space();
            _showExclusionSettings = EditorGUILayout.Foldout(_showExclusionSettings, "排除列表设置", true);
            if (_showExclusionSettings)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                // 排除路径列表
                EditorGUILayout.LabelField("排除的路径:", normalStyle);
                _exclusionScrollPosition = EditorGUILayout.BeginScrollView(_exclusionScrollPosition, GUILayout.Height(100));
                for (int i = 0; i < _excludedPaths.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(_excludedPaths[i], normalStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("删除", GUILayout.Width(50)))
                    {
                        _excludedPaths.RemoveAt(i);
                        SaveExclusionSettings();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
                
                // 添加新排除路径 - 支持手动输入和Project窗口选择
                EditorGUILayout.BeginHorizontal();
                // 缩短标签宽度为80像素
                EditorGUILayout.LabelField("新排除路径:", normalStyle, GUILayout.Width(80));
                _newExclusionPath = EditorGUILayout.TextField(_newExclusionPath);
                if (GUILayout.Button("添加", GUILayout.Width(60)) && !string.IsNullOrEmpty(_newExclusionPath))
                {
                    if (!_excludedPaths.Contains(_newExclusionPath))
                    {
                        _excludedPaths.Add(_newExclusionPath);
                        _newExclusionPath = "";
                        SaveExclusionSettings();
                    }
                }
                // 添加"添加选中目录"按钮
                GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button("Project中选中", GUILayout.Width(90)))
                {
                    AddSelectedProjectFolderToExclusion();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                
                
                // 排除模式列表
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("排除的模式 (通配符):", normalStyle);
                for (int i = 0; i < _excludedPatterns.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(_excludedPatterns[i], normalStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("删除", GUILayout.Width(50)))
                    {
                        _excludedPatterns.RemoveAt(i);
                        SaveExclusionSettings();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                // 添加新排除模式
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("新排除模式:", normalStyle, GUILayout.Width(80));
                _newExclusionPattern = EditorGUILayout.TextField(_newExclusionPattern);
                if (GUILayout.Button("添加", GUILayout.Width(60)) && !string.IsNullOrEmpty(_newExclusionPattern))
                {
                    if (!_excludedPatterns.Contains(_newExclusionPattern))
                    {
                        _excludedPatterns.Add(_newExclusionPattern);
                        _newExclusionPattern = "";
                        SaveExclusionSettings();
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                // 示例说明
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("示例:", normalStyle);
                EditorGUILayout.LabelField("• 路径: Assets/GameMain/Test/", normalStyle);
                EditorGUILayout.LabelField("• 模式: *.test.* 或 Test*", normalStyle);
                
                EditorGUILayout.EndVertical();
            }
            
            // 显示扫描结果
            if (_unusedResources.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"发现 {_unusedResources.Count} 个未使用资源", normalStyle);
                
                // 按类型统计和筛选
                if (_unusedResourcesByType.Count > 0)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("按类型筛选 (点击复选框切换显示):", normalStyle);
                    
                // 全选/全不选按钮和单选模式
                EditorGUILayout.BeginHorizontal();
                bool allSelected = _resourceTypeFilters.Count > 0 && _resourceTypeFilters.All(kvp => kvp.Value);
                bool newAllSelected = EditorGUILayout.ToggleLeft("全选", allSelected, GUILayout.Width(80));
                if (newAllSelected != allSelected)
                {
                    // 切换所有筛选状态
                    foreach (var key in _resourceTypeFilters.Keys.ToList())
                    {
                        _resourceTypeFilters[key] = newAllSelected;
                    }
                }
                
                // 单选模式Toggle
                bool newSingleSelectMode = EditorGUILayout.ToggleLeft("单选", _isSingleSelectMode, GUILayout.Width(80));
                if (newSingleSelectMode != _isSingleSelectMode)
                {
                    _isSingleSelectMode = newSingleSelectMode;
                    
                    // 如果启用单选模式，确保最多只有一个选项被选中
                    if (_isSingleSelectMode)
                    {
                        // 检查当前是否有选中的选项
                        bool hasSelected = _resourceTypeFilters.Any(kvp => kvp.Value);
                        if (!hasSelected && _resourceTypeFilters.Count > 0)
                        {
                            // 如果没有选中的选项，选择第一个
                            var firstKey = _resourceTypeFilters.Keys.First();
                            foreach (var key in _resourceTypeFilters.Keys.ToList())
                            {
                                _resourceTypeFilters[key] = (key == firstKey);
                            }
                        }
                        else if (hasSelected)
                        {
                            // 确保只有一个选项被选中
                            bool foundFirst = false;
                            foreach (var key in _resourceTypeFilters.Keys.ToList())
                            {
                                if (_resourceTypeFilters[key] && !foundFirst)
                                {
                                    foundFirst = true;
                                }
                                else if (_resourceTypeFilters[key] && foundFirst)
                                {
                                    _resourceTypeFilters[key] = false;
                                }
                            }
                        }
                    }
                }
                
                GUILayout.FlexibleSpace();
                
                // 显示筛选后的资源数量
                int filteredCount = GetFilteredResourcesCount();
                EditorGUILayout.LabelField($"显示: {filteredCount}/{_unusedResources.Count}", normalStyle);
                EditorGUILayout.EndHorizontal();
                    
                    // 类型筛选复选框
                    EditorGUILayout.BeginHorizontal();
                    int columnCount = 0;
                    foreach (var kvp in _unusedResourcesByType.OrderByDescending(x => x.Value.Count))
                    {
                        if (columnCount >= 3) // 每行最多3个
                        {
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.BeginHorizontal();
                            columnCount = 0;
                        }
                        
                        // 确保类型在筛选字典中
                        if (!_resourceTypeFilters.ContainsKey(kvp.Key))
                        {
                            _resourceTypeFilters[kvp.Key] = true;
                        }
                        
                        // 显示复选框
                        bool isSelected = _resourceTypeFilters[kvp.Key];
                        bool newIsSelected = EditorGUILayout.ToggleLeft($"{kvp.Key} ({kvp.Value.Count})", isSelected, GUILayout.Width(150));
                        if (newIsSelected != isSelected)
                        {
                            // 处理单选模式逻辑
                            if (_isSingleSelectMode)
                            {
                                if (newIsSelected)
                                {
                                    // 单选模式下选中一个选项：取消所有其他选项的选择
                                    foreach (var key in _resourceTypeFilters.Keys.ToList())
                                    {
                                        _resourceTypeFilters[key] = (key == kvp.Key);
                                    }
                                }
                                else
                                {
                                    // 单选模式下取消选中：如果这是唯一选中的选项，不允许取消
                                    // 检查是否还有其他选中的选项
                                    bool hasOtherSelected = _resourceTypeFilters.Any(k => k.Key != kvp.Key && k.Value);
                                    if (!hasOtherSelected)
                                    {
                                        // 如果没有其他选中的选项，保持当前选项为选中状态
                                        _resourceTypeFilters[kvp.Key] = true;
                                    }
                                }
                            }
                            else
                            {
                                // 多选模式：直接更新当前选项的状态
                                _resourceTypeFilters[kvp.Key] = newIsSelected;
                            }
                        }
                        
                        columnCount++;
                    }
                    
                    // 填充剩余空间
                    if (columnCount > 0)
                    {
                        GUILayout.FlexibleSpace();
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.EndVertical();
                }
                
                // 资源列表
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("未使用资源列表:", normalStyle);
                
                _resourceScrollPosition = EditorGUILayout.BeginScrollView(_resourceScrollPosition, GUILayout.Height(200));
                
                // 显示筛选后的资源
                int displayedCount = 0;
                foreach (var resourcePath in _unusedResources)
                {
                    // 检查资源类型是否被选中
                    string fileExtension = Path.GetExtension(resourcePath).ToLower();
                    string resourceType = GetResourceType(fileExtension);
                    
                    if (_resourceTypeFilters.ContainsKey(resourceType) && !_resourceTypeFilters[resourceType])
                    {
                        continue; // 跳过未选中的类型
                    }
                    
                    EditorGUILayout.BeginHorizontal();
                    
                    // 显示资源路径
                    EditorGUILayout.LabelField(Path.GetFileName(resourcePath), normalStyle, GUILayout.Width(150));
                    EditorGUILayout.LabelField(resourcePath, normalStyle, GUILayout.ExpandWidth(true));
                    
                    // GUILayout.FlexibleSpace();
                    
                    // 选择按钮
                    if (GUILayout.Button("选择", GUILayout.Width(50)))
                    {
                        SelectResource(resourcePath);
                    }
                    
                    // 删除按钮
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("删除", GUILayout.Width(50)))
                    {
                        DeleteResource(resourcePath);
                    }
                    GUI.backgroundColor = Color.white;
                    
                    EditorGUILayout.EndHorizontal();
                    displayedCount++;
                }
                
                // 如果没有显示任何资源（所有类型都被取消选中）
                if (displayedCount == 0)
                {
                    EditorGUILayout.LabelField("没有选中任何类型，请至少选择一个资源类型", normalStyle);
                }
                
                EditorGUILayout.EndScrollView();
                
                // 批量操作按钮
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button("选择所有未使用资源", GUILayout.Width(150)))
                {
                    SelectAllUnusedResources();
                }
                
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("删除所有未使用资源", GUILayout.Width(150)))
                {
                    DeleteAllUnusedResources();
                }
                GUI.backgroundColor = Color.white;
                
                EditorGUILayout.EndHorizontal();
            }
            else if (_isScanningResources)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("正在扫描资源...", normalStyle);
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("未发现未使用资源", normalStyle);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 扫描未使用资源
        /// </summary>
        private void ScanUnusedResources()
        {
            _isScanningResources = true;
            _unusedResources.Clear();
            _unusedResourcesByType.Clear();
            _resourceTypeFilters.Clear();
            _resourceUsageCache.Clear(); // 清除缓存以允许重复扫描
            
            // 在实际实现中，这里应该使用后台线程或协程来避免阻塞UI
            // 这里使用简单的实现作为示例
            
            try
            {
                // 获取项目中所有资源
                string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
                
                foreach (string assetPath in allAssetPaths)
                {
                // 跳过文件夹 - 简化文件夹识别逻辑，主要依赖AssetDatabase.IsValidFolder
                bool isFolder = AssetDatabase.IsValidFolder(assetPath);
                
                // 如果AssetDatabase.IsValidFolder返回false，进行简单的启发式检查
                // 主要检查是否有文件扩展名（大多数文件都有扩展名）
                if (!isFolder)
                {
                    string extension = Path.GetExtension(assetPath);
                    bool hasExtension = !string.IsNullOrEmpty(extension);
                    
                    // 如果没有扩展名，且路径看起来像文件夹（包含斜杠，不以常见文件扩展名结尾）
                    if (!hasExtension && assetPath.Contains("/"))
                    {
                        // 检查是否是已知的特殊文件夹路径
                        bool isSpecialFolderPath = assetPath.Contains("/Resources/") || 
                                                  assetPath.Contains("/StreamingAssets/") ||
                                                  assetPath.Contains("/Editor/") ||
                                                  assetPath.Contains("/Plugins/") ||
                                                  assetPath.Contains("/Gizmos/") ||
                                                  assetPath.Contains("/Standard Assets/") ||
                                                  assetPath.Contains("/Editor Default Resources/");
                        
                        // 如果是特殊文件夹路径，很可能是文件夹
                        if (isSpecialFolderPath)
                        {
                            isFolder = true;
                        }
                        // 对于包管理器路径，进行特殊处理
                        else if (assetPath.StartsWith("Packages/"))
                        {
                            // 包管理器路径通常没有文件扩展名
                            // 检查最后一部分是否包含@（版本号）或看起来像包名
                            string lastPart = assetPath.Split('/').Last();
                            if (lastPart.Contains("@") || (!lastPart.Contains(".") && lastPart.Length > 0))
                            {
                                isFolder = true;
                            }
                        }
                    }
                }
                    
                    if (isFolder)
                    {
                        // 调试日志，帮助识别哪些路径被识别为文件夹
                        // Debug.Log($"跳过文件夹: {assetPath}");
                        continue;
                    }
                    
                    // 跳过特殊文件夹和文件
                    if (assetPath.StartsWith("Assets/") && 
                        !assetPath.Contains("/Editor/") && 
                        !assetPath.Contains("/Resources/") &&
                        !assetPath.EndsWith(".cs") &&
                        !assetPath.EndsWith(".shader") &&
                        !assetPath.EndsWith(".cginc") &&
                        !assetPath.EndsWith(".hlsl"))
                    {
                    // 检查资源是否被引用
                    if (!IsResourceUsed(assetPath))
                    {
                        // 按类型分类
                        string fileExtension = Path.GetExtension(assetPath).ToLower();
                        string resourceType = GetResourceType(fileExtension);
                        
                        // 排除"Other"和"ScriptableObject"类型的资源
                        if (resourceType != "Other" && resourceType != "ScriptableObject")
                        {
                            _unusedResources.Add(assetPath);
                            
                            if (!_unusedResourcesByType.ContainsKey(resourceType))
                                _unusedResourcesByType[resourceType] = new List<string>();
                            
                            _unusedResourcesByType[resourceType].Add(assetPath);
                        }
                    }
                    }
                }
                
                // 初始化所有类型筛选状态为true（默认选中）
                foreach (var resourceType in _unusedResourcesByType.Keys)
                {
                    _resourceTypeFilters[resourceType] = true;
                }
                
                _lastResourceScanTime = System.DateTime.Now;
                Debug.Log($"扫描完成: 发现 {_unusedResources.Count} 个未使用资源");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"扫描资源时出错: {ex.Message}");
            }
            finally
            {
                _isScanningResources = false;
            }
        }

        /// <summary>
        /// 获取构建场景列表
        /// </summary>
        private List<string> GetBuildScenes()
        {
            List<string> buildScenes = new List<string>();
            
            try
            {
                // 使用Unity标准的EditorBuildSettings.scenes获取构建场景
                // 这是最可靠的方法，直接获取用户在Build Settings中配置的场景
                EditorBuildSettingsScene[] editorBuildScenes = EditorBuildSettings.scenes;
                
                if (editorBuildScenes == null || editorBuildScenes.Length == 0)
                {
                    Debug.LogWarning("构建设置中没有配置任何场景，将使用备用检查逻辑");
                    return buildScenes;
                }
                
                // 收集所有启用的构建场景
                foreach (EditorBuildSettingsScene scene in editorBuildScenes)
                {
                    if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    {
                        buildScenes.Add(scene.path);
                    }
                }
                
                Debug.Log($"从构建设置中获取到 {buildScenes.Count} 个构建场景");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"获取构建场景列表时出错: {ex.Message}");
            }
            
            return buildScenes;
        }

        /// <summary>
        /// 检查资源是否被构建场景引用
        /// </summary>
        private bool IsReferencedByBuildScenes(string assetPath)
        {
            // 获取构建场景列表
            List<string> buildScenes = GetBuildScenes();
            
            // 如果构建场景列表为空，返回false
            if (buildScenes.Count == 0)
            {
                return false;
            }
            
            // 检查资源是否被任何构建场景引用
            foreach (string scenePath in buildScenes)
            {
                // 检查场景本身是否引用了该资源
                if (IsAssetReferencedBy(scenePath, assetPath))
                {
                    return true;
                }
                
                // 检查场景的依赖资源是否引用了该资源
                string[] sceneDependencies = AssetDatabase.GetDependencies(new string[] { scenePath }, true);
                foreach (string dependency in sceneDependencies)
                {
                    if (dependency == assetPath)
                    {
                        return true;
                    }
                    
                    // 检查依赖资源的依赖
                    if (IsAssetReferencedBy(dependency, assetPath))
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 清理过期缓存
        /// </summary>
        private void CleanupExpiredCache()
        {
            float currentTime = Time.realtimeSinceStartup;
            
            // 检查是否达到清理间隔
            if (currentTime - _lastCacheCleanupTime < CACHE_CLEANUP_INTERVAL)
            {
                return; // 未达到清理间隔，跳过清理
            }
            
            // 记录清理开始时间
            _lastCacheCleanupTime = currentTime;
            
            // 收集过期的缓存键
            List<string> expiredKeys = new List<string>();
            
            foreach (var kvp in _resourceUsageCache)
            {
                if (kvp.Value.IsExpired(currentTime))
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            // 移除过期缓存
            foreach (string key in expiredKeys)
            {
                _resourceUsageCache.Remove(key);
            }
            
            // 如果清理了大量缓存，记录日志
            if (expiredKeys.Count > 0)
            {
                Debug.Log($"清理了 {expiredKeys.Count} 个过期缓存条目");
            }
        }
        
        /// <summary>
        /// 检查资源是否被使用（改进版，支持Addressables和更多特殊文件夹）
        /// </summary>
        private bool IsResourceUsed(string assetPath)
        {
            // 定期清理过期缓存
            CleanupExpiredCache();
            
            // 检查缓存
            if (_resourceUsageCache.TryGetValue(assetPath, out ResourceCacheEntry cacheEntry))
            {
                // 检查缓存是否过期
                if (!cacheEntry.IsExpired(Time.realtimeSinceStartup) && !cacheEntry.IsFileModified(assetPath))
                {
                    return cacheEntry.IsUsed;
                }
            }
            
            bool isUsed = IsResourceUsedInternal(assetPath);
            
            // 缓存结果
            _resourceUsageCache[assetPath] = ResourceCacheEntry.Create(isUsed, assetPath);
            
            return isUsed;
        }
        
        /// <summary>
        /// 内部资源使用检查逻辑（增强版）
        /// </summary>
        private bool IsResourceUsedInternal(string assetPath)
        {
            // 0. 检查是否在排除列表中
            if (IsResourceExcluded(assetPath))
            {
                return true; // 如果在排除列表中，视为"已使用"
            }
            
            // 1. 检查是否在特殊文件夹中（这些文件夹的资源会被自动包含）
            if (IsInSpecialFolder(assetPath))
            {
                return true;
            }
            
            // 2. 检查是否被Addressables系统引用
            if (IsReferencedByAddressables(assetPath))
            {
                return true;
            }
            
            // 3. 检查是否被构建场景引用
            if (IsReferencedByBuildScenes(assetPath))
            {
                return true;
            }
            
            // 4. 检查是否在代码中被引用（Resources.Load等）
            if (IsReferencedInCode(assetPath))
            {
                return true;
            }
            
            // 5. 检查是否被预制件引用
            if (IsReferencedByPrefabs(assetPath))
            {
                return true;
            }
            
            // 6. 检查是否被材质引用
            if (IsReferencedByMaterials(assetPath))
            {
                return true;
            }
            
            // 7. 检查是否被动画控制器引用
            if (IsReferencedByAnimators(assetPath))
            {
                return true;
            }
            
            // 8. 检查是否被脚本化对象引用
            if (IsReferencedByScriptableObjects(assetPath))
            {
                return true;
            }
            
            // 9. 检查是否被其他资源引用（备用检查）
            return IsResourceUsedFallback(assetPath);
        }
        
        /// <summary>
        /// 检查资源是否在特殊文件夹中
        /// </summary>
        private bool IsInSpecialFolder(string assetPath)
        {
            // Resources文件夹 - 打包时会被自动包含
            if (assetPath.Contains("/Resources/"))
                return true;
                
            // StreamingAssets文件夹 - 打包时会被包含
            if (assetPath.Contains("/StreamingAssets/"))
                return true;
                
            // Plugins文件夹 - 插件资源会被包含
            if (assetPath.Contains("/Plugins/"))
                return true;
                
            // Standard Assets文件夹 - 标准资源会被包含
            if (assetPath.Contains("/Standard Assets/"))
                return true;
                
            // Gizmos文件夹 - 编辑器图标资源
            if (assetPath.Contains("/Gizmos/"))
                return true;
                
            // Editor Default Resources文件夹 - 编辑器默认资源
            if (assetPath.Contains("/Editor Default Resources/"))
                return true;
                
            // Editor文件夹 - 编辑器专用资源，打包时不会包含
            if (assetPath.Contains("/Editor/"))
                return false; // 编辑器资源不被视为"使用"
                
            // 检查文件扩展名，某些类型的文件总是会被包含
            string extension = Path.GetExtension(assetPath).ToLower();
            if (extension == ".cs" || extension == ".shader" || extension == ".cginc" || extension == ".hlsl")
                return false; // 代码和着色器文件不被视为"使用"
                
            return false;
        }
        
        /// <summary>
        /// 检查资源是否被Addressables系统引用（优化版）
        /// </summary>
        private bool IsReferencedByAddressables(string assetPath)
        {
            #if UNITY_2021_3_OR_NEWER
            try
            {
                // 检查项目是否启用了Addressables
                if (!IsAddressablesEnabled())
                {
                    return false;
                }
                
                // 获取资源的GUID
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    return false;
                }
                
                // 使用Addressables API检查资源是否被引用
                // 方法1：检查资源是否在Addressables设置中
                if (IsAssetInAddressablesSettings(guid))
                {
                    return true;
                }
                
                // 方法2：检查资源是否被任何Addressables组引用
                if (IsAssetReferencedByAddressablesGroups(guid))
                {
                    return true;
                }
                
                // 方法3：检查资源是否在Addressables标签中
                if (IsAssetInAddressablesLabels(guid))
                {
                    return true;
                }
                
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"检查Addressables引用时出错: {ex.Message}");
                return false;
            }
            #else
            // 旧版本Unity不支持Addressables
            return false;
            #endif
        }
        
        /// <summary>
        /// 检查资源是否在Addressables设置中
        /// </summary>
        private bool IsAssetInAddressablesSettings(string guid)
        {
            #if UNITY_2021_3_OR_NEWER
            try
            {
                // 使用反射访问Addressables设置
                // 这是一个更可靠的检查方法，避免直接依赖可能变化的API
                System.Type addressableAssetSettingsType = System.Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettings, Unity.Addressables.Editor");
                if (addressableAssetSettingsType == null)
                {
                    return false;
                }
                
                // 获取默认设置
                var getSettingsMethod = addressableAssetSettingsType.GetMethod("GetDefault", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getSettingsMethod == null)
                {
                    return false;
                }
                
                var settings = getSettingsMethod.Invoke(null, null);
                if (settings == null)
                {
                    return false;
                }
                
                // 检查资源是否在设置中
                var findAssetEntryMethod = settings.GetType().GetMethod("FindAssetEntry", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (findAssetEntryMethod == null)
                {
                    return false;
                }
                
                var assetEntry = findAssetEntryMethod.Invoke(settings, new object[] { guid });
                return assetEntry != null;
            }
            catch
            {
                return false;
            }
            #else
            return false;
            #endif
        }
        
        /// <summary>
        /// 检查资源是否被Addressables组引用
        /// </summary>
        private bool IsAssetReferencedByAddressablesGroups(string guid)
        {
            #if UNITY_2021_3_OR_NEWER
            try
            {
                // 获取所有Addressables组
                System.Type addressableAssetSettingsType = System.Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettings, Unity.Addressables.Editor");
                if (addressableAssetSettingsType == null)
                {
                    return false;
                }
                
                var getSettingsMethod = addressableAssetSettingsType.GetMethod("GetDefault", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getSettingsMethod == null)
                {
                    return false;
                }
                
                var settings = getSettingsMethod.Invoke(null, null);
                if (settings == null)
                {
                    return false;
                }
                
                // 获取组列表
                var groupsProperty = settings.GetType().GetProperty("groups", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (groupsProperty == null)
                {
                    return false;
                }
                
                var groups = groupsProperty.GetValue(settings) as System.Collections.IEnumerable;
                if (groups == null)
                {
                    return false;
                }
                
                // 遍历所有组，检查是否包含该资源
                foreach (var group in groups)
                {
                    var entriesProperty = group.GetType().GetProperty("entries", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (entriesProperty == null)
                    {
                        continue;
                    }
                    
                    var entries = entriesProperty.GetValue(group) as System.Collections.IEnumerable;
                    if (entries == null)
                    {
                        continue;
                    }
                    
                    foreach (var entry in entries)
                    {
                        var guidProperty = entry.GetType().GetProperty("guid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (guidProperty == null)
                        {
                            continue;
                        }
                        
                        var entryGuid = guidProperty.GetValue(entry) as string;
                        if (entryGuid == guid)
                        {
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
            #else
            return false;
            #endif
        }
        
        /// <summary>
        /// 检查资源是否在Addressables标签中
        /// </summary>
        private bool IsAssetInAddressablesLabels(string guid)
        {
            #if UNITY_2021_3_OR_NEWER
            try
            {
                // 检查资源是否被任何Addressables标签引用
                // 这是一个简化的检查，实际实现可能需要更复杂的逻辑
                // 对于大多数项目，如果资源在Addressables设置中，它通常会有标签
                
                // 这里可以添加更复杂的标签检查逻辑
                // 由于Addressables API可能因版本而异，这里使用简化实现
                return false;
            }
            catch
            {
                return false;
            }
            #else
            return false;
            #endif
        }
        
        /// <summary>
        /// 检查项目是否启用了Addressables
        /// </summary>
        private bool IsAddressablesEnabled()
        {
            #if UNITY_2021_3_OR_NEWER
            try
            {
                // 检查Addressables包是否已安装
                // 这是一个简化的检查，实际实现可能需要更准确的方法
                return Directory.Exists("Assets/AddressableAssetsData");
            }
            catch
            {
                return false;
            }
            #else
            return false;
            #endif
        }
        
        /// <summary>
        /// 检查资源是否被预制件引用
        /// </summary>
        private bool IsReferencedByPrefabs(string assetPath)
        {
            try
            {
                // 获取所有预制件
                string[] allPrefabs = AssetDatabase.FindAssets("t:Prefab");
                
                foreach (string guid in allPrefabs)
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(prefabPath))
                        continue;
                    
                    // 检查预制件是否引用了该资源
                    string[] dependencies = AssetDatabase.GetDependencies(new string[] { prefabPath }, true);
                    if (dependencies.Contains(assetPath))
                    {
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 被预制件引用: {prefabPath}");
                        return true;
                    }
                }
                
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"检查预制件引用时出错: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 检查资源是否被材质引用
        /// </summary>
        private bool IsReferencedByMaterials(string assetPath)
        {
            try
            {
                // 获取所有材质
                string[] allMaterials = AssetDatabase.FindAssets("t:Material");
                
                foreach (string guid in allMaterials)
                {
                    string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(materialPath))
                        continue;
                    
                    // 检查材质是否引用了该资源
                    string[] dependencies = AssetDatabase.GetDependencies(new string[] { materialPath }, true);
                    if (dependencies.Contains(assetPath))
                    {
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 被材质引用: {materialPath}");
                        return true;
                    }
                }
                
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"检查材质引用时出错: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 检查资源是否被动画控制器引用
        /// </summary>
        private bool IsReferencedByAnimators(string assetPath)
        {
            try
            {
                // 获取所有动画控制器
                string[] allAnimators = AssetDatabase.FindAssets("t:AnimatorController");
                
                foreach (string guid in allAnimators)
                {
                    string animatorPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(animatorPath))
                        continue;
                    
                    // 检查动画控制器是否引用了该资源
                    string[] dependencies = AssetDatabase.GetDependencies(new string[] { animatorPath }, true);
                    if (dependencies.Contains(assetPath))
                    {
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 被动画控制器引用: {animatorPath}");
                        return true;
                    }
                }
                
                // 检查动画剪辑
                string[] allAnimations = AssetDatabase.FindAssets("t:AnimationClip");
                
                foreach (string guid in allAnimations)
                {
                    string animationPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(animationPath))
                        continue;
                    
                    // 检查动画剪辑是否引用了该资源
                    string[] dependencies = AssetDatabase.GetDependencies(new string[] { animationPath }, true);
                    if (dependencies.Contains(assetPath))
                    {
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 被动画剪辑引用: {animationPath}");
                        return true;
                    }
                }
                
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"检查动画控制器引用时出错: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 检查资源是否被脚本化对象引用
        /// </summary>
        private bool IsReferencedByScriptableObjects(string assetPath)
        {
            try
            {
                // 获取所有脚本化对象
                string[] allScriptableObjects = AssetDatabase.FindAssets("t:ScriptableObject");
                
                foreach (string guid in allScriptableObjects)
                {
                    string soPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(soPath))
                        continue;
                    
                    // 跳过非.asset文件（如.cs文件）
                    if (!soPath.EndsWith(".asset"))
                        continue;
                    
                    // 检查脚本化对象是否引用了该资源
                    string[] dependencies = AssetDatabase.GetDependencies(new string[] { soPath }, true);
                    if (dependencies.Contains(assetPath))
                    {
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 被脚本化对象引用: {soPath}");
                        return true;
                    }
                }
                
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"检查脚本化对象引用时出错: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 检查资源是否在代码中被引用（增强版）
        /// </summary>
        private bool IsReferencedInCode(string assetPath)
        {
            try
            {
                // 获取资源的文件名（不含扩展名）和完整路径
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string assetName = Path.GetFileName(assetPath);
                
                // 扫描项目中的所有C#脚本
                string[] allScripts = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);
                
                // 并行处理以提高性能（对于大型项目）
                bool foundReference = false;
                System.Threading.Tasks.Parallel.ForEach(allScripts, (scriptPath, state) =>
                {
                    // 跳过编辑器脚本（可选）
                    if (scriptPath.Contains("/Editor/") && !scriptPath.Contains("Assets/Editor/VicTools/"))
                        return;
                    
                    // 检查脚本是否引用了该资源
                    if (CheckScriptForResourceReference(scriptPath, assetPath, fileName, assetName))
                    {
                        foundReference = true;
                        state.Stop(); // 找到引用后停止搜索
                    }
                });
                
                return foundReference;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"检查代码引用时出错: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 检查脚本是否引用了指定资源（增强版）
        /// </summary>
        private bool CheckScriptForResourceReference(string scriptPath, string assetPath, string fileName, string assetName)
        {
            try
            {
                // 读取脚本内容
                string scriptContent = File.ReadAllText(scriptPath);
                
                // 检查常见的资源加载模式
                
                // 1. Resources.Load 调用（支持各种重载）
                if (scriptContent.Contains("Resources.Load"))
                {
                    // 检查是否包含资源文件名（带或不带扩展名）
                    if (scriptContent.Contains($"\"{fileName}\"") || 
                        scriptContent.Contains($"'{fileName}'") ||
                        scriptContent.Contains($"\"{assetName}\"") ||
                        scriptContent.Contains($"'{assetName}'"))
                    {
                        // 进一步检查路径模式
                        string relativePath = GetRelativeResourcePath(assetPath);
                        if (!string.IsNullOrEmpty(relativePath) && scriptContent.Contains(relativePath))
                        {
                            Debug.Log($"资源 {Path.GetFileName(assetPath)} 在代码中被引用: {Path.GetFileName(scriptPath)} (Resources.Load)");
                            return true;
                        }
                        
                        // 如果脚本包含文件名，也认为是引用
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 在代码中被引用: {Path.GetFileName(scriptPath)} (文件名匹配)");
                        return true;
                    }
                }
                
                // 2. AssetDatabase.LoadAssetAtPath 调用
                if (scriptContent.Contains("AssetDatabase.LoadAssetAtPath"))
                {
                    if (scriptContent.Contains($"\"{assetPath}\"") || 
                        scriptContent.Contains($"'{assetPath}'"))
                    {
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 在代码中被引用: {Path.GetFileName(scriptPath)} (AssetDatabase.LoadAssetAtPath)");
                        return true;
                    }
                }
                
                // 3. Resources.LoadAll 调用（可能引用文件夹）
                if (scriptContent.Contains("Resources.LoadAll"))
                {
                    // 检查资源是否在Resources文件夹中
                    if (assetPath.Contains("/Resources/"))
                    {
                        string resourcesPath = GetResourcesRelativePath(assetPath);
                        if (!string.IsNullOrEmpty(resourcesPath) && scriptContent.Contains(resourcesPath))
                        {
                            Debug.Log($"资源 {Path.GetFileName(assetPath)} 在代码中被引用: {Path.GetFileName(scriptPath)} (Resources.LoadAll)");
                            return true;
                        }
                    }
                }
                
                // 4. Addressables.LoadAssetAsync 调用
                if (scriptContent.Contains("Addressables.LoadAssetAsync"))
                {
                    // 检查是否包含资源地址
                    string address = GetAddressableAddress(assetPath);
                    if (!string.IsNullOrEmpty(address) && scriptContent.Contains($"\"{address}\""))
                    {
                        Debug.Log($"资源 {Path.GetFileName(assetPath)} 在代码中被引用: {Path.GetFileName(scriptPath)} (Addressables.LoadAssetAsync)");
                        return true;
                    }
                }
                
                // 5. 序列化字段引用（通过[SerializeField]属性）
                // 检查是否有对资源类型的字段声明
                if (IsReferencedBySerializedField(scriptContent, assetPath, fileName))
                {
                    Debug.Log($"资源 {Path.GetFileName(assetPath)} 在代码中被引用: {Path.GetFileName(scriptPath)} (序列化字段)");
                    return true;
                }
                
                // 6. 字符串常量引用（资源路径作为字符串常量）
                if (scriptContent.Contains($"\"{assetPath}\"") || scriptContent.Contains($"'{assetPath}'"))
                {
                    Debug.Log($"资源 {Path.GetFileName(assetPath)} 在代码中被引用: {Path.GetFileName(scriptPath)} (字符串常量)");
                    return true;
                }
                
                // 7. 检查资源是否在注释中被引用（跳过注释）
                // 这里需要更复杂的解析，但为了性能，我们假设注释中的引用不是真正的引用
                
                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
        
        /// <summary>
        /// 检查资源是否被序列化字段引用
        /// </summary>
        private bool IsReferencedBySerializedField(string scriptContent, string assetPath, string fileName)
        {
            // 这是一个简化的检查，实际实现可能需要更复杂的解析
            // 检查常见的序列化字段模式
            
            // 1. [SerializeField] private Texture2D myTexture;
            // 2. public Sprite mySprite;
            // 3. [SerializeField] private Material myMaterial;
            
            // 获取文件扩展名以确定资源类型
            string extension = Path.GetExtension(assetPath).ToLower();
            
            // 根据资源类型检查对应的字段声明
            switch (extension)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".bmp":
                    // 检查Texture2D字段
                    if (scriptContent.Contains("Texture2D") && scriptContent.Contains(fileName))
                        return true;
                    break;
                    
                case ".mat":
                    // 检查Material字段
                    if (scriptContent.Contains("Material") && scriptContent.Contains(fileName))
                        return true;
                    break;
                    
                case ".prefab":
                    // 检查GameObject字段
                    if (scriptContent.Contains("GameObject") && scriptContent.Contains(fileName))
                        return true;
                    break;
                    
                case ".wav":
                case ".mp3":
                case ".ogg":
                    // 检查AudioClip字段
                    if (scriptContent.Contains("AudioClip") && scriptContent.Contains(fileName))
                        return true;
                    break;
            }
            
            return false;
        }
        
        /// <summary>
        /// 获取Addressables资源地址
        /// </summary>
        private string GetAddressableAddress(string assetPath)
        {
            #if UNITY_2021_3_OR_NEWER
            try
            {
                // 使用反射获取Addressables地址
                System.Type addressableAssetSettingsType = System.Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettings, Unity.Addressables.Editor");
                if (addressableAssetSettingsType == null)
                    return null;
                
                var getSettingsMethod = addressableAssetSettingsType.GetMethod("GetDefault", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getSettingsMethod == null)
                    return null;
                
                var settings = getSettingsMethod.Invoke(null, null);
                if (settings == null)
                    return null;
                
                // 获取资源的GUID
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    return null;
                
                // 查找资源条目
                var findAssetEntryMethod = settings.GetType().GetMethod("FindAssetEntry", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (findAssetEntryMethod == null)
                    return null;
                
                var assetEntry = findAssetEntryMethod.Invoke(settings, new object[] { guid });
                if (assetEntry == null)
                    return null;
                
                // 获取地址
                var addressProperty = assetEntry.GetType().GetProperty("address", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (addressProperty == null)
                    return null;
                
                return addressProperty.GetValue(assetEntry) as string;
            }
            catch
            {
                return null;
            }
            #else
            return null;
            #endif
        }
        
        /// <summary>
        /// 获取资源相对于Resources文件夹的路径
        /// </summary>
        private string GetRelativeResourcePath(string assetPath)
        {
            if (!assetPath.Contains("/Resources/"))
                return null;
                
            int resourcesIndex = assetPath.IndexOf("/Resources/") + 11; // +11 跳过 "/Resources/"
            if (resourcesIndex >= assetPath.Length)
                return null;
                
            string pathInResources = assetPath.Substring(resourcesIndex);
            // 移除文件扩展名（Resources.Load不需要扩展名）
            return Path.Combine(Path.GetDirectoryName(pathInResources), Path.GetFileNameWithoutExtension(pathInResources))
                   .Replace("\\", "/"); // 统一使用正斜杠
        }
        
        /// <summary>
        /// 获取Resources文件夹相对路径
        /// </summary>
        private string GetResourcesRelativePath(string assetPath)
        {
            if (!assetPath.Contains("/Resources/"))
                return null;
                
            int resourcesIndex = assetPath.IndexOf("/Resources/");
            if (resourcesIndex < 0)
                return null;
                
            // 获取Resources文件夹及其之后的路径
            string resourcesAndAfter = assetPath.Substring(resourcesIndex);
            
            // 找到Resources文件夹后的第一个斜杠
            int slashAfterResources = resourcesAndAfter.IndexOf('/', 1);
            if (slashAfterResources > 0)
            {
                // 返回Resources文件夹后的路径部分
                return resourcesAndAfter.Substring(slashAfterResources + 1);
            }
            
            return null;
        }
        
        /// <summary>
        /// 备用检查逻辑（当无法获取构建场景时使用）
        /// </summary>
        private bool IsResourceUsedFallback(string assetPath)
        {
            // 获取引用此资源的所有资源
            string[] referencingAssets = AssetDatabase.GetDependencies(new string[] { assetPath }, true);
            
            // 如果只有自己引用自己，说明没有被其他资源引用
            if (referencingAssets.Length <= 1)
                return false;
            
            // 检查是否被场景引用
            foreach (string referencingAsset in referencingAssets)
            {
                // 跳过自己
                if (referencingAsset == assetPath)
                    continue;
                    
                // 检查引用资源是否是场景文件
                if (referencingAsset.EndsWith(".unity"))
                    return true;
                    
                // 检查引用资源是否是预制件
                if (referencingAsset.EndsWith(".prefab"))
                    return true;
                    
                // 检查引用资源是否是材质
                if (referencingAsset.EndsWith(".mat"))
                    return true;
                    
                // 检查引用资源是否是ScriptableObject
                if (referencingAsset.EndsWith(".asset"))
                    return true;
            }
            
            // 检查是否被Resources文件夹中的资源引用（这些资源在打包时会被包含）
            if (assetPath.Contains("/Resources/"))
                return true;
                
            // 检查是否被StreamingAssets文件夹中的资源引用
            if (assetPath.Contains("/StreamingAssets/"))
                return true;
                
            // 检查是否被Editor文件夹中的资源引用（编辑器专用资源，打包时不会包含）
            if (assetPath.Contains("/Editor/"))
                return false;
                
            // 默认认为未被使用
            return false;
        }
        
        /// <summary>
        /// 检查资源A是否引用了资源B
        /// </summary>
        private bool IsAssetReferencedBy(string assetPathA, string assetPathB)
        {
            if (assetPathA == assetPathB)
                return true;
                
            try
            {
                string[] dependencies = AssetDatabase.GetDependencies(new string[] { assetPathA }, true);
                return dependencies.Contains(assetPathB);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取资源类型
        /// </summary>
        private string GetResourceType(string fileExtension)
        {
            switch (fileExtension)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".bmp":
                case ".psd":
                case ".tif":
                case ".tiff":
                    return "Texture";
                case ".mat":
                    return "Material";
                case ".fbx":
                case ".obj":
                case ".blend":
                case ".max":
                case ".ma":
                case ".mb":
                    return "Model";
                case ".prefab":
                    return "Prefab";
                case ".anim":
                case ".controller":
                    return "Animation";
                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".aiff":
                    return "Audio";
                case ".asset":
                    return "ScriptableObject";
                default:
                    return "Other";
            }
        }

        /// <summary>
        /// 选择资源
        /// </summary>
        private void SelectResource(string resourcePath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resourcePath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                Debug.Log($"已选择资源: {Path.GetFileName(resourcePath)}");
            }
        }

        /// <summary>
        /// 删除资源
        /// </summary>
        private void DeleteResource(string resourcePath)
        {
            if (EditorUtility.DisplayDialog("确认删除", 
                $"确定要删除资源 '{Path.GetFileName(resourcePath)}' 吗？\n\n此操作无法撤销。", 
                "删除", "取消"))
            {
                if (AssetDatabase.DeleteAsset(resourcePath))
                {
                    Debug.Log($"已删除资源: {resourcePath}");
                    _unusedResources.Remove(resourcePath);
                    
                    // 更新按类型统计
                    string fileExtension = Path.GetExtension(resourcePath).ToLower();
                    string resourceType = GetResourceType(fileExtension);
                    if (_unusedResourcesByType.ContainsKey(resourceType))
                    {
                        _unusedResourcesByType[resourceType].Remove(resourcePath);
                        if (_unusedResourcesByType[resourceType].Count == 0)
                            _unusedResourcesByType.Remove(resourceType);
                    }
                }
                else
                {
                    Debug.LogError($"删除资源失败: {resourcePath}");
                }
            }
        }

        /// <summary>
        /// 选择所有未使用资源
        /// </summary>
        private void SelectAllUnusedResources()
        {
            List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
            
            foreach (string resourcePath in _unusedResources)
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resourcePath);
                if (asset != null)
                    assets.Add(asset);
            }
            
            if (assets.Count > 0)
            {
                Selection.objects = assets.ToArray();
                EditorGUIUtility.PingObject(assets[0]);
                Debug.Log($"已选择 {assets.Count} 个未使用资源");
            }
            else
            {
                Debug.Log("没有可选择的未使用资源");
            }
        }

        /// <summary>
        /// 删除所有未使用资源
        /// </summary>
        private void DeleteAllUnusedResources()
        {
            int count = _unusedResources.Count;
            if (count == 0)
            {
                Debug.Log("没有未使用资源可删除");
                return;
            }
            
            if (EditorUtility.DisplayDialog("确认删除所有", 
                $"确定要删除所有 {count} 个未使用资源吗？\n\n此操作无法撤销。", 
                "删除所有", "取消"))
            {
                int deletedCount = 0;
                List<string> resourcesToDelete = new List<string>(_unusedResources);
                
                foreach (string resourcePath in resourcesToDelete)
                {
                    if (AssetDatabase.DeleteAsset(resourcePath))
                    {
                        deletedCount++;
                    }
                }
                
                _unusedResources.Clear();
                _unusedResourcesByType.Clear();
                
                Debug.Log($"已删除 {deletedCount} 个未使用资源");
            }
        }

        /// <summary>
        /// 获取筛选后的资源数量
        /// </summary>
        private int GetFilteredResourcesCount()
        {
            if (_unusedResources.Count == 0 || _resourceTypeFilters.Count == 0)
                return _unusedResources.Count;
            
            int count = 0;
            foreach (var resourcePath in _unusedResources)
            {
                string fileExtension = Path.GetExtension(resourcePath).ToLower();
                string resourceType = GetResourceType(fileExtension);
                
                if (_resourceTypeFilters.ContainsKey(resourceType) && _resourceTypeFilters[resourceType])
                {
                    count++;
                }
            }
            
            return count;
        }

        /// <summary>
        /// 根据警告级别获取颜色
        /// </summary>
        private Color GetWarningColor(PerformanceWarningLevel level)
        {
            switch (level)
            {
                case PerformanceWarningLevel.Critical:
                    return Color.red;
                case PerformanceWarningLevel.Warning:
                    return Color.yellow;
                case PerformanceWarningLevel.Info:
                    return Color.cyan;
                default:
                    return Color.white;
            }
        }

        /// <summary>
        /// 加载模块显示开关的存档设置
        /// </summary>
        private void LoadModuleToggleStates()
        {
            string prefix = "VicTools.ScenePerformanceAnalyzer.";
            
            _showSelectedObjectInfo = EditorPrefs.GetBool(prefix + "ShowSelectedObjectInfo", true);
            _showSceneInfo = EditorPrefs.GetBool(prefix + "ShowSceneInfo", true);
            _showMemoryUsage = EditorPrefs.GetBool(prefix + "ShowMemoryUsage", true);
            _showObjectStatistics = EditorPrefs.GetBool(prefix + "ShowObjectStatistics", true);
            _showGlobalIllumination = EditorPrefs.GetBool(prefix + "ShowGlobalIllumination", true);
            _showPerformanceWarnings = EditorPrefs.GetBool(prefix + "ShowPerformanceWarnings", true);
            _showDetailedStatisticsSection = EditorPrefs.GetBool(prefix + "ShowDetailedStatisticsSection", true);
            _showResourceUtilization = EditorPrefs.GetBool(prefix + "ShowResourceUtilization", true);
        }

        /// <summary>
        /// 保存模块显示开关的存档设置
        /// </summary>
        private void SaveModuleToggleStates()
        {
            string prefix = "VicTools.ScenePerformanceAnalyzer.";
            
            EditorPrefs.SetBool(prefix + "ShowSelectedObjectInfo", _showSelectedObjectInfo);
            EditorPrefs.SetBool(prefix + "ShowSceneInfo", _showSceneInfo);
            EditorPrefs.SetBool(prefix + "ShowMemoryUsage", _showMemoryUsage);
            EditorPrefs.SetBool(prefix + "ShowObjectStatistics", _showObjectStatistics);
            EditorPrefs.SetBool(prefix + "ShowGlobalIllumination", _showGlobalIllumination);
            EditorPrefs.SetBool(prefix + "ShowPerformanceWarnings", _showPerformanceWarnings);
            EditorPrefs.SetBool(prefix + "ShowDetailedStatisticsSection", _showDetailedStatisticsSection);
            EditorPrefs.SetBool(prefix + "ShowResourceUtilization", _showResourceUtilization);
        }

        /// <summary>
        /// 保存排除列表设置到EditorPrefs
        /// </summary>
        private void SaveExclusionSettings()
        {
            string prefix = "VicTools.ScenePerformanceAnalyzer.Exclusion.";
            
            // 保存排除路径列表
            string excludedPathsJson = JsonUtility.ToJson(new StringListWrapper(_excludedPaths));
            EditorPrefs.SetString(prefix + "Paths", excludedPathsJson);
            
            // 保存排除模式列表
            string excludedPatternsJson = JsonUtility.ToJson(new StringListWrapper(_excludedPatterns));
            EditorPrefs.SetString(prefix + "Patterns", excludedPatternsJson);
            
            Debug.Log("排除列表设置已保存");
        }

        /// <summary>
        /// 从EditorPrefs加载排除列表设置
        /// </summary>
        private void LoadExclusionSettings()
        {
            string prefix = "VicTools.ScenePerformanceAnalyzer.Exclusion.";
            
            // 加载排除路径列表
            string excludedPathsJson = EditorPrefs.GetString(prefix + "Paths", "");
            if (!string.IsNullOrEmpty(excludedPathsJson))
            {
                StringListWrapper wrapper = JsonUtility.FromJson<StringListWrapper>(excludedPathsJson);
                if (wrapper != null && wrapper.list != null)
                {
                    _excludedPaths.Clear();
                    _excludedPaths.AddRange(wrapper.list);
                }
            }
            
            // 加载排除模式列表
            string excludedPatternsJson = EditorPrefs.GetString(prefix + "Patterns", "");
            if (!string.IsNullOrEmpty(excludedPatternsJson))
            {
                StringListWrapper wrapper = JsonUtility.FromJson<StringListWrapper>(excludedPatternsJson);
                if (wrapper != null && wrapper.list != null)
                {
                    _excludedPatterns.Clear();
                    _excludedPatterns.AddRange(wrapper.list);
                }
            }
            
            Debug.Log($"排除列表设置已加载: {_excludedPaths.Count} 个路径, {_excludedPatterns.Count} 个模式");
        }

        /// <summary>
        /// 检查资源是否在排除列表中
        /// </summary>
        private bool IsResourceExcluded(string assetPath)
        {
            // 检查路径排除
            foreach (string excludedPath in _excludedPaths)
            {
                if (!string.IsNullOrEmpty(excludedPath) && assetPath.StartsWith(excludedPath))
                {
                    Debug.Log($"资源 {Path.GetFileName(assetPath)} 在排除路径中: {excludedPath}");
                    return true;
                }
            }
            
            // 检查模式排除（通配符）
            foreach (string pattern in _excludedPatterns)
            {
                if (!string.IsNullOrEmpty(pattern) && MatchesWildcardPattern(assetPath, pattern))
                {
                    Debug.Log($"资源 {Path.GetFileName(assetPath)} 匹配排除模式: {pattern}");
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 检查字符串是否匹配通配符模式
        /// </summary>
        private bool MatchesWildcardPattern(string input, string pattern)
        {
            try
            {
                // 将通配符模式转换为正则表达式
                string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                
                return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                // 如果正则表达式转换失败，使用简单的包含检查
                return input.Contains(pattern);
            }
        }

        /// <summary>
        /// 添加Project窗口中选中的目录到排除列表
        /// </summary>
        private void AddSelectedProjectFolderToExclusion()
        {
            // 获取Project窗口中选中的对象
            UnityEngine.Object[] selectedObjects = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
            
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请在Project窗口中选择一个或多个目录", "确定");
                return;
            }
            
            int addedCount = 0;
            List<string> addedPaths = new List<string>();
            
            foreach (UnityEngine.Object obj in selectedObjects)
            {
                // 检查选中的对象是否是目录（DefaultAsset类型通常表示文件夹）
                if (obj is DefaultAsset)
                {
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    
                    // 确保是有效的目录路径
                    if (AssetDatabase.IsValidFolder(assetPath))
                    {
                        // 检查是否已经在排除列表中
                        if (!_excludedPaths.Contains(assetPath))
                        {
                            _excludedPaths.Add(assetPath);
                            addedPaths.Add(assetPath);
                            addedCount++;
                        }
                        else
                        {
                            Debug.Log($"目录已在排除列表中: {assetPath}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"选中的对象不是有效目录: {assetPath}");
                    }
                }
                else
                {
                    // 如果不是目录，尝试获取其所在目录
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        string directoryPath = Path.GetDirectoryName(assetPath);
                        if (!string.IsNullOrEmpty(directoryPath) && !_excludedPaths.Contains(directoryPath))
                        {
                            _excludedPaths.Add(directoryPath);
                            addedPaths.Add(directoryPath);
                            addedCount++;
                        }
                    }
                }
            }
            
            if (addedCount > 0)
            {
                // 保存设置
                SaveExclusionSettings();
                
                // 显示成功消息
                // string message = $"已添加 {addedCount} 个目录到排除列表:\n\n";
                // foreach (string path in addedPaths)
                // {
                //     message += $"• {path}\n";
                // }
                
                // EditorUtility.DisplayDialog("成功", message, "确定");
                Debug.Log($"已添加 {addedCount} 个目录到排除列表");
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "没有找到可添加的新目录", "确定");
            }
        }
    }

    /// <summary>
    /// 性能数据类
    /// </summary>
    [System.Serializable]
    public class PerformanceData
    {
        public int totalObjects;
        public int meshCount;
        public int materialCount;
        public int textureCount;
        public int lightCount;
        public int cameraCount;
        public int particleSystemCount;
        public int colliderCount;
        public int rigidbodyCount;
        public int audioSourceCount;
        public int contributeGICount; // 开启全局光照贡献的模型数量
        public int totalTriangles;
        public int totalVertices;
        public long textureMemory;
    }

    /// <summary>
    /// 性能警告类
    /// </summary>
    [System.Serializable]
    public class PerformanceWarning
    {
        public string title;
        public string message;
        public PerformanceWarningLevel level;

        public PerformanceWarning(string title, string message, PerformanceWarningLevel level)
        {
            this.title = title;
            this.message = message;
            this.level = level;
        }
    }

    /// <summary>
    /// 性能警告级别
    /// </summary>
    public enum PerformanceWarningLevel
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>
    /// 资源缓存条目 - 智能缓存系统的核心数据结构
    /// </summary>
    internal class ResourceCacheEntry
    {
        /// <summary>
        /// 资源是否被使用
        /// </summary>
        public bool IsUsed { get; set; }
        
        /// <summary>
        /// 缓存创建时间
        /// </summary>
        public float CreationTime { get; set; }
        
        /// <summary>
        /// 资源文件最后修改时间（用于检测文件变更）
        /// </summary>
        public System.DateTime FileLastWriteTime { get; set; }
        
        /// <summary>
        /// 检查缓存是否过期
        /// </summary>
        public bool IsExpired(float currentTime)
        {
            return (currentTime - CreationTime) > ScenePerformanceAnalyzer.CACHE_EXPIRY_TIME;
        }
        
        /// <summary>
        /// 文件是否已修改（需要重新检查）
        /// </summary>
        public bool IsFileModified(string assetPath)
        {
            try
            {
                if (!System.IO.File.Exists(assetPath))
                    return true; // 文件不存在，需要重新检查
                    
                System.DateTime currentFileTime = System.IO.File.GetLastWriteTime(assetPath);
                return currentFileTime != FileLastWriteTime;
            }
            catch
            {
                return true; // 如果无法获取文件时间，假设文件已修改
            }
        }
        
        /// <summary>
        /// 创建新的缓存条目
        /// </summary>
        public static ResourceCacheEntry Create(bool isUsed, string assetPath)
        {
            System.DateTime fileTime = System.DateTime.MinValue;
            try
            {
                if (System.IO.File.Exists(assetPath))
                {
                    fileTime = System.IO.File.GetLastWriteTime(assetPath);
                }
            }
            catch
            {
                // 忽略异常，使用默认时间
            }
            
            return new ResourceCacheEntry
            {
                IsUsed = isUsed,
                CreationTime = UnityEngine.Time.realtimeSinceStartup,
                FileLastWriteTime = fileTime
            };
        }
    }

    /// <summary>
    /// 字符串列表包装器 - 用于JSON序列化
    /// </summary>
    [System.Serializable]
    public class StringListWrapper
    {
        public List<string> list;
        
        public StringListWrapper() { }
        
        public StringListWrapper(List<string> list)
        {
            this.list = list;
        }
    }
}

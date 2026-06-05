using UnityEngine;

/// 雪地凹陷管理器 v2.1 (2026.05.28)
/// - 新增PaintRect方法，支持矩形画笔绘制
/// v2.0 (2026.05.27)
/// - 移除maxDeformDepth，凹陷深度完全由sinkDepth(雪层厚度)全局控制
/// - sinkDepth同时驱动雪面上抬和凹陷最大深度

/// 雪地凹陷管理器（全局唯一）
/// 管理 RT、全局共用参数、痕迹恢复
[ExecuteAlways]
public class SnowDeformManager : MonoBehaviour
{
    [Header("RT 设置")]
    [Tooltip("RT 分辨率（移动端建议 1024）")]
    public int rtResolution = 1024;

    [Header("投影区域")]
    [Tooltip("自动从指定的雪地 Renderer 获取投影范围（优先使用）")]
    public Renderer snowRenderer;

    [Tooltip("RT 覆盖的世界空间中心点（XZ平面），如果指定了 snowRenderer 则自动计算")]
    public Vector2 areaCenter = Vector2.zero;

    [Tooltip("RT 覆盖的世界空间范围大小（正方形边长，米），如果指定了 snowRenderer 则自动计算")]
    public float areaSize = 20f;

    [Header("凹陷全局参数")]
    [Tooltip("雪层厚度（米），同时控制雪面上抬和凹陷最大深度，所有Foot的凹陷深度都受此影响")]
    [Range(0f, 0.1f)]
    public float sinkDepth = 0.005f;

    [Tooltip("凹陷区域颜色变暗程度")]
    [Range(0f, 2f)]
    public float deformDarken = 0.38f;

    [Tooltip("画笔边缘柔和度")]
    [Range(0f, 1f)]
    public float brushSoftness = 0.95f;

    [Header("网格细分")]
    [Tooltip("运行时对雪地Mesh进行细分的次数（0=不细分，1=4倍面数，2=16倍面数）")]
    [Range(0, 2)]
    public int subdivisionLevel = 0;

    [Header("痕迹恢复")]
    [Tooltip("启用痕迹渐渐恢复")]
    public bool enableFade = true;

    [Tooltip("恢复速度（每秒衰减量）")]
    [Range(0.001f, 0.5f)]
    public float fadeSpeed = 0.04f;

    [Tooltip("衰减执行间隔（秒），避免高帧率下精度累积误差")]
    [Range(0.016f, 0.5f)]
    public float fadeInterval = 0.1f;

    [Header("检测设置")]
    [Tooltip("雪地地形 Layer")]
    public LayerMask snowLayer = ~0;

    [Header("RT 预览")]
    [Tooltip("在 Scene 视图中显示 RT 预览窗口")]
    public bool showRTPreview = true;

    [Tooltip("预览窗口大小（像素）")]
    [Range(128, 512)]
    public int previewSize = 256;

    [Tooltip("预览窗口位置（屏幕左下角偏移）")]
    public Vector2 previewOffset = new Vector2(10, 10);

    [Header("Shader 引用")]
    [Tooltip("必须手动拖入，打包后 Shader.Find 无法找到 Hidden shader")]
    [SerializeField] private Shader paintShader;
    [SerializeField] private Shader fadeShader;

    public RenderTexture SnowRT { get; private set; }

    private Material _paintMaterial;
    private Material _fadeMaterial;
    private RenderTexture _tempRT;
    private float _fadeTimer;

    private static readonly int _SnowDeformRT_ID = Shader.PropertyToID("_SnowDeformRT");
    private static readonly int _SnowDeformDepth_ID = Shader.PropertyToID("_SnowDeformDepth");
    private static readonly int _SnowDeformDarken_ID = Shader.PropertyToID("_SnowDeformDarken");
    private static readonly int _SnowAreaCenter_ID = Shader.PropertyToID("_SnowAreaCenter");
    private static readonly int _SnowAreaSize_ID = Shader.PropertyToID("_SnowAreaSize");
    private static readonly int _SnowSinkDepth_ID = Shader.PropertyToID("_SnowSinkDepth");

    // Paint shader property IDs（缓存避免字符串查找）
    private static readonly int _BrushPosA_ID = Shader.PropertyToID("_BrushPosA");
    private static readonly int _BrushPosB_ID = Shader.PropertyToID("_BrushPosB");
    private static readonly int _BrushSize_ID = Shader.PropertyToID("_BrushSize");
    private static readonly int _BrushStrength_ID = Shader.PropertyToID("_BrushStrength");
    private static readonly int _BrushSoftness_ID = Shader.PropertyToID("_BrushSoftness");
    private static readonly int _BrushFeather_ID = Shader.PropertyToID("_BrushFeather");
    private static readonly int _BrushShape_ID = Shader.PropertyToID("_BrushShape");
    private static readonly int _RectLength_ID = Shader.PropertyToID("_RectLength");
    private static readonly int _RectAngle_ID = Shader.PropertyToID("_RectAngle");
    private static readonly int _ExistingTex_ID = Shader.PropertyToID("_ExistingTex");
    private static readonly int _FadeAmount_ID = Shader.PropertyToID("_FadeAmount");
    private static readonly int _MainTex_ID = Shader.PropertyToID("_MainTex");

    private Mesh _originalMesh; // 保存原始Mesh用于恢复
    private bool _subdivisionApplied;

    void OnEnable()
    {
        UpdateAreaFromRenderer();
        CreateResources();
        UpdateGlobalShaderParams();
        
        if (Application.isPlaying && subdivisionLevel > 0 && !_subdivisionApplied)
        {
            ApplySubdivision();
        }
    }

    void OnDisable()
    {
        Shader.SetGlobalTexture(_SnowDeformRT_ID, Texture2D.blackTexture);
        ReleaseResources();
    }

    void Update()
    {
        // 只在编辑器或参数变化时更新区域（运行时snowRenderer不会移动）
        #if UNITY_EDITOR
        UpdateAreaFromRenderer();
        #endif

        if (enableFade && Application.isPlaying && SnowRT != null && _fadeMaterial != null)
        {
            _fadeTimer += Time.deltaTime;
            if (_fadeTimer >= fadeInterval)
            {
                float fadeAmount = fadeSpeed * _fadeTimer;
                _fadeTimer = 0f;

                _fadeMaterial.SetFloat(_FadeAmount_ID, fadeAmount);
                Graphics.Blit(SnowRT, _tempRT);
                _fadeMaterial.SetTexture(_MainTex_ID, _tempRT);
                Graphics.Blit(_tempRT, SnowRT, _fadeMaterial, 0);
            }
        }
    }

    private void UpdateAreaFromRenderer()
    {
        if (snowRenderer == null) return;
        Bounds b = snowRenderer.bounds;
        float newSize = Mathf.Max(b.size.x, b.size.z);
        Vector2 newCenter = new Vector2(b.center.x, b.center.z);
        
        // 只在值变化时更新全局shader参数
        if (newCenter != areaCenter || !Mathf.Approximately(newSize, areaSize))
        {
            areaCenter = newCenter;
            areaSize = newSize;
            UpdateGlobalShaderParams();
        }
    }

    private void ApplyColliderSink()
    {
        // sinkDepth 通过 shader 全局参数实现
    }

    /// 对snowRenderer的Mesh执行细分
    private void ApplySubdivision()
    {
        if (snowRenderer == null) return;
        
        MeshFilter meshFilter = snowRenderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;
        
        // 保存原始Mesh
        _originalMesh = meshFilter.sharedMesh;
        
        // 复制Mesh进行细分（不修改原始资源）
        Mesh subdividedMesh = Instantiate(_originalMesh);
        subdividedMesh.name = _originalMesh.name + "_Subdivided";
        
        for (int i = 0; i < subdivisionLevel; i++)
        {
            subdividedMesh = SubdivideMesh(subdividedMesh);
        }
        
        subdividedMesh.RecalculateBounds();
        meshFilter.mesh = subdividedMesh;
        _subdivisionApplied = true;
        
        // 更新MeshCollider（如果有）
        MeshCollider meshCollider = snowRenderer.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = subdividedMesh;
        }
        
        Debug.Log($"[SnowDeformManager] 网格细分完成: {_originalMesh.triangles.Length / 3} → {subdividedMesh.triangles.Length / 3} 三角面");
    }
    
    /// 中点细分：每个三角形分为4个
    private static Mesh SubdivideMesh(Mesh source)
    {
        var vertices = source.vertices;
        var normals = source.normals;
        var uvs = source.uv;
        var triangles = source.triangles;
        
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        bool hasUVs = uvs != null && uvs.Length == vertices.Length;
        
        // 用字典缓存边中点，避免重复创建
        var edgeMidpoints = new System.Collections.Generic.Dictionary<long, int>();
        var newVertices = new System.Collections.Generic.List<Vector3>(vertices);
        var newNormals = new System.Collections.Generic.List<Vector3>(hasNormals ? normals : new Vector3[vertices.Length]);
        var newUVs = new System.Collections.Generic.List<Vector2>(hasUVs ? uvs : new Vector2[vertices.Length]);
        var newTriangles = new System.Collections.Generic.List<int>();
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = triangles[i];
            int v1 = triangles[i + 1];
            int v2 = triangles[i + 2];
            
            // 获取或创建三条边的中点
            int m01 = GetOrCreateMidpoint(v0, v1, edgeMidpoints, newVertices, newNormals, newUVs, vertices, normals, uvs, hasNormals, hasUVs);
            int m12 = GetOrCreateMidpoint(v1, v2, edgeMidpoints, newVertices, newNormals, newUVs, vertices, normals, uvs, hasNormals, hasUVs);
            int m20 = GetOrCreateMidpoint(v2, v0, edgeMidpoints, newVertices, newNormals, newUVs, vertices, normals, uvs, hasNormals, hasUVs);
            
            // 4个子三角形
            newTriangles.Add(v0);  newTriangles.Add(m01); newTriangles.Add(m20);
            newTriangles.Add(m01); newTriangles.Add(v1);  newTriangles.Add(m12);
            newTriangles.Add(m20); newTriangles.Add(m12); newTriangles.Add(v2);
            newTriangles.Add(m01); newTriangles.Add(m12); newTriangles.Add(m20);
        }
        
        var result = new Mesh();
        result.indexFormat = newVertices.Count > 65535 
            ? UnityEngine.Rendering.IndexFormat.UInt32 
            : UnityEngine.Rendering.IndexFormat.UInt16;
        result.SetVertices(newVertices);
        if (hasNormals) result.SetNormals(newNormals);
        if (hasUVs) result.SetUVs(0, newUVs);
        result.SetTriangles(newTriangles, 0);
        result.RecalculateBounds();
        if (!hasNormals) result.RecalculateNormals();
        return result;
    }
    
    private static int GetOrCreateMidpoint(
        int a, int b,
        System.Collections.Generic.Dictionary<long, int> cache,
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> norms,
        System.Collections.Generic.List<Vector2> uvs,
        Vector3[] srcVerts, Vector3[] srcNorms, Vector2[] srcUVs,
        bool hasNormals, bool hasUVs)
    {
        // 用排序后的顶点索引对作为key，确保边(a,b)和(b,a)共享同一个中点
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        
        if (cache.TryGetValue(key, out int midIndex))
            return midIndex;
        
        midIndex = verts.Count;
        verts.Add((srcVerts[a] + srcVerts[b]) * 0.5f);
        
        if (hasNormals)
            norms.Add(((srcNorms[a] + srcNorms[b]) * 0.5f).normalized);
        else
            norms.Add(Vector3.up);
            
        if (hasUVs)
            uvs.Add((srcUVs[a] + srcUVs[b]) * 0.5f);
        else
            uvs.Add(Vector2.zero);
        
        cache[key] = midIndex;
        return midIndex;
    }

    /// 由 FootprintMarker 调用：绘制从 uvA 到 uvB 的线段痕迹（一次 Blit）
    public void PaintLine(Vector2 uvA, Vector2 uvB, float brushSize, float brushStrength, float feather = 0.5f)
    {
        if (SnowRT == null || _paintMaterial == null) return;

        _paintMaterial.SetVector(_BrushPosA_ID, new Vector4(uvA.x, uvA.y, 0, 0));
        _paintMaterial.SetVector(_BrushPosB_ID, new Vector4(uvB.x, uvB.y, 0, 0));
        _paintMaterial.SetFloat(_BrushSize_ID, brushSize);
        _paintMaterial.SetFloat(_BrushStrength_ID, brushStrength);
        _paintMaterial.SetFloat(_BrushSoftness_ID, brushSoftness);
        _paintMaterial.SetFloat(_BrushFeather_ID, feather);

        Graphics.Blit(SnowRT, _tempRT);
        _paintMaterial.SetTexture(_ExistingTex_ID, _tempRT);
        Graphics.Blit(null, SnowRT, _paintMaterial, 0);
    }

    /// 兼容旧接口：画单点（内部转为零长度线段）
    public void PaintAtUV(Vector2 uv, float brushSize, float brushStrength, float feather = 0.5f)
    {
        PaintLine(uv, uv, brushSize, brushStrength, feather);
    }

    /// 绘制矩形画笔痕迹（用于雪橇等长条形划痕）
    public void PaintRect(Vector2 uvCenter, float uvWidth, float uvLength, float angle, float brushStrength, float feather = 0.5f)
    {
        if (SnowRT == null || _paintMaterial == null) return;

        _paintMaterial.SetVector(_BrushPosA_ID, new Vector4(uvCenter.x, uvCenter.y, 0, 0));
        _paintMaterial.SetVector(_BrushPosB_ID, new Vector4(uvCenter.x, uvCenter.y, 0, 0));
        _paintMaterial.SetFloat(_BrushSize_ID, uvWidth);
        _paintMaterial.SetFloat(_BrushStrength_ID, brushStrength);
        _paintMaterial.SetFloat(_BrushSoftness_ID, brushSoftness);
        _paintMaterial.SetFloat(_BrushFeather_ID, feather);
        _paintMaterial.SetFloat(_BrushShape_ID, 1.0f);
        _paintMaterial.SetFloat(_RectLength_ID, uvLength);
        _paintMaterial.SetFloat(_RectAngle_ID, angle);

        Graphics.Blit(SnowRT, _tempRT);
        _paintMaterial.SetTexture(_ExistingTex_ID, _tempRT);
        Graphics.Blit(null, SnowRT, _paintMaterial, 0);

        _paintMaterial.SetFloat(_BrushShape_ID, 0.0f);
    }

    /// 世界坐标 XZ 转 RT 的 UV 坐标
    public Vector2 WorldToUV(Vector3 worldPos)
    {
        float u = (worldPos.x - areaCenter.x) / areaSize + 0.5f;
        float v = (worldPos.z - areaCenter.y) / areaSize + 0.5f;
        return new Vector2(u, v);
    }

    /// 世界空间画笔大小转 UV 空间大小
    public float WorldBrushToUV(float worldBrushSize)
    {
        return worldBrushSize / areaSize;
    }

    public void ClearAll()
    {
        if (SnowRT == null) return;
        var prev = RenderTexture.active;
        RenderTexture.active = SnowRT;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    private void CreateResources()
    {
        if (SnowRT == null || SnowRT.width != rtResolution)
        {
            if (SnowRT != null) SnowRT.Release();
            SnowRT = new RenderTexture(rtResolution, rtResolution, 0, RenderTextureFormat.R8);
            SnowRT.name = "SnowDeformRT";
            SnowRT.filterMode = FilterMode.Bilinear;
            SnowRT.wrapMode = TextureWrapMode.Clamp;
            SnowRT.Create();
            ClearAll();
        }
        if (_tempRT == null || _tempRT.width != rtResolution)
        {
            if (_tempRT != null) _tempRT.Release();
            _tempRT = new RenderTexture(rtResolution, rtResolution, 0, RenderTextureFormat.R8);
            _tempRT.Create();
        }
        if (_paintMaterial == null)
        {
            #if UNITY_EDITOR
            if (paintShader == null) paintShader = Shader.Find("Hidden/SnowPaint");
            #endif
            if (paintShader != null)
                _paintMaterial = new Material(paintShader);
            else
                Debug.LogError("[SnowDeformManager] paintShader 未赋值！请在 Inspector 中拖入 Hidden/SnowPaint shader。打包后 Shader.Find 无法找到未被引用的 shader。");
        }
        if (_fadeMaterial == null)
        {
            #if UNITY_EDITOR
            if (fadeShader == null) fadeShader = Shader.Find("Hidden/SnowFade");
            #endif
            if (fadeShader != null)
                _fadeMaterial = new Material(fadeShader);
            else
                Debug.LogError("[SnowDeformManager] fadeShader 未赋值！请在 Inspector 中拖入 Hidden/SnowFade shader。打包后 Shader.Find 无法找到未被引用的 shader。");
        }
    }

    private void UpdateGlobalShaderParams()
    {
        Shader.SetGlobalTexture(_SnowDeformRT_ID, SnowRT != null ? (Texture)SnowRT : Texture2D.blackTexture);
        // 凹陷深度等于雪厚度，雪越厚凹陷越深；Foot的deformDepth控制踩穿比例
        Shader.SetGlobalFloat(_SnowDeformDepth_ID, (sinkDepth*10));
        Shader.SetGlobalFloat(_SnowDeformDarken_ID, deformDarken);
        Shader.SetGlobalVector(_SnowAreaCenter_ID, new Vector4(areaCenter.x, areaCenter.y, 0, 0));
        Shader.SetGlobalFloat(_SnowAreaSize_ID, areaSize);
        Shader.SetGlobalFloat(_SnowSinkDepth_ID, sinkDepth);
    }

    private void ReleaseResources()
    {
        if (SnowRT != null) { SnowRT.Release(); SnowRT = null; }
        if (_tempRT != null) { _tempRT.Release(); _tempRT = null; }
        if (_paintMaterial != null) { DestroyImmediate(_paintMaterial); _paintMaterial = null; }
        if (_fadeMaterial != null) { DestroyImmediate(_fadeMaterial); _fadeMaterial = null; }
    }

    void OnValidate()
    {
        if (SnowRT != null && SnowRT.width != rtResolution) { ReleaseResources(); CreateResources(); }
        UpdateGlobalShaderParams();
    }

    // ─── RT 预览（Game 视图 OnGUI） ───
    void OnGUI()
    {
        if (!showRTPreview || SnowRT == null) return;

        float size = previewSize;
        Rect rect = new Rect(previewOffset.x, Screen.height - size - previewOffset.y, size, size);

        GUI.DrawTexture(rect, SnowRT, ScaleMode.ScaleToFit, false);

        // 标签
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(rect.x, rect.y - 18, 200, 20), $"SnowDeformRT ({rtResolution}x{rtResolution})", style);
    }

#if UNITY_EDITOR
    // ─── Scene 视图 RT 预览 ───
    void OnDrawGizmos()
    {
        if (!showRTPreview || SnowRT == null) return;

        // 注册 Scene 视图 GUI 回调
        UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
        UnityEditor.SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDestroy()
    {
        UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSceneGUI(UnityEditor.SceneView sceneView)
    {
        if (!showRTPreview || SnowRT == null) return;

        UnityEditor.Handles.BeginGUI();

        float size = previewSize;
        Rect rect = new Rect(previewOffset.x, previewOffset.y, size, size);

        GUI.DrawTexture(rect, SnowRT, ScaleMode.ScaleToFit, false);

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = Color.cyan }
        };
        GUI.Label(new Rect(rect.x, rect.y + size + 2, 250, 20), $"SnowDeformRT ({rtResolution}x{rtResolution})", style);

        UnityEditor.Handles.EndGUI();
    }
#endif
}

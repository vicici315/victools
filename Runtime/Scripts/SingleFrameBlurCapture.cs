using UnityEngine;

/// 单帧场景颜色捕获组件（运行时自动执行）。
/// 启动时抓取一帧场景颜色，在 GPU 端预模糊后赋给材质的 _CapturedSceneTex。
/// shader 在 _SINGLE_FRAME 模式下只需 1 次采样，性能极高。
[RequireComponent(typeof(Renderer))]
public class SingleFrameBlurCapture : MonoBehaviour
{
    [Tooltip("目标相机，为空则使用 Camera.main")]
    public Camera targetCamera;

    [Tooltip("模糊 Shader（必须指定，否则打包后无法使用）")]
    [SerializeField] private Shader blurShader;

    [Tooltip("捕获延迟帧数")]
    [Range(1, 10)]
    public int captureDelayFrames = 3;

    [Tooltip("RT 分辨率缩放")]
    [Range(0.25f, 1f)]
    public float resolutionScale = 0.5f;

    [Tooltip("模糊大小（对应材质 _BlurSize）")]
    [Range(0f, 11f)]
    public float blurSize = 4.0f;

    [Tooltip("像素大小（对应材质 _PixelSize）")]
    [Range(0.25f, 4f)]
    public float pixelSize = 1.0f;

    [Tooltip("Sigma 高斯权重（对应材质 _Sigma）")]
    [Range(0.1f, 5f)]
    public float sigma = 2.7f;

    [Tooltip("采样数量（对应材质 _SampleCount）")]
    [Range(1, 12)]
    public int sampleCount = 6;

    [Tooltip("使用性能模式（对应材质 Performance Mode）")]
    public bool usePerformanceMode = false;

    private RenderTexture _capturedRT;
    private Material _material;
    private Material _blurMaterial;
    private int _frameCount;
    private bool _captured;
    private bool _capturing; // 防止递归

    private static readonly int CapturedSceneTex = Shader.PropertyToID("_CapturedSceneTex");
    private static readonly int BlurSizeID = Shader.PropertyToID("_BlurSize");
    private static readonly int PixelSizeID = Shader.PropertyToID("_PixelSize");
    private static readonly int SigmaID = Shader.PropertyToID("_Sigma");
    private static readonly int SampleCountID = Shader.PropertyToID("_SampleCount");
    private static readonly int UsePerformanceModeID = Shader.PropertyToID("_UsePerformanceMode");
    private static readonly int ResolutionScaleID = Shader.PropertyToID("_ResolutionScale");

    private void Awake()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
            _material = renderer.material;
    }

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        _frameCount = 0;
        _captured = false;
        _capturing = false;

        // 初始化模糊材质
        if (blurShader == null)
            blurShader = Shader.Find("Hidden/SingleFrameBlur");

        if (blurShader != null && blurShader.isSupported)
        {
            _blurMaterial = new Material(blurShader);
            _blurMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogError("SingleFrameBlurCapture: Blur shader 无效或不支持，单帧捕获功能不可用。");
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (_captured || _capturing) return;

        _frameCount++;
        if (_frameCount >= captureDelayFrames)
        {
            CaptureAndBlur();
            _captured = true;
            enabled = false;
        }
    }

    private void CaptureAndBlur()
    {
        if (targetCamera == null || _material == null || _blurMaterial == null) return;

        _capturing = true; // 防止递归

        int width = Mathf.Max(64, Mathf.RoundToInt(targetCamera.pixelWidth * resolutionScale));
        int height = Mathf.Max(64, Mathf.RoundToInt(targetCamera.pixelHeight * resolutionScale));

        // 捕获场景（排除自身）
        var selfRenderer = GetComponent<Renderer>();
        bool wasEnabled = selfRenderer != null && selfRenderer.enabled;
        if (selfRenderer != null)
            selfRenderer.enabled = false;

        RenderTexture sceneRT = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        sceneRT.name = "SceneCapture_Temp";

        var originalRT = targetCamera.targetTexture;
        var originalCulling = targetCamera.cullingMask;

        targetCamera.targetTexture = sceneRT;
        targetCamera.Render();
        targetCamera.targetTexture = originalRT;

        if (selfRenderer != null)
            selfRenderer.enabled = wasEnabled;

        // GPU 端多 Pass 模糊
        _capturedRT = ApplyBlur(sceneRT, width, height);
        RenderTexture.ReleaseTemporary(sceneRT);

        // 赋给材质
        _material.SetTexture(CapturedSceneTex, _capturedRT);

        _capturing = false;
    }

    private RenderTexture ApplyBlur(RenderTexture source, int width, int height)
    {
        // 设置与主材质一致的模糊参数
        _blurMaterial.SetFloat(BlurSizeID, blurSize);
        _blurMaterial.SetFloat(PixelSizeID, pixelSize);
        _blurMaterial.SetFloat(SigmaID, sigma);
        _blurMaterial.SetFloat(SampleCountID, sampleCount);
        _blurMaterial.SetFloat(UsePerformanceModeID, usePerformanceMode ? 1f : 0f);
        _blurMaterial.SetFloat(ResolutionScaleID, resolutionScale);

        // 单次 Blit 完成全部模糊（shader 内部完整计算高斯核或 Kawase）
        RenderTexture result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        result.name = "SingleFrameBlurred";
        Graphics.Blit(source, result, _blurMaterial);

        return result;
    }

    private void OnDestroy()
    {
        if (_capturedRT != null)
        {
            _capturedRT.Release();
            Destroy(_capturedRT);
        }
        if (_blurMaterial != null)
            Destroy(_blurMaterial);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (blurShader == null)
        {
            blurShader = Shader.Find("Hidden/SingleFrameBlur");
        }
    }

    private void Reset()
    {
        blurShader = Shader.Find("Hidden/SingleFrameBlur");
    }
#endif
}

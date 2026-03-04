// FPS2.2优化帧率刷新速度
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public class FPS : MonoBehaviour
{
    [Header("显示设置")]
    [Tooltip("FPS显示刷新间隔（秒）- 值越小刷新越快，但数值跳动越明显")]
    [Range(0.1f, 2.0f)]
    public float refreshRate = 0.2f;
    
    [Tooltip("FPS文本的颜色")]
    public Color goodColor = Color.green;    // 良好帧率颜色
    public Color warningColor = Color.yellow;// 警告帧率颜色
    public Color badColor = Color.red;       // 差帧率颜色
    
    [Tooltip("颜色变化的阈值")]
    public float warningThreshold = 29f;
    public float badThreshold = 15f;
    
    [Header("帧率限制设置")]
    [Tooltip("是否解除帧率限制")]
    public bool unlockFrameRate = false;
    
    [Tooltip("目标帧率（仅在不解除限制时有效，-1表示使用垂直同步）")]
    public int targetFrameRate = 60;

    private Text fpsText;
    private float deltaTime = 0f;
    private float timer = 0f;
    private float currentFps = 0f;

    void Start()
    {
        fpsText = GetComponent<Text>();
        fpsText.fontSize = 36;
        
        // 设置帧率限制
        ApplyFrameRateSettings();
    }
    
    void ApplyFrameRateSettings()
    {
        if (unlockFrameRate)
        {
            // 解除帧率限制
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
        }
        else
        {
            // 应用目标帧率
            if (targetFrameRate > 0)
            {
                Application.targetFrameRate = targetFrameRate;
                QualitySettings.vSyncCount = 0;
            }
            else
            {
                // 使用垂直同步
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 1;
            }
        }
    }

    void Update()
    {
        // 使用平滑的 deltaTime 计算 FPS
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        
        // 累计时间
        timer += Time.unscaledDeltaTime;
        
        // 按照设定的刷新速率更新显示
        if (timer >= refreshRate)
        {
            currentFps = 1.0f / deltaTime;
            
            // 更新显示文本
            string frameRateInfo = unlockFrameRate ? " (Unlocked)" : 
                                   (targetFrameRate > 0 ? $" (Target: {targetFrameRate})" : " (VSync)");
            fpsText.text = $"F: {currentFps:0.0}{frameRateInfo}";
            
            // 根据FPS值改变文本颜色
            if (currentFps >= warningThreshold)
                fpsText.color = goodColor;
            else if (currentFps >= badThreshold)
                fpsText.color = warningColor;
            else
                fpsText.color = badColor;
            
            // 重置计时器
            timer = 0f;
        }
    }
    
    // 在编辑器中修改参数时实时应用
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyFrameRateSettings();
        }
    }
}

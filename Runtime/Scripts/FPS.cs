using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public class FPS : MonoBehaviour
{
    [Tooltip("更新FPS显示的间隔时间（秒）")]
    public float updateInterval = 0.5f;
    
    [Tooltip("FPS文本的颜色")]
    public Color goodColor = Color.green;    // 良好帧率颜色
    public Color warningColor = Color.yellow;// 警告帧率颜色
    public Color badColor = Color.red;       // 差帧率颜色
    
    [Tooltip("颜色变化的阈值")]
    public float warningThreshold = 29f;
    public float badThreshold = 15f;

    private Text fpsText;

    private float accumulator = 0f; // 帧数累加器
    private int frameCount = 0;    // 帧数计数
    private float timeLeft;        // 距离下次更新的时间
    private float fps;             // 当前FPS值

    void Start()
        {
        fpsText = GetComponent<Text>();
        fpsText.fontSize = 36;
        timeLeft = updateInterval;
    }

    void Update()
    {
        timeLeft -= Time.deltaTime;
        accumulator += Time.timeScale / Time.deltaTime;
        frameCount++;

        // 当达到更新间隔时计算FPS
        if (timeLeft <= 0f)
        {
            // 计算平均FPS
            fps = accumulator / frameCount;
            
            // 更新显示文本
            fpsText.text = $"FPS: {fps:0.0}";
            
            // 根据FPS值改变文本颜色
            if (fps >= warningThreshold)
                fpsText.color = goodColor;
            else if (fps >= badThreshold)
                fpsText.color = warningColor;
            else
                fpsText.color = badColor;

            // 重置计数器
            timeLeft = updateInterval;
            accumulator = 0f;
            frameCount = 0;
        }
    }
}

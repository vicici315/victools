using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace VicTools
{
    /// VicTools 全局配置和常量
    public static class VicToolsConfig
    {
        /// VicTools 全局版本号
        public const string Ver = "2.7.4";

        /// 性能分析器窗口标签名（包含版本号）
        public const string PerformanceAnalyzerWindowName = "[性能分析 v1.4]";
    }
    
    /// 子窗口抽象基类 - 参考ES3Window的SubWindow设计
    
    public abstract class SubWindow
    {
        public readonly string Name;
        protected readonly EditorWindow Parent;

        protected SubWindow(string name, EditorWindow parent)
        {
            Name = name;
            Parent = parent;
        }
        
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
        public virtual void OnGUI() { }
        public virtual void OnFocus() { }
        public virtual void OnLostFocus() { }
        public virtual void OnDestroy() { }
        public virtual void OnHierarchyChange() { }
        

        // ReSharper disable Unity.PerformanceAnalysis
        /// 创建带事件处理的Slider控件 - 公共方法，可在任何子窗口中使用
        /// <param name="label">Slider标签</param>
        /// <param name="value">当前值</param>
        /// <param name="leftValue">最小值</param>
        /// <param name="rightValue">最大值</param>
        /// <param name="onValueChanged">值变化时的回调函数（每次变化都触发）</param>
        /// <returns>更新后的Slider值</returns>
        protected float CreateSliderWithEvent(string label, float value, float leftValue, float rightValue, 
                                         Action<float> onValueChanged = null)
        {
            EditorGUI.BeginChangeCheck();
            var newValue = EditorGUILayout.Slider(label, value, leftValue, rightValue);
            var valueChanged = EditorGUI.EndChangeCheck();

            if (!valueChanged) return newValue;
            // 调用值变化回调函数（每次变化都触发）
            onValueChanged?.Invoke(newValue);
                
            // 强制重绘窗口以更新显示
            Parent?.Repaint();
            return newValue;
        }


        /// 创建带事件处理的整数Slider控件 - 公共方法，可在任何子窗口中使用
        /// 使用自定义实现避免滑动图标显示问题
        /// <param name="label">Slider标签</param>
        /// <param name="value">当前整数值</param>
        /// <param name="leftValue">最小整数值</param>
        /// <param name="rightValue">最大整数值</param>
        /// <param name="onValueChanged">值变化时的回调函数（每次变化都触发）</param>
        /// <param name="minWidth">Slider的最小宽度（可选，默认0表示自动宽度）</param>
        /// <param name="maxWidth">Slider的最大宽度（可选，默认0表示无限制）</param>
        /// <param name="labelWidth">标签宽度（可选，默认0表示自动宽度）</param>
        /// <returns>更新后的整数Slider值</returns>
        protected int CreateIntSliderWithEvent(string label, int value, int leftValue, int rightValue,
                                          Action<int> onValueChanged = null, float minWidth = 0, float maxWidth = 0, float labelWidth = 0)
        {
            var newValue = value;
            
            // 使用水平布局手动创建滑块和输入框
            EditorGUILayout.BeginHorizontal();
            
            // 显示标签 - 支持自定义标签宽度
            if (labelWidth > 0)
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            }
            else
            {
                EditorGUILayout.LabelField(label);
            }
            
            // 创建滑块部分 - 使用自定义样式避免滑动图标
            float sliderValue = value;
            float minValue = leftValue;
            float maxValue = rightValue;
            
            // 使用自定义的滑块实现
            EditorGUI.BeginChangeCheck();
            switch (minWidth)
            {
                case > 0 when maxWidth > 0:
                    sliderValue = GUILayout.HorizontalSlider(sliderValue, minValue, maxValue, GUILayout.MinWidth(minWidth), GUILayout.MaxWidth(maxWidth));
                    break;
                case > 0:
                    sliderValue = GUILayout.HorizontalSlider(sliderValue, minValue, maxValue, GUILayout.MinWidth(minWidth));
                    break;
                default:
                {
                    sliderValue = maxWidth > 0 ? GUILayout.HorizontalSlider(sliderValue, minValue, maxValue, GUILayout.MaxWidth(maxWidth)) : GUILayout.HorizontalSlider(sliderValue, minValue, maxValue);

                    break;
                }
            }
            
            // 将滑块值转换为整数
            var sliderIntValue = Mathf.RoundToInt(sliderValue);
            var sliderChanged = EditorGUI.EndChangeCheck();
            
            // 如果滑块变化了，更新newValue并移除输入框焦点
            if (sliderChanged)
            {
                newValue = sliderIntValue;
                // 移除输入框焦点，确保滑块能够正常工作
                GUI.FocusControl(null);
            }
            
            // 显示数值输入框 - 使用当前值（可能是滑块更新后的值）
            EditorGUI.BeginChangeCheck();
            newValue = EditorGUILayout.IntField(newValue, GUILayout.Width(60));
            var inputChanged = EditorGUI.EndChangeCheck();
            
            // 确保数值在有效范围内
            newValue = Mathf.Clamp(newValue, leftValue, rightValue);
            
            EditorGUILayout.EndHorizontal();
            
            var valueChanged = sliderChanged || inputChanged;

            if (!valueChanged) return newValue;
            // 调用值变化回调函数（每次变化都触发）
            onValueChanged?.Invoke(newValue);

            // 强制重绘窗口以更新显示
            Parent?.Repaint();
            return newValue;
        }
        

        // ReSharper disable Unity.PerformanceAnalysis
        /// 创建带自定义样式的Toggle控件 - 内置选中时颜色变化功能，支持点击标签切换状态
        /// <param name="label">Toggle标签</param>
        /// <param name="value">当前Toggle状态</param>
        /// <param name="onValueChanged">值变化时的回调函数</param>
        /// <param name="toggleStyle">Toggle自定义GUI样式（可选）</param>
        /// <param name="labelStyle">标签自定义GUI样式（可选）</param>
        /// <param name="activeLabelStyle">Toggle选中时的标签样式（可选）</param>
        /// <param name="labelWidth">标签宽度（可选，默认80）</param>
        /// <param name="toggleWidth">Toggle宽度（可选，默认20）</param>
        /// <returns>更新后的Toggle状态</returns>
        /// 使用方法：
            // includeSubfolders = base.CreateToggleWithStyle("包含子文件夹", includeSubfolders, 
            // (newValue) => {
            //     Debug.Log($"Toggle状态变化: {includeSubfolders} → {newValue}");
            // });
            protected bool CreateToggleWithStyle(string label, bool value, Action<bool> onValueChanged = null, 
                                        GUIStyle toggleStyle = null, GUIStyle labelStyle = null, GUIStyle activeLabelStyle = null,
                                        float labelWidth = 80, float toggleWidth = 20)
        {
            // 将string转换为GUIContent，调用支持GUIContent的重载方法
            return CreateToggleWithStyle(new GUIContent(label), value, onValueChanged, toggleStyle, labelStyle, activeLabelStyle, labelWidth, toggleWidth);
        }

        // ReSharper disable Unity.PerformanceAnalysis
        /// 创建带自定义样式的Toggle控件 - 内置选中时颜色变化功能，支持点击标签切换状态，支持带ToolTip的GUIContent
        /// <param name="labelContent">Toggle标签内容（支持ToolTip）</param>
        /// <param name="value">当前Toggle状态</param>
        /// <param name="onValueChanged">值变化时的回调函数</param>
        /// <param name="toggleStyle">Toggle自定义GUI样式（可选）</param>
        /// <param name="labelStyle">标签自定义GUI样式（可选）</param>
        /// <param name="activeLabelStyle">Toggle选中时的标签样式（可选）</param>
        /// <param name="labelWidth">标签宽度（可选，默认80）</param>
        /// <param name="toggleWidth">Toggle宽度（可选，默认20）</param>
        /// <returns>更新后的Toggle状态</returns>
        /// 使用方法：
            // includeSubfolders = base.CreateToggleWithStyle(new GUIContent("包含子文件夹", "包含子文件夹的ToolTip提示"), includeSubfolders, 
            // (newValue) => {
            //     Debug.Log($"Toggle状态变化: {includeSubfolders} → {newValue}");
            // });
            protected bool CreateToggleWithStyle(GUIContent labelContent, bool value, Action<bool> onValueChanged = null, 
                                        GUIStyle toggleStyle = null, GUIStyle labelStyle = null, GUIStyle activeLabelStyle = null,
                                        float labelWidth = 80, float toggleWidth = 20)
        {
            EditorGUI.BeginChangeCheck();
            
            var newValue = value;
            
            // 如果未提供activeLabelStyle，创建默认的选中时样式
            if (activeLabelStyle == null)
            {
                activeLabelStyle = new GUIStyle(EditorStyles.label != null ? EditorStyles.label : new GUIStyle());
                // activeLabelStyle.fontStyle = FontStyle.Bold;
                activeLabelStyle.normal.textColor = new Color(0.95f, 0.45f, 0.3f); // 默认选中时颜色
                activeLabelStyle.fontSize = 14;
                // activeLabelStyle.GUILayout.Width(80);
                // GUIStyle不是UnityEngine.Object，没有hideFlags属性
                // 不需要设置HideFlags.HideAndDontSave
            }
            
            // 如果未提供labelStyle，创建默认的未选中时样式
            labelStyle ??= new GUIStyle(EditorStyles.label != null ? EditorStyles.label : new GUIStyle())
            {
                normal =
                {
                    textColor = new Color(0.2f, 0.8f, 0.4f) // 默认未选中时颜色
                },
                fontSize = 14
            };
            // GUIStyle不是UnityEngine.Object，没有hideFlags属性
            // 不需要设置HideFlags.HideAndDontSave
            
            // 使用水平布局手动控制标签和Toggle的位置
            EditorGUILayout.BeginHorizontal();
            
            // 创建标签区域 - 支持点击切换状态
            var labelRect = EditorGUILayout.GetControlRect(GUILayout.Width(labelWidth));
            // Toggle未选中时使用labelStyle
            // Toggle选中时使用activeLabelStyle
            EditorGUI.LabelField(labelRect, labelContent, value ? activeLabelStyle : labelStyle);

            // 检测标签点击事件
            var currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && labelRect.Contains(currentEvent.mousePosition))
            {
                newValue = !value;
                currentEvent.Use(); // 消耗事件，防止重复处理
            }
            
            // 显示Toggle - 支持自定义样式和宽度
            newValue = toggleStyle != null ? EditorGUILayout.Toggle(newValue, toggleStyle, GUILayout.Width(toggleWidth)) : EditorGUILayout.Toggle(newValue, GUILayout.Width(toggleWidth));
            
            EditorGUILayout.EndHorizontal();
            
            var valueChanged = EditorGUI.EndChangeCheck() || (newValue != value);

            if (!valueChanged) return newValue;
            // 调用值变化回调函数
            onValueChanged?.Invoke(newValue);
                
            // 强制重绘窗口以更新显示
            Parent?.Repaint();

            return newValue;
        }


        // ReSharper disable Unity.PerformanceAnalysis
        /// 创建带自定义样式的文本输入控件 - 使用Unity Editor自带组件，支持长文本滚动和自动光标聚焦
        /// 完全修复了控件名称冲突和焦点管理问题，确保每个文本框都有稳定唯一的控件名称
        /// 修复了文本控件内容相互影响的问题，确保每个文本框独立工作
        /// <param name="label">文本标签</param>
        /// <param name="text">当前文本内容</param>
        /// <param name="onTextChanged">文本变化时的回调函数</param>
        /// <param name="labelWidth">标签宽度（可选，默认80）</param>
        /// <param name="fieldWidth">输入框宽度（可选，默认0表示自动宽度）</param>
        /// <param name="alignment">文本对齐方式（可选，默认TextAnchor.MiddleLeft）</param>
        /// <param name="labelStyle">标签自定义GUI样式（可选）</param>
        /// <param name="textFieldStyle">输入框自定义GUI样式（可选）</param>
        /// <param name="controlName">控件名称，用于焦点控制（可选）</param>
        /// <returns>更新后的文本内容</returns>
        /// 使用方法：
        // searchText = base.CreateTextFieldWithStyle("搜索文本", searchText, 
        // (newText) => {
        //     Debug.Log($"文本输入变化: {searchText} → {newText}");
        // }, 
        // 80, 200, TextAnchor.MiddleLeft, null, null, "ProjectTools_pathField");
        protected string CreateTextFieldWithStyle(string label, string text, Action<string> onTextChanged = null, 
                                             float labelWidth = 80, float fieldWidth = 0, TextAnchor alignment = TextAnchor.MiddleLeft,
                                             GUIStyle labelStyle = null, GUIStyle textFieldStyle = null, string controlName = null)
        {
            EditorGUI.BeginChangeCheck();
            
            string newText;
            
            // 使用水平布局手动控制标签和输入框的位置
            EditorGUILayout.BeginHorizontal();
            
            // 显示标签 - 支持自定义标签样式
            if (labelWidth > 0)
            {
                if (labelStyle != null)
                {
                    EditorGUILayout.LabelField(label, labelStyle, GUILayout.Width(labelWidth));
                }
                else
                {
                    EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
                }
            }
            else
            {
                if (labelStyle != null)
                {
                    EditorGUILayout.LabelField(label, labelStyle);
                }
                else
                {
                    EditorGUILayout.LabelField(label);
                }
            }
            
            // 生成稳定唯一的控件名称 - 无论是否提供controlName参数，都确保名称唯一
            // 包含父窗口类型、标签、窗口实例ID和调用堆栈信息，确保每个文本框都有唯一名称
            var parentTypeName = Parent?.GetType().Name ?? "Unknown";
            var windowInstanceId = Parent?.GetInstanceID().ToString() ?? "Unknown";
            
            // 使用更复杂的名称生成逻辑，确保唯一性
            controlName = string.IsNullOrEmpty(controlName) ? $"TextField_{parentTypeName}_{label}_{windowInstanceId}" :
                // 如果提供了controlName，将其作为基础，但添加额外信息确保唯一性
                $"TextField_{parentTypeName}_{controlName}_{windowInstanceId}";
            
            // 设置下一个控件名称
            GUI.SetNextControlName(controlName);
            
            // 创建支持水平滚动的文本字段样式
            var scrollableTextFieldStyle = textFieldStyle != null ? new GUIStyle(textFieldStyle) : new GUIStyle(EditorStyles.textField != null ? EditorStyles.textField : new GUIStyle());

            // 确保样式支持水平滚动
            scrollableTextFieldStyle.alignment = alignment;
            scrollableTextFieldStyle.clipping = TextClipping.Clip; // 启用文本裁剪
            scrollableTextFieldStyle.wordWrap = false; // 禁用自动换行，强制水平滚动
            
            // 使用GUILayout.TextField确保宽度参数正常工作
            if (fieldWidth > 0)
            {
                // 固定宽度模式
                newText = EditorGUILayout.TextField(text, scrollableTextFieldStyle, GUILayout.Width(fieldWidth));
            }
            else
            {
                // 自动扩展宽度模式
                newText = EditorGUILayout.TextField(text, scrollableTextFieldStyle, GUILayout.ExpandWidth(true));
            }
            
            EditorGUILayout.EndHorizontal();
            
            var textChanged = EditorGUI.EndChangeCheck();
            
            // 处理文本变化
            if (!textChanged) return newText;
            // 调用文本变化回调函数
            onTextChanged?.Invoke(newText);
                
            // 强制重绘窗口以更新显示
            Parent?.Repaint();

            return newText;
        }


        /// 创建贴图尺寸选择器 - 专门用于选择常用贴图尺寸
        /// <param name="label">选择器标签</param>
        /// <param name="currentSize">当前贴图尺寸</param>
        /// <param name="onSizeChanged">尺寸变化时的回调函数</param>
        /// <param name="labelWidth">标签文本宽度（可选，默认0表示自动宽度）</param>
        /// <param name="enumWidth">选择器宽度（可选，默认0表示自动宽度）</param>
        /// <param name="style">选择器自定义GUI样式（可选）</param>
        /// <param name="labelStyle">标签自定义GUI样式（可选）</param>
        /// <param name="sizeOptions">自定义尺寸选项数组（可选，默认使用常用贴图尺寸）</param>
        /// <returns>更新后的贴图尺寸</returns>
        /// 其它文件子窗口中带onSizeChanged事件使用方法：
        // textureSize = base.CreateEnumPopupSizeSelector($"贴图尺寸", textureSize, 
        // (newSize) => {
        //     Debug.Log($"自定义样式选择器 - 贴图尺寸变化: {textureSize}x{textureSize} → {newSize}x{newSize}");
        // }, 
        // 0, customStyle); // 使用自定义样式，宽度为0表示自动宽度
        protected int CreateEnumPopupSizeSelector(string label, int currentSize, Action<int> onSizeChanged = null, float labelWidth = 0, 
                                           float enumWidth = 0, GUIStyle style = null, GUIStyle labelStyle = null, int[] sizeOptions = null)
        {
            // 使用传入的尺寸选项，如果没有传入则使用默认的常用贴图尺寸
            var textureSizes = sizeOptions ?? new[] { 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
            
            // 找到当前尺寸在选项中的索引
            var currentIndex = Array.IndexOf(textureSizes, currentSize);
            if (currentIndex == -1)
            {
                // 如果当前尺寸不在选项中，使用最接近的尺寸
                currentIndex = 0;
                for (var i = 0; i < textureSizes.Length; i++)
                {
                    if (currentSize > textureSizes[i]) continue;
                    currentIndex = i;
                    break;
                }
                if (currentIndex == 0 && currentSize > textureSizes[textureSizes.Length - 1])
                {
                    currentIndex = textureSizes.Length - 1;
                }
            }
            
            EditorGUI.BeginChangeCheck();
            
            // 创建选项数组用于显示
            var options = new GUIContent[textureSizes.Length];
            for (var i = 0; i < textureSizes.Length; i++)
            {
                options[i] = new GUIContent($"{textureSizes[i]} x {textureSizes[i]}");
                // options[i] = new GUIContent($"{textureSizes[i]}");
            }
            
            // 使用水平布局手动控制标签和选择器的位置
            EditorGUILayout.BeginHorizontal();
            
            // 显示标签 - 支持自定义标签样式
            if (labelWidth > 0)
            {
                if (labelStyle != null)
                {
                    EditorGUILayout.LabelField(label, labelStyle, GUILayout.Width(labelWidth));
                }
                else
                {
                    EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
                }
            }
            else
            {
                if (labelStyle != null)
                {
                    EditorGUILayout.LabelField(label, labelStyle);
                }
                else
                {
                    EditorGUILayout.LabelField(label);
                }
            }
            
            // 显示弹出式选择器 - 支持自定义宽度和样式
            int newIndex;
            switch (enumWidth)
            {
                case > 0 when style != null:
                    newIndex = EditorGUILayout.Popup(currentIndex, options, style, GUILayout.Width(enumWidth));
                    break;
                case > 0:
                    newIndex = EditorGUILayout.Popup(currentIndex, options, GUILayout.Width(enumWidth));
                    break;
                default:
                {
                    if (style != null)
                    {
                        newIndex = EditorGUILayout.Popup(currentIndex, options, style);
                    }
                    else
                    {
                        newIndex = EditorGUILayout.Popup(currentIndex, options);
                    }

                    break;
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            var sizeChanged = EditorGUI.EndChangeCheck();

            if (!sizeChanged) return currentSize;
            var newSize = textureSizes[newIndex];
            // 调用尺寸变化回调函数
            onSizeChanged?.Invoke(newSize);
                
            // 强制重绘窗口以更新显示
            Parent?.Repaint();
                
            return newSize;

        }


        /// 创建字符串选项选择器 - 专门用于选择字符串选项
        /// <param name="label">选择器标签</param>
        /// <param name="currentIndex">当前选项索引</param>
        /// <param name="onIndexChanged">索引变化时的回调函数</param>
        /// <param name="labelWidth">标签文本宽度（可选，默认0表示自动宽度）</param>
        /// <param name="enumWidth">选择器宽度（可选，默认0表示自动宽度）</param>
        /// <param name="style">选择器自定义GUI样式（可选）</param>
        /// <param name="labelStyle">标签自定义GUI样式（可选）</param>
        /// <param name="stringOptions">字符串选项数组（必需）</param>
        /// <param name="tooltip">标签的Tooltip提示文本（可选）</param>
        /// <returns>更新后的选项索引</returns>
        /// 使用方法：
            // selectedIndex = base.CreateStringOptionsSelector("选项", selectedIndex, 
            // (newIndex) => {
            //     Debug.Log($"选项变化: {selectedIndex} → {newIndex}");
            // }, 
            // 0, 0, null, null, new string[] { "选项1", "选项2", "选项3" }, "这是选项的Tooltip提示");
        protected int CreateStringOptionsSelector(string label, int currentIndex, Action<int> onIndexChanged = null, float labelWidth = 0, 
                                           float enumWidth = 0, GUIStyle style = null, GUIStyle labelStyle = null, string[] stringOptions = null, string tooltip = null)
        {
            if (stringOptions == null || stringOptions.Length == 0)
            {
                Debug.LogError("字符串选项数组不能为空");
                return currentIndex;
            }
            
            // 确保当前索引在有效范围内
            currentIndex = Mathf.Clamp(currentIndex, 0, stringOptions.Length - 1);
            
            EditorGUI.BeginChangeCheck();
            
            // 创建选项数组用于显示
            var options = new GUIContent[stringOptions.Length];
            for (var i = 0; i < stringOptions.Length; i++)
            {
                options[i] = new GUIContent(stringOptions[i]);
            }
            
            // 使用水平布局手动控制标签和选择器的位置
            EditorGUILayout.BeginHorizontal();
            
            // 创建带Tooltip的标签内容
            var labelContent = new GUIContent(label, tooltip);
            
            // 显示标签 - 支持自定义标签样式和Tooltip
            if (labelWidth > 0)
            {
                if (labelStyle != null)
                {
                    EditorGUILayout.LabelField(labelContent, labelStyle, GUILayout.Width(labelWidth));
                }
                else
                {
                    EditorGUILayout.LabelField(labelContent, GUILayout.Width(labelWidth));
                }
            }
            else
            {
                if (labelStyle != null)
                {
                    EditorGUILayout.LabelField(labelContent, labelStyle);
                }
                else
                {
                    EditorGUILayout.LabelField(labelContent);
                }
            }
            
            // 显示弹出式选择器 - 支持自定义宽度和样式
            int newIndex;
            if (enumWidth > 0 && style != null)
            {
                newIndex = EditorGUILayout.Popup(currentIndex, options, style, GUILayout.Width(enumWidth));
            }
            else if (enumWidth > 0)
            {
                newIndex = EditorGUILayout.Popup(currentIndex, options, GUILayout.Width(enumWidth));
            }
            else if (style != null)
            {
                newIndex = EditorGUILayout.Popup(currentIndex, options, style);
            }
            else
            {
                newIndex = EditorGUILayout.Popup(currentIndex, options);
            }
            
            EditorGUILayout.EndHorizontal();
            
            bool indexChanged = EditorGUI.EndChangeCheck();
            
            if (indexChanged)
            {
                // 调用索引变化回调函数
                onIndexChanged?.Invoke(newIndex);
                
                // 强制重绘窗口以更新显示
                Parent?.Repaint();
                
                return newIndex;
            }
            
            return currentIndex;
        }

        /// 创建纯色纹理
        /// <param name="width">纹理宽度</param>
        /// <param name="height">纹理高度</param>
        /// <param name="color">纹理颜色</param>
        /// <returns>创建的纹理</returns>
        public Texture2D CreateColorTexture(int width, int height, Color color)
        {
            Texture2D texture = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            // 正确设置HideFlags.HideAndDontSave，避免Unity编辑器试图持久化临时纹理
            // 这解决了断言失败：'!(o->TestHideFlag(Object::kDontSaveInEditor) && (options & kAllowDontSaveObjectsToBePersistent) == 0)'
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }

    
    /// VicTools 主窗口 - 优化版本
    public class VicToolsWindow : EditorWindow
    {
        private SubWindow[] _windows;
        public SubWindow CurrentWindow;
        
        // 全局选中对象数量 - 所有子窗口共享
        public int globalSelectedObjectsCount;
        
        // 窗口大小管理
        private Vector2 _previousWindowSize = Vector2.zero;
        private bool _isPerformanceAnalyzerActive;
        private Vector2 _performanceAnalyzerSize = new(497, 1132);   //性能分析器窗口大小
        private Vector2 _normalWindowSize = new(497, 600);           //常规窗口默认尺寸
        private bool _needsWindowSizeUpdate;                          //标记需要更新窗口尺寸
        
        // 窗口配置界面显示状态
        private bool _showWindowOrderConfig;
        
        // 停靠式窗口设置 - 当窗口停靠状态时不自动设置窗口尺寸
        private bool _disableDockedWindowAutoSize = true;
        
        // 窗口持久化相关
        private Vector2 _lastWindowPosition = Vector2.zero;
        private Vector2 _lastWindowSize = Vector2.zero;
        private int _windowRestoreAttempts; // 窗口状态恢复尝试次数
        private const int MaxWindowRestoreAttempts = 3; // 最大恢复尝试次数

        // 多种菜单项入口，提供更灵活的使用方式
        [MenuItem("Tools/VicTools(YD)/[ ScenesTools ]", false, 3000)]
        public static void ShowWindow()
        {
            var window = (VicToolsWindow)GetWindow(typeof(VicToolsWindow));
            if (!window) return;
            // Set the window name and icon.
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/VicTools/scenestools-1.png");
            window.titleContent = new GUIContent($"VicTools(YD) {VicToolsConfig.Ver}", icon);
            window.minSize = new Vector2(497, 400); // 最小宽度，最小高度
            window.maxSize = new Vector2(1200, 1700);    // 最大宽度，最大高度
            window.Show();
        }


        // 动态菜单项 - 使用MenuItem的validate函数来避免硬编码依赖
        // [MenuItem("Tools/VicTools/场景工具", false, 2001)]
        // public static void ShowScenesWindow()
        // {
        //     ShowWindowByTypeName("ScenesTools");
        // }


        // ReSharper disable Unity.PerformanceAnalysis
        private void InitSubWindows()
        {
            // 延迟初始化子窗口，避免在序列化过程中创建对象
            var subWindowTypes = FindAllSubWindowTypes();
            var subWindows = new List<SubWindow>();
            
            foreach (var type in subWindowTypes)
            {
                try
                {
                    // 延迟创建子窗口实例，确保在适当的时机创建
                    var subWindow = CreateSubWindowInstance(type);
                    if (subWindow == null) continue;
                    subWindows.Add(subWindow);
                    Debug.Log($"成功加载子窗口: {subWindow.Name} ({type.Name})");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"无法加载子窗口类型 {type.Name}: {ex.Message}");
                }
            }
            
            // 按自定义固定顺序排列子窗口
            _windows = OrderSubWindowsByCustomSequence(subWindows.ToArray());
            
            if (_windows.Length == 0)
            {
                Debug.LogWarning("未找到任何子窗口，请确保有继承自SubWindow的类存在");
            }
        }
        

        /// 查找所有继承自SubWindow的子窗口类型
        private static Type[] FindAllSubWindowTypes()
        {
            var subWindowType = typeof(SubWindow);
            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => 
                    type.IsClass && 
                    !type.IsAbstract && 
                    type.IsSubclassOf(subWindowType))
                .ToArray();
            
            return allTypes;
        }
        

        /// 创建子窗口实例
        private SubWindow CreateSubWindowInstance(Type type)
        {
            try
            {
                // 获取构造函数参数
                var constructors = type.GetConstructors();
                if (constructors.Length == 0)
                {
                    Debug.LogWarning($"子窗口类型 {type.Name} 没有可用的构造函数");
                    return null;
                }
                
                // 使用第一个构造函数
                var constructor = constructors[0];
                var parameters = constructor.GetParameters();
                
                if (parameters.Length == 2 && 
                    parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(EditorWindow))
                {
                    // 使用默认名称（类名）创建实例
                    var windowName = GetDefaultWindowName(type);
                    return (SubWindow)constructor.Invoke(new object[] { windowName, this });
                }
                else
                {
                    Debug.LogWarning($"子窗口类型 {type.Name} 的构造函数签名不匹配");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建子窗口实例失败 {type.Name}: {ex}");
                return null;
            }
        }
        

        /// 获取子窗口的默认名称
        private static string GetDefaultWindowName(Type type)
        {
            // 移除"SubWindow"、"Window"等后缀
            var name = type.Name;
            if (name.EndsWith("SubWindow"))
                name = name[..^"SubWindow".Length];
            else if (name.EndsWith("Window"))
                name = name[..^"Window".Length];
            else if (name.EndsWith("Tools"))
                name = name[..^"Tools".Length];
                
            return name;
        }

        /// <summary>
        /// 按自定义固定顺序排列子窗口
        /// </summary>
        /// <param name="subWindows">子窗口数组</param>
        /// <returns>按自定义顺序排列的子窗口数组</returns>
        private SubWindow[] OrderSubWindowsByCustomSequence(SubWindow[] subWindows)
        {
            // 从EditorPrefs加载自定义顺序，如果没有则使用默认顺序
            var customOrder = LoadCustomWindowOrder();
            
            // 如果EditorPrefs中没有保存的顺序，使用默认顺序并保存
            if (customOrder == null || customOrder.Length == 0)
            {
                customOrder = new[]
                {
                    "ScenesTools",           // 场景工具 - 最常用
                    "ProjectTools",          // 项目工具
                    "ShaderMaterialFinder",  // 着色器材质查找器
                    VicToolsConfig.PerformanceAnalyzerWindowName        // 性能分析器 - 重要工具
                };
                SaveCustomWindowOrder(customOrder);
            }

            var orderedWindows = customOrder.Select(windowName => subWindows.FirstOrDefault(w => w.Name == windowName)).Where(window => window != null).ToList();
            
            // 首先按自定义顺序添加窗口

            // 然后添加不在自定义顺序中的其他窗口（按名称排序）
            var remainingWindows = subWindows.Where(w => !customOrder.Contains(w.Name))
                                            .OrderBy(w => w.Name)
                                            .ToList();
            orderedWindows.AddRange(remainingWindows);
            
            return orderedWindows.ToArray();
        }
        
        /// <summary>
        /// 从EditorPrefs加载自定义窗口顺序
        /// </summary>
        /// <returns>窗口名称数组</returns>
        private static string[] LoadCustomWindowOrder()
        {
            var orderString = EditorPrefs.GetString("VicTools.CustomWindowOrder", "");
            return string.IsNullOrEmpty(orderString) ? Array.Empty<string>() : orderString.Split(';');
        }
        
        /// <summary>
        /// 保存自定义窗口顺序到EditorPrefs
        /// </summary>
        /// <param name="order">窗口名称数组</param>
        private void SaveCustomWindowOrder(string[] order)
        {
            string orderString = string.Join(";", order);
            EditorPrefs.SetString("VicTools.CustomWindowOrder", orderString);
        }
        
        /// <summary>
        /// 获取所有可用的子窗口名称
        /// </summary>
        /// <returns>子窗口名称数组</returns>
        private string[] GetAllWindowNames()
        {
            if (_windows == null)
                InitSubWindows();
                
            return (_windows ?? Array.Empty<SubWindow>()).Select(w => w.Name).ToArray();
        }
        
        // 拖拽排序相关变量 - 完全重写版本
        private int _dragFromIndex = -1;
        private int _dragToIndex = -1;
        private bool _isDragging;
        private readonly List<Rect> _itemRects = new();
        private Vector2 _scrollGui;
        
        /// 显示设置界面
        private void ShowWindowOrderConfig()
        {
            var style = EditorStyle.Get;
            var currentOrder = LoadCustomWindowOrder();
            var allWindows = GetAllWindowNames();
            
            // 创建新的顺序列表，只包含当前实际存在的有效子窗口
            _scrollGui = EditorGUILayout.BeginScrollView(_scrollGui, style.area, GUILayout.ExpandHeight(true));
            // 首先添加当前顺序中存在的有效窗口
            var newOrder = currentOrder.Where(windowName => allWindows.Contains(windowName)).ToList();

            // 然后添加任何在当前顺序中缺失的有效窗口
            foreach (var windowName in allWindows)
            {
                if (!newOrder.Contains(windowName))
                {
                    newOrder.Add(windowName);
                }
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("窗口顺序配置", style.subheading2);
            EditorGUILayout.HelpBox("拖拽窗口名称来重新排序。新添加的窗口会自动出现在列表末尾。", MessageType.Info);
            
            // 重置项目矩形列表
            _itemRects.Clear();
            
            // 显示当前顺序
            for (var i = 0; i < newOrder.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                
                // 创建可拖拽的标签区域
                var labelRect = EditorGUILayout.GetControlRect();
                var displayText = $"{i + 1}. {newOrder[i]}";
                
                // 保存项目矩形用于拖拽计算
                _itemRects.Add(labelRect);
                
                // 高亮显示拖拽目标位置
                if (_isDragging && _dragToIndex == i)
                {
                    GUI.backgroundColor = new Color(0.8f, 0.9f, 1.0f, 0.5f);
                    GUI.Box(new Rect(labelRect.x, labelRect.y, labelRect.width, labelRect.height), "");
                    GUI.backgroundColor = Color.white;
                }
                
                // 显示窗口名称
                EditorGUI.LabelField(labelRect, displayText);
                
                // 处理拖拽事件
                HandleDragEvents(labelRect, i);
                
                // 保留原有的上下移动按钮作为备用
                // 上移按钮（第一个元素不能上移）
                if (i > 0 && GUILayout.Button("↑", GUILayout.Width(25)))
                {
                    (newOrder[i], newOrder[i - 1]) = (newOrder[i - 1], newOrder[i]);
                    SaveCustomWindowOrder(newOrder.ToArray());
                    Repaint(); // 立即重绘以显示变化
                }
                // 下移按钮（最后一个元素不能下移）
                if (i < newOrder.Count - 1 && GUILayout.Button("↓", GUILayout.Width(25)))
                {
                    (newOrder[i], newOrder[i + 1]) = (newOrder[i + 1], newOrder[i]);
                    SaveCustomWindowOrder(newOrder.ToArray());
                    Repaint(); // 立即重绘以显示变化
                }
                EditorGUILayout.Space(25);
                
                EditorGUILayout.EndHorizontal();
            }
            
            // 处理拖拽逻辑
            HandleDragLogic(newOrder);
            
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.red;
            // 应用按钮 - 手动触发重新排序
            if (GUILayout.Button("应用并刷新窗口顺序", GUILayout.Height(30)))
            {
                ApplyWindowOrderChanges();
                Repaint();
            }
            GUI.backgroundColor = Color.white;
            // 重置按钮
            if (GUILayout.Button("重置为默认顺序", GUILayout.Height(30)))
            {
                var defaultOrder = new[]
                {
                    "ScenesTools",
                    "ProjectTools", 
                    "ShaderMaterialFinder",
                    VicToolsConfig.PerformanceAnalyzerWindowName
                };
                SaveCustomWindowOrder(defaultOrder);
                ApplyWindowOrderChanges(); // 应用窗口顺序变化
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.Space();
            
            // 停靠式窗口设置
            EditorGUILayout.LabelField("窗口行为设置", style.subheading2);
            
            EditorGUILayout.HelpBox("启用此选项后，当窗口处于停靠状态时将不会自动调整窗口尺寸。", MessageType.Info);
            // 停靠式窗口选项 - 当窗口停靠状态时不自动设置窗口尺寸
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("停靠式窗口", GUILayout.Width(70));
            EditorGUI.BeginChangeCheck();
            _disableDockedWindowAutoSize = EditorGUILayout.Toggle(_disableDockedWindowAutoSize, GUILayout.Width(30));
            if (EditorGUI.EndChangeCheck())
            {
                // 立即保存设置到 EditorPrefs
                EditorPrefs.SetBool("VicTools.DisableDockedWindowAutoSize", _disableDockedWindowAutoSize);
                Debug.Log($"停靠式窗口设置已更新: {_disableDockedWindowAutoSize}");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 处理拖拽逻辑 - 完全重写版本
        /// </summary>
        /// <param name="orderList">窗口顺序列表</param>
        private void HandleDragLogic(List<string> orderList)
        {
            Event currentEvent = Event.current;
            
            // 全局拖拽目标位置计算 - 使用精确的项目矩形
            if (_isDragging && currentEvent.type == EventType.MouseDrag)
            {
                // 计算鼠标位置对应的目标索引
                int newDragToIndex = -1;
                
                // 检查鼠标是否在某个项目区域内
                for (int i = 0; i < _itemRects.Count; i++)
                {
                    if (_itemRects[i].Contains(currentEvent.mousePosition))
                    {
                        // 鼠标在项目区域内，计算具体位置
                        float relativeY = currentEvent.mousePosition.y - _itemRects[i].y;
                        float halfHeight = _itemRects[i].height * 0.5f;
                        
                        if (relativeY < halfHeight)
                        {
                            // 鼠标在上半部分，拖拽到当前项之前
                            newDragToIndex = i;
                        }
                        else
                        {
                            // 鼠标在下半部分，拖拽到当前项之后
                            newDragToIndex = i + 1;
                        }
                        break;
                    }
                }
                
                // 如果鼠标不在任何项目区域内，检查是否在列表末尾
                if (newDragToIndex == -1 && _itemRects.Count > 0)
                {
                    Rect lastRect = _itemRects[_itemRects.Count - 1];
                    if (currentEvent.mousePosition.y > lastRect.y + lastRect.height)
                    {
                        newDragToIndex = orderList.Count; // 拖拽到列表末尾
                    }
                    else if (currentEvent.mousePosition.y < _itemRects[0].y)
                    {
                        newDragToIndex = 0; // 拖拽到列表开头
                    }
                }
                
                // 确保目标索引在有效范围内
                if (newDragToIndex != -1)
                {
                    newDragToIndex = Mathf.Clamp(newDragToIndex, 0, orderList.Count);
                    _dragToIndex = newDragToIndex;
                }
                
                currentEvent.Use();
            }
            
            // 拖拽结束处理
            if (_isDragging && currentEvent.type == EventType.MouseUp)
            {
                if (_dragFromIndex >= 0 && _dragToIndex >= 0 && _dragFromIndex != _dragToIndex)
                {
                    // 执行拖拽操作
                    string draggedItem = orderList[_dragFromIndex];
                    orderList.RemoveAt(_dragFromIndex);
                    
                    // 如果拖拽到列表末尾，需要调整索引
                    if (_dragToIndex >= orderList.Count)
                    {
                        orderList.Add(draggedItem);
                    }
                    else
                    {
                        orderList.Insert(_dragToIndex, draggedItem);
                    }
                    
                    // 保存新的顺序
                    SaveCustomWindowOrder(orderList.ToArray());
                    
                    // 强制重绘以显示变化
                    Repaint();
                }
                
                // 重置拖拽状态
                _isDragging = false;
                _dragFromIndex = -1;
                _dragToIndex = -1;
                currentEvent.Use();
            }
            
            // 在拖拽过程中也需要重绘以更新拖拽指示器
            if (_isDragging)
            {
                Repaint();
            }
            
            // 绘制拖拽指示器 - 在拖拽过程中显示拖拽中的项
            if (!_isDragging || _dragFromIndex < 0 || _dragFromIndex >= orderList.Count) return;
            // 绘制拖拽中的项
            var dragRect = new Rect(currentEvent.mousePosition.x - 100, 
                currentEvent.mousePosition.y - 10, 
                200, 20);
                
            // 使用更明显的拖拽样式
            var dragStyle = new GUIStyle(EditorStyles.helpBox)
            {
                normal =
                {
                    textColor = Color.black
                },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            GUI.Box(dragRect, orderList[_dragFromIndex], dragStyle);
            GUI.backgroundColor = Color.white;
        }

        /// <summary>
        /// 处理拖拽事件
        /// </summary>
        /// <param name="rect">标签区域矩形</param>
        /// <param name="index">当前项的索引</param>
        private void HandleDragEvents(Rect rect, int index)
        {
            var currentEvent = Event.current;
            
            // 鼠标按下开始拖拽
            if (currentEvent.type == EventType.MouseDown && rect.Contains(currentEvent.mousePosition))
            {
                _dragFromIndex = index;
                _dragToIndex = index;
                _isDragging = true;
                currentEvent.Use();
                return;
            }
            
            // 鼠标悬停时显示拖拽指针
            if (!_isDragging && rect.Contains(currentEvent.mousePosition))
            {
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.MoveArrow);
            }
        }
        
        /// <summary>
        /// 应用窗口顺序变化 - 重新初始化子窗口数组
        /// </summary>
        private void ApplyWindowOrderChanges()
        {
            // 保存当前窗口状态
            var currentWindowName = CurrentWindow?.Name;
            
            // 重新初始化子窗口数组
            InitSubWindows();
            
            // 恢复当前窗口
            if (!string.IsNullOrEmpty(currentWindowName))
            {
                var targetWindow = _windows.FirstOrDefault(w => w.Name == currentWindowName);
                if (targetWindow != null)
                {
                    SetCurrentWindow(targetWindow);
                }
            }
            
            Debug.Log("窗口顺序已更新并立即生效");
        }

        private void OnLostFocus()
        {
            if (CurrentWindow != null)
                CurrentWindow.OnLostFocus();
        }

        private void OnFocus()
        {
            if (CurrentWindow != null)
                CurrentWindow.OnFocus();
        }

        private void OnDestroy()
        {
            // 在窗口销毁前保存常规窗口尺寸
            SaveNormalWindowSize();
            
            // 在窗口销毁前保存窗口位置和停靠状态
            SaveWindowPositionAndDockState();
            
            // 在窗口销毁前，如果当前是性能分析窗口，恢复常规窗口尺寸
            // 确保Unity保存的是常规窗口尺寸而不是性能分析窗口尺寸
            if (_isPerformanceAnalyzerActive && CurrentWindow != null && CurrentWindow.Name == VicToolsConfig.PerformanceAnalyzerWindowName)
            {
                // 恢复之前保存的常规窗口大小
                if (_previousWindowSize != Vector2.zero)
                {
                    position = new Rect(position.position, _previousWindowSize);
                }
                _isPerformanceAnalyzerActive = false;
            }
            
            if (CurrentWindow != null)
                CurrentWindow.OnDestroy();
        }

        private void OnEnable()
        {
            // 注册选择变化事件 - 在主窗口中注册，确保在任何窗口状态下都能更新选中数量
            Selection.selectionChanged += OnSelectionChanged;

            // 延迟初始化子窗口，避免在序列化过程中创建对象
            if (_windows == null)
            {
                try
                {
                    InitSubWindows();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"初始化VicTools子窗口失败: {ex.Message}");
                    // 创建空的子窗口数组以避免空引用
                    _windows = Array.Empty<SubWindow>();
                }
            }

            // 获取上次打开的窗口并恢复
            if (CurrentWindow == null && _windows is { Length: > 0 })
            {
                var currentWindowName = EditorPrefs.GetString("VicTools.Window.currentWindow", _windows[0].Name);
                foreach (var t in _windows)
                {
                    if (t.Name != currentWindowName) continue;
                    CurrentWindow = t;
                    break;
                }
            }

            // 加载停靠式窗口设置
            _disableDockedWindowAutoSize = EditorPrefs.GetBool("VicTools.DisableDockedWindowAutoSize", false);

            // 加载保存的常规窗口尺寸 - 仅在停靠式窗口设置关闭时执行
            if (!_disableDockedWindowAutoSize)
            {
                LoadNormalWindowSize();
            }

            // 加载窗口位置和停靠状态 - 仅在停靠式窗口设置关闭时执行
            if (!_disableDockedWindowAutoSize)
            {
                LoadWindowPositionAndDockState();
            }

            // 启用当前窗口
            if (CurrentWindow != null)
            {
                try
                {
                    CurrentWindow.OnEnable();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"启用子窗口 {CurrentWindow.Name} 失败: {ex.Message}");
                }
            }
            
            // 确保窗口显示在前面
            Focus();
            
            // 延迟执行窗口位置恢复，确保窗口完全初始化后再应用位置
            EditorApplication.delayCall += DelayedWindowRestore;
        }
        
        /// <summary>
        /// 延迟窗口恢复 - 确保窗口完全初始化后再应用位置和尺寸
        /// </summary>
        private void DelayedWindowRestore()
        {
            // 移除回调，确保只执行一次
            EditorApplication.delayCall -= DelayedWindowRestore;
            
            // 增加恢复尝试次数
            _windowRestoreAttempts++;
            
            // 使用增强的停靠状态检测
            bool isCurrentlyDocked = IsWindowDockedEnhanced();
            
            // 如果窗口不是停靠状态且停靠式窗口设置关闭，重新加载并应用窗口位置和尺寸
            if (!isCurrentlyDocked && !_disableDockedWindowAutoSize)
            {
                // 重新加载并应用窗口位置
                LoadWindowPositionAndDockState();
                
                // 强制重绘窗口，确保窗口位置和尺寸变化立即生效
                Repaint();
                
                // 延迟再次检查窗口状态，确保恢复成功
                if (_windowRestoreAttempts < MaxWindowRestoreAttempts)
                {
                    EditorApplication.delayCall += () => {
                        if (this != null)
                        {
                            // 再次检查窗口状态，确保位置和尺寸已正确恢复
                            bool finalDockedState = IsWindowDockedEnhanced();
                            if (!finalDockedState)
                            {
                                // 如果仍然不是停靠状态，再次应用保存的位置
                                LoadWindowPositionAndDockState();
                                Repaint();
                                
                                // 最终保险措施：如果仍然失败，强制应用窗口位置
                                EditorApplication.delayCall += () => {
                                    if (this != null && !IsWindowDockedEnhanced())
                                    {
                                        ForceApplyWindowPosition();
                                    }
                                };
                            }
                            else
                            {
                            }
                        }
                    };
                }
                else
                {
                    // 达到最大尝试次数，使用强制恢复方法
                    if (!IsWindowDockedEnhanced())
                    {
                        ForceApplyWindowPosition();
                    }
                }
            }
            else
            {
                // 窗口处于停靠状态，完成恢复过程
            }
            
            // 增强的窗口焦点管理 - 确保窗口显示在前面
            EnsureWindowFocus();
        }
        
        /// <summary>
        /// 确保窗口获得焦点并显示在前面 - 优化版本
        /// </summary>
        private void EnsureWindowFocus()
        {
            // 方法1：立即获取焦点
            Focus();
            
            // 方法2：延迟焦点尝试，确保窗口完全初始化
            EditorApplication.delayCall += () => {
                if (this != null)
                {
                    // 再次获取焦点
                    Focus();
                    
                    // 强制重绘窗口
                    Repaint();
                    
                    // 如果仍然不是焦点窗口，使用更直接的方法
                    if (!focusedWindow)
                    {
                        // 使用ShowUtility确保窗口显示在前面
                        ShowUtility();
                        Focus();
                        Repaint();
                    }
                }
            };
            
            // 方法3：最终保险措施 - 在更长延迟后再次尝试
            EditorApplication.delayCall += () => {
                if (this != null && !focusedWindow)
                {
                    // 使用更安全的方法：直接获取焦点和重绘
                    Focus();
                    Repaint();
                    
                    // 如果仍然没有焦点，使用ShowUtility方法
                    if (!focusedWindow)
                    {
                        ShowUtility();
                        Focus();
                        Repaint();
                    }
                }
            };
        }

        private void OnDisable()
        {
            // 在窗口禁用前保存常规窗口尺寸
            SaveNormalWindowSize();
            
            // 在窗口禁用前保存窗口位置和停靠状态
            SaveWindowPositionAndDockState();
            
            // 在窗口禁用前，如果当前是性能分析窗口，恢复常规窗口尺寸
            // 这样Unity保存的将是常规窗口尺寸而不是性能分析窗口尺寸
            if (_isPerformanceAnalyzerActive && CurrentWindow != null && CurrentWindow.Name == VicToolsConfig.PerformanceAnalyzerWindowName)
            {
                // 恢复之前保存的常规窗口大小
                if (_previousWindowSize != Vector2.zero)
                {
                    position = new Rect(position.position, _previousWindowSize);
                }
                _isPerformanceAnalyzerActive = false;
            }

            // 取消注册选择变化事件
            Selection.selectionChanged -= OnSelectionChanged;

            // 禁用当前窗口
            if (CurrentWindow != null)
                CurrentWindow.OnDisable();
        }

        /// <summary>
        /// 处理选择变化事件 - 更新全局选中对象数量
        /// </summary>
        private void OnSelectionChanged()
        {
            // 更新全局选中对象数量
            globalSelectedObjectsCount = Selection.gameObjects.Length;
            
            // 强制重绘窗口以更新显示
            Repaint();
        }

        private void OnHierarchyChange()
        {
            if (CurrentWindow != null)
                CurrentWindow.OnHierarchyChange();
        }

        void OnGUI()
        {
            // 检查是否需要更新窗口尺寸 - 在第一次GUI绘制时强制执行窗口尺寸更新（仅在停靠式窗口设置关闭时）
            if (_needsWindowSizeUpdate && !_disableDockedWindowAutoSize)
            {
                _needsWindowSizeUpdate = false;
                
                // 强制执行窗口尺寸更新
                if (CurrentWindow == null || CurrentWindow.Name != VicToolsConfig.PerformanceAnalyzerWindowName)
                {
                    // 确保应用保存的常规窗口尺寸
                    Vector2 clampedSize = new Vector2(
                        Mathf.Clamp(_normalWindowSize.x, minSize.x, maxSize.x),
                        Mathf.Clamp(_normalWindowSize.y, minSize.y, maxSize.y)
                    );
                    
                    // 强制应用窗口尺寸
                    position = new Rect(position.position, clampedSize);
                    
                    // 强制重绘窗口，确保窗口尺寸变化立即生效
                    Repaint();
                }
            }
            
            // 实时检测窗口尺寸变化并保存常规窗口尺寸（仅在停靠式窗口设置关闭时）
            if (CurrentWindow != null && CurrentWindow.Name != VicToolsConfig.PerformanceAnalyzerWindowName && !_disableDockedWindowAutoSize)
            {
                // 检查窗口尺寸是否发生变化
                if (position.size != _previousWindowSize)
                {
                    // 窗口尺寸发生变化，立即保存常规窗口尺寸
                    SaveNormalWindowSize();
                    
                    // 更新前一个窗口尺寸记录
                    _previousWindowSize = position.size;
                }
            }
            
            // 实时检测窗口位置变化并保存窗口位置和停靠状态（仅在停靠式窗口设置关闭时）
            if ((position.position != _lastWindowPosition || position.size != _lastWindowSize) && !_disableDockedWindowAutoSize)
            {
                // 窗口位置或尺寸发生变化，立即保存窗口位置和停靠状态
                SaveWindowPositionAndDockState();
                
                // 更新内部状态记录
                _lastWindowPosition = position.position;
                _lastWindowSize = position.size;
            }
            
            var style = EditorStyle.Get;    //  获取自定义样式
            
            // 添加帮助和配置按钮 - 放在窗口右上角
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace(); // 将按钮推到右侧
            
            // 帮助按钮
            GUI.backgroundColor = new Color(0.8f, 0.9f, 1.0f); // 浅蓝色背景
            if (GUILayout.Button("?", GUILayout.Width(25), GUILayout.Height(14)))
            {
                ShowHelpDialog();
            }
            GUIContent label = new GUIContent("HELP", "帮助文档");
            if (GUILayout.Button(label, GUILayout.Width(44), GUILayout.Height(14)))
            {
                Application.OpenURL("https://nyq1lw99l7.feishu.cn/wiki/GVDYwV0TFiEPl2kTJzWcwcI6n6d?from=from_copylink");
            }
            // 配置按钮
            GUIContent setlabel = new GUIContent("◎", "设置");
            GUI.backgroundColor = new Color(0.9f, 0.8f, 0.3f); // 黄色背景
            if (GUILayout.Button(setlabel, GUILayout.Width(25), GUILayout.Height(14)))
            {
                _showWindowOrderConfig = !_showWindowOrderConfig;
            }
            
            GUI.backgroundColor = Color.white; // 恢复默认背景色
            EditorGUILayout.EndHorizontal();
            
            // 显示菜单栏
            const int maxButtonsPerRow = 3; // 每行最多显示的按钮数量
            
            // 使用更简单可靠的多行显示逻辑
            EditorGUILayout.BeginVertical();
            
            for (var i = 0; i < _windows.Length; i += maxButtonsPerRow)
            {
                EditorGUILayout.BeginHorizontal();  
                
                // 计算当前行要显示的按钮数量
                var buttonsInThisRow = Mathf.Min(maxButtonsPerRow, _windows.Length - i);
                
                for (var j = 0; j < buttonsInThisRow; j++)
                {
                    var buttonIndex = i + j;
                    // 使用自定义样式
                    if (GUILayout.Button(_windows[buttonIndex].Name, CurrentWindow == _windows[buttonIndex] ? style.menuButtonSelected : style.menuButton))
                        SetCurrentWindow(_windows[buttonIndex]);
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();

            // 添加分隔线 - 分隔菜单按钮与工具界面
            // GUILayout.Space(5);
            
            // 创建更醒目的黄色分隔线
            GUIStyle separatorStyle = new GUIStyle();
            separatorStyle.normal.background = CreateColorTexture(1, 1, new Color(0.7f, 0.5f, 0.0f)); // 黄色
            separatorStyle.normal.background.hideFlags = HideFlags.HideAndDontSave;
            GUILayout.Box("", separatorStyle, GUILayout.Height(4), GUILayout.ExpandWidth(true));
            
            GUILayout.Space(5);

            // 显示窗口配置界面或当前窗口内容
            if (_showWindowOrderConfig)
            {
                ShowWindowOrderConfig();
            }
            else if (CurrentWindow != null)
            {
                CurrentWindow.OnGUI();
            }
            else
            {
                GUIStyle customStyle = new GUIStyle(GUI.skin.label != null ? GUI.skin.label : new GUIStyle());
                customStyle.fontSize = 22;
                customStyle.fontStyle = FontStyle.Bold;
                customStyle.normal.textColor = new Color(0.5f, 0.3f, 0.3f);
                customStyle.hover.textColor = new Color(0.5f, 0.3f, 0.3f);
                customStyle.alignment = TextAnchor.MiddleCenter; // 文字居中
                GUILayout.Label("↑ 请在上面选择一个工具 ↑", customStyle);
            }
        }

        void SetCurrentWindow(SubWindow window)
        {
            // 如果当前在窗口配置界面中，并且点击的是工具标签按钮（不是配置界面内部的按钮），则自动退出配置界面
            // 通过检查window是否在windows数组中来确定是否是工具标签点击
            if (_showWindowOrderConfig && _windows != null && _windows.Contains(window))
            {
                _showWindowOrderConfig = false;
            }
            
            // 检查是否禁用停靠式窗口自动尺寸设置
            bool shouldDisableAutoSize = _disableDockedWindowAutoSize;
            
            // 窗口大小管理逻辑 - 仅在未禁用自动尺寸设置时执行
            if (!shouldDisableAutoSize)
            {
                bool wasPerformanceAnalyzer = _isPerformanceAnalyzerActive;
                bool isSwitchingToPerformanceAnalyzer = window.Name == VicToolsConfig.PerformanceAnalyzerWindowName;
                
                // 如果之前是性能分析器，现在切换到其他窗口，保存当前大小并恢复之前的大小
                if (wasPerformanceAnalyzer && !isSwitchingToPerformanceAnalyzer)
                {
                    // 保存性能分析器窗口的专属尺寸
                    _performanceAnalyzerSize = position.size;
                    
                    // 恢复之前保存的常规窗口大小
                    if (_previousWindowSize != Vector2.zero)
                    {
                        position = new Rect(position.position, _previousWindowSize);
                    }
                    _isPerformanceAnalyzerActive = false;
                    
                    // 额外确保：当从性能分析窗口切换到常规窗口时，重新加载并应用保存的常规窗口尺寸（仅在停靠式窗口设置关闭时）
                    if (!_disableDockedWindowAutoSize)
                    {
                        LoadNormalWindowSize();
                        
                        // 标记需要更新窗口尺寸，确保在第一次点击时就能生效
                        _needsWindowSizeUpdate = true;
                    }
                    
                    // 强制重绘窗口，确保窗口尺寸变化立即生效
                    Repaint();
                }
                // 如果切换到性能分析器，保存当前大小并设置新大小
                else if (!wasPerformanceAnalyzer && isSwitchingToPerformanceAnalyzer)
                {
                    // 保存当前常规窗口大小以便切换回来时恢复
                    _previousWindowSize = position.size;
                    
                    // 设置性能分析器窗口大小
                    position = new Rect(position.position, _performanceAnalyzerSize);
                    _isPerformanceAnalyzerActive = true;
                    
                    // 强制重绘窗口，确保窗口尺寸变化立即生效
                    Repaint();
                }
                // 如果是从一个常规窗口切换到另一个常规窗口，确保应用正确的常规窗口尺寸
                else if (!wasPerformanceAnalyzer)
                {
                    // 确保应用保存的常规窗口尺寸（仅在停靠式窗口设置关闭时）
                    if (!_disableDockedWindowAutoSize)
                    {
                        LoadNormalWindowSize();
                        
                        // 标记需要更新窗口尺寸，确保在第一次点击时就能生效
                        _needsWindowSizeUpdate = true;
                    }
                    
                    // 强制重绘窗口，确保窗口尺寸变化立即生效
                    Repaint();
                }
            }
            else
            {
                // 当禁用自动尺寸设置时，清除相关标记
                _needsWindowSizeUpdate = false;
            }
            
            if (CurrentWindow != null)
            {
                CurrentWindow.OnLostFocus();
                CurrentWindow.OnDisable();
            }
            
            CurrentWindow = window;
            CurrentWindow.OnFocus();
            CurrentWindow.OnEnable();
            EditorPrefs.SetString("VicTools.Window.currentWindow", window.Name);
        }

        /// 通过类型名称设置当前窗口
        public void SetCurrentWindowByTypeName(string typeName)
        {
            if (_windows == null)
                InitSubWindows();

            if (_windows == null) return;
            var targetWindow = _windows.FirstOrDefault(w => w.GetType().Name == typeName);
            if (targetWindow != null)
            {
                SetCurrentWindow(targetWindow);
            }
            else
            {
                Debug.LogWarning($"未找到类型为 {typeName} 的子窗口");
            }
        }



        /// 显示帮助对话框 - 使用自定义窗口支持链接
        private void ShowHelpDialog()
        {
            VicToolsHelpWindow.ShowWindow();
        }

        /// <summary>
        /// 加载保存的常规窗口尺寸
        /// </summary>
        private void LoadNormalWindowSize()
        {
            // 从EditorPrefs加载保存的常规窗口尺寸
            var savedWidth = EditorPrefs.GetFloat("VicTools.NormalWindowSize.Width", _normalWindowSize.x);
            var savedHeight = EditorPrefs.GetFloat("VicTools.NormalWindowSize.Height", _normalWindowSize.y);
            
            _normalWindowSize = new Vector2(savedWidth, savedHeight);
            
            // 如果当前不是性能分析窗口，应用保存的常规窗口尺寸
            if (CurrentWindow != null && CurrentWindow.Name == VicToolsConfig.PerformanceAnalyzerWindowName) return;
            // 确保窗口尺寸在合理范围内
            var clampedSize = new Vector2(
                Mathf.Clamp(_normalWindowSize.x, minSize.x, maxSize.x),
                Mathf.Clamp(_normalWindowSize.y, minSize.y, maxSize.y)
            );
                
            // 强制应用保存的常规窗口尺寸，确保窗口切换时立即生效
            position = new Rect(position.position, clampedSize);
                
            // 强制重绘窗口，确保窗口尺寸变化立即生效
            Repaint();
        }
        
        /// <summary>
        /// 保存常规窗口尺寸
        /// </summary>
        private void SaveNormalWindowSize()
        {
            // 只有当当前不是性能分析窗口时才保存常规窗口尺寸
            if (CurrentWindow is { Name: VicToolsConfig.PerformanceAnalyzerWindowName }) return;
            // 保存当前窗口尺寸作为常规窗口尺寸
            _normalWindowSize = position.size;
                
            // 保存到EditorPrefs
            EditorPrefs.SetFloat("VicTools.NormalWindowSize.Width", _normalWindowSize.x);
            EditorPrefs.SetFloat("VicTools.NormalWindowSize.Height", _normalWindowSize.y);
        }

        /// <summary>
        /// 加载窗口位置和停靠状态 - 增强版本
        /// </summary>
        private void LoadWindowPositionAndDockState()
        {
            // 使用增强的停靠状态检测
            var currentDockedState = IsWindowDockedEnhanced();
            
            // 加载保存的停靠状态
            var savedDockedState = EditorPrefs.GetBool("VicTools.WindowIsDocked", false);
            
            // 加载窗口位置
            var savedX = EditorPrefs.GetFloat("VicTools.WindowPosition.X", position.x);
            var savedY = EditorPrefs.GetFloat("VicTools.WindowPosition.Y", position.y);
            var savedWidth = EditorPrefs.GetFloat("VicTools.WindowPosition.Width", position.width);
            var savedHeight = EditorPrefs.GetFloat("VicTools.WindowPosition.Height", position.height);
            
            _lastWindowPosition = new Vector2(savedX, savedY);
            _lastWindowSize = new Vector2(savedWidth, savedHeight);
            
            // 只有当窗口不是停靠状态时才恢复保存的位置和尺寸
            if (!currentDockedState && !savedDockedState)
            {
                // 确保窗口位置在屏幕范围内
                var clampedPosition = new Vector2(
                    Mathf.Clamp(_lastWindowPosition.x, 0, Screen.currentResolution.width - _lastWindowSize.x),
                    Mathf.Clamp(_lastWindowPosition.y, 0, Screen.currentResolution.height - _lastWindowSize.y)
                );
                
                // 确保窗口尺寸在合理范围内
                var clampedSize = new Vector2(
                    Mathf.Clamp(_lastWindowSize.x, minSize.x, maxSize.x),
                    Mathf.Clamp(_lastWindowSize.y, minSize.y, maxSize.y)
                );
                
                // 检查保存的位置是否有效（不在屏幕外）
                var isValidPosition = 
                    clampedPosition.x >= 0 && 
                    clampedPosition.y >= 0 &&
                    clampedPosition.x + clampedSize.x <= Screen.currentResolution.width &&
                    clampedPosition.y + clampedSize.y <= Screen.currentResolution.height;
                
                // 如果位置有效，应用保存的窗口位置和尺寸
                if (isValidPosition)
                {
                    position = new Rect(clampedPosition, clampedSize);
                    
                    // 强制重绘窗口，确保窗口位置和尺寸变化立即生效
                    Repaint();
                    
                    Debug.Log($"成功恢复窗口位置: {clampedPosition}, 尺寸: {clampedSize}");
                }
                else
                {
                    Debug.LogWarning($"保存的窗口位置无效，使用默认位置: {clampedPosition}, 屏幕尺寸: {Screen.currentResolution.width}x{Screen.currentResolution.height}");
                }
            }
            else
            {
                Debug.Log($"窗口处于停靠状态，不恢复位置。当前停靠: {currentDockedState}, 保存停靠: {savedDockedState}");
            }
            
            // 更新内部状态
        }

        /// <summary>
        /// 保存窗口位置和停靠状态
        /// </summary>
        private void SaveWindowPositionAndDockState()
        {
            // 检测当前窗口是否停靠
            bool currentDockedState = IsWindowDocked();
            
            // 保存停靠状态
            EditorPrefs.SetBool("VicTools.WindowIsDocked", currentDockedState);
            
            // 只有当窗口不是停靠状态时才保存位置和尺寸
            if (!currentDockedState)
            {
                // 保存窗口位置
                EditorPrefs.SetFloat("VicTools.WindowPosition.X", position.x);
                EditorPrefs.SetFloat("VicTools.WindowPosition.Y", position.y);
                EditorPrefs.SetFloat("VicTools.WindowPosition.Width", position.width);
                EditorPrefs.SetFloat("VicTools.WindowPosition.Height", position.height);
            }
            
            // 更新内部状态
            _lastWindowPosition = position.position;
            _lastWindowSize = position.size;
        }

        /// <summary>
        /// 检测窗口是否处于停靠状态 - 增强版本
        /// </summary>
        /// <returns>如果窗口停靠返回true，否则返回false</returns>
        private bool IsWindowDocked()
        {
            // 方法1：使用反射来检测窗口是否停靠（最准确的方法）
            try
            {
                var dockedProperty = typeof(EditorWindow).GetProperty("docked", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (dockedProperty != null)
                {
                    bool isDocked = (bool)dockedProperty.GetValue(this);
                    // Debug.Log($"反射检测窗口停靠状态: {isDocked}");
                    return isDocked;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"反射检测窗口停靠状态失败: {ex.Message}");
            }
            
            // 方法2：检查窗口是否最大化（最大化窗口通常也是停靠的）
            try
            {
                var maximizedProperty = typeof(EditorWindow).GetProperty("maximized", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (maximizedProperty != null)
                {
                    bool isMaximized = (bool)maximizedProperty.GetValue(this);
                    if (isMaximized)
                    {
                        // Debug.Log("窗口处于最大化状态，视为停靠状态");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"检测窗口最大化状态失败: {ex.Message}");
            }
            
            // 方法3：通过窗口位置检测停靠状态（备用方法）
            return IsWindowPositionDocked();
        }
        
        /// <summary>
        /// 增强的窗口停靠状态检测 - 综合多种检测方法
        /// </summary>
        /// <returns>如果窗口停靠返回true，否则返回false</returns>
        private bool IsWindowDockedEnhanced()
        {
            // 方法1：反射检测（最准确）
            var reflectionDocked = IsWindowDockedByReflection();
            if (reflectionDocked) return true;
            
            // 方法2：最大化状态检测
            var isWindowMaximized = IsWindowMaximized();
            if (isWindowMaximized) return true;
            
            // 方法3：位置检测
            var positionDocked = IsWindowPositionDocked();
            if (positionDocked) return true;
            
            // 方法4：编辑器边界检测
            var withinEditorBounds = IsWindowWithinEditorBounds();
            if (withinEditorBounds) return true;
            
            // 方法5：窗口行为检测（检查窗口是否具有停靠窗口的典型行为）
            var behaviorDocked = IsWindowBehaviorDocked();
            return behaviorDocked;
        }
        
        /// <summary>
        /// 使用反射检测窗口停靠状态
        /// </summary>
        /// <returns>如果窗口停靠返回true</returns>
        private bool IsWindowDockedByReflection()
        {
            try
            {
                var dockedProperty = typeof(EditorWindow).GetProperty("docked", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (dockedProperty != null)
                {
                    return (bool)dockedProperty.GetValue(this);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"反射检测窗口停靠状态失败: {ex.Message}");
            }
            return false;
        }
        
        /// <summary>
        /// 检测窗口是否最大化
        /// </summary>
        /// <returns>如果窗口最大化返回true</returns>
        private bool IsWindowMaximized()
        {
            try
            {
                var maximizedProperty = typeof(EditorWindow).GetProperty("maximized", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (maximizedProperty != null)
                {
                    return (bool)maximizedProperty.GetValue(this);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"检测窗口最大化状态失败: {ex.Message}");
            }
            return false;
        }
        
        /// <summary>
        /// 通过窗口行为检测停靠状态
        /// </summary>
        /// <returns>如果窗口行为显示停靠状态返回true</returns>
        private bool IsWindowBehaviorDocked()
        {
            // 检查窗口是否具有停靠窗口的典型行为特征
            // 1. 窗口位置固定且靠近编辑器边界
            // 2. 窗口尺寸与编辑器主窗口尺寸有特定关系
            // 3. 窗口无法自由移动（在某些Unity版本中）
            
            try
            {
                // 获取编辑器主窗口位置
                var mainWindowPosition = EditorGUIUtility.GetMainWindowPosition();
                
                // 检查窗口是否与编辑器主窗口对齐
                var alignedWithMainWindow = 
                    Mathf.Abs(position.x - mainWindowPosition.x) < 10f ||
                    Mathf.Abs(position.y - mainWindowPosition.y) < 10f ||
                    Mathf.Abs(position.x + position.width - (mainWindowPosition.x + mainWindowPosition.width)) < 10f ||
                    Mathf.Abs(position.y + position.height - (mainWindowPosition.y + mainWindowPosition.height)) < 10f;
                
                // 检查窗口尺寸是否与编辑器主窗口有特定关系
                var sizeRelation = 
                    position.width >= mainWindowPosition.width * 0.8f ||
                    position.height >= mainWindowPosition.height * 0.8f;
                
                // 如果窗口与编辑器主窗口对齐且尺寸有特定关系，可能处于停靠状态
                return alignedWithMainWindow && sizeRelation;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"窗口行为检测失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通过窗口位置检测停靠状态（增强的备用方法）
        /// </summary>
        /// <returns>如果窗口位置显示停靠状态返回true</returns>
        private bool IsWindowPositionDocked()
        {
            // 检查窗口是否靠近屏幕边缘
            const float edgeTolerance = 5f; // 减少边缘容差，提高检测精度
            
            var nearLeftEdge = position.x <= edgeTolerance;
            var nearRightEdge = position.x >= Screen.currentResolution.width - position.width - edgeTolerance;
            var nearTopEdge = position.y <= edgeTolerance;
            var nearBottomEdge = position.y >= Screen.currentResolution.height - position.height - edgeTolerance;
            
            // 如果窗口靠近任何屏幕边缘，可能处于停靠状态
            var isNearEdge = nearLeftEdge || nearRightEdge || nearTopEdge || nearBottomEdge;
            
            // 额外检查：如果窗口尺寸接近屏幕尺寸，可能处于停靠状态
            var isFullWidth = position.width >= Screen.currentResolution.width - edgeTolerance * 2;
            var isFullHeight = position.height >= Screen.currentResolution.height - edgeTolerance * 2;
            
            // 检查窗口是否在Unity编辑器主窗口内（非浮动窗口）
            var isWithinEditorBounds = IsWindowWithinEditorBounds();
            
            // Debug.Log($"位置检测: 靠近边缘={isNearEdge}, 全宽={isFullWidth}, 全高={isFullHeight}, 编辑器内={isWithinEditorBounds}");
            
            return isNearEdge || isFullWidth || isFullHeight || isWithinEditorBounds;
        }
        
        /// <summary>
        /// 检查窗口是否在Unity编辑器主窗口边界内
        /// </summary>
        /// <returns>如果窗口在编辑器主窗口内返回true</returns>
        private bool IsWindowWithinEditorBounds()
        {
            try
            {
                // 获取Unity编辑器主窗口位置
                var mainWindowPosition = EditorGUIUtility.GetMainWindowPosition();
                
                // 检查当前窗口是否在编辑器主窗口内
                var isWithinX = position.x >= mainWindowPosition.x && 
                                position.x + position.width <= mainWindowPosition.x + mainWindowPosition.width;
                var isWithinY = position.y >= mainWindowPosition.y && 
                                position.y + position.height <= mainWindowPosition.y + mainWindowPosition.height;
                
                // 如果窗口完全在编辑器主窗口内，且不是浮动窗口，则可能是停靠状态
                return isWithinX && isWithinY;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"检测编辑器主窗口边界失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 强制应用窗口位置 - 最终的窗口位置恢复保障机制
        /// 当所有其他恢复方法都失败时使用此方法（仅在停靠式窗口设置关闭时）
        /// </summary>
        private void ForceApplyWindowPosition()
        {
            Debug.Log("使用强制窗口位置恢复方法");
            
            // 方法1：重新加载保存的窗口位置（仅在停靠式窗口设置关闭时）
            if (!_disableDockedWindowAutoSize)
            {
                LoadWindowPositionAndDockState();
            }
            
            // 方法2：延迟再次尝试应用位置
            EditorApplication.delayCall += () =>
            {
                if (!this) return;
                // 再次加载并应用窗口位置
                LoadWindowPositionAndDockState();
                    
                // 强制重绘窗口
                Repaint();
                    
                // 方法3：如果仍然失败，使用更直接的方法
                EditorApplication.delayCall += () =>
                {
                    if (!this) return;
                    // 检查窗口是否仍然不在正确位置
                    var isCurrentlyDocked = IsWindowDockedEnhanced();
                    if (isCurrentlyDocked) return;
                    // 获取保存的位置
                    var savedX = EditorPrefs.GetFloat("VicTools.WindowPosition.X", position.x);
                    var savedY = EditorPrefs.GetFloat("VicTools.WindowPosition.Y", position.y);
                    var savedWidth = EditorPrefs.GetFloat("VicTools.WindowPosition.Width", position.width);
                    var savedHeight = EditorPrefs.GetFloat("VicTools.WindowPosition.Height", position.height);
                                
                    // 确保位置在屏幕范围内
                    var clampedPosition = new Vector2(
                        Mathf.Clamp(savedX, 0, Screen.currentResolution.width - savedWidth),
                        Mathf.Clamp(savedY, 0, Screen.currentResolution.height - savedHeight)
                    );
                                
                    // 确保尺寸在合理范围内
                    var clampedSize = new Vector2(
                        Mathf.Clamp(savedWidth, minSize.x, maxSize.x),
                        Mathf.Clamp(savedHeight, minSize.y, maxSize.y)
                    );
                                
                    // 直接设置窗口位置
                    position = new Rect(clampedPosition, clampedSize);
                                
                    // 强制重绘
                    Repaint();
                                
                    Debug.Log($"强制应用窗口位置: {clampedPosition}, 尺寸: {clampedSize}");
                };
            };
        }

        /// 创建纯色纹理 - VicToolsWindow 自己的实现
        /// <param name="width">纹理宽度</param>
        /// <param name="height">纹理高度</param>
        /// <param name="color">纹理颜色</param>
        /// <returns>创建的纹理</returns>
        private Texture2D CreateColorTexture(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height);
            var pixels = new Color[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            // 正确设置HideFlags.HideAndDontSave，避免Unity编辑器试图持久化临时纹理
            // 这解决了断言失败：'!(o->TestHideFlag(Object::kDontSaveInEditor) && (options & kAllowDontSaveObjectsToBePersistent) == 0)'
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}

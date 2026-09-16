// ComputeBufferLightManagerEditor v1.0 初版：ComputeBufferLightManager 的 Inspector 参数名中文显示
// 说明：只改显示层，字段名保持原样（重命名会导致场景中已保存的序列化值丢失），绘制时用"字段名 -> 中文"映射替换标签。
//       新增或重命名字段后必须同步更新 ChineseLabels / ElementLabels，否则会回退为 Unity 默认名并在 Inspector 顶部告警。

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;

namespace Vic.Editor
{
    [CustomEditor(typeof(ComputeBufferLightManager))]
    public class ComputeBufferLightManagerEditor : UnityEditor.Editor
    {
        // ● 字段名 -> 中文显示名（顺序与 ComputeBuffer.cs 中的字段声明一致）
        private static readonly Dictionary<string, string> ChineseLabels = new Dictionary<string, string>
        {
            // 点光源
            { "_usePointLight",          "启用点光照" },
            { "_pointLightIntensity",    "点光照强度" },
            { "_lightRangeMultiplier",   "点光照范围倍增" },
            { "_lightFalloff",           "点光照衰减幂次" },

            // 回弹动画
            { "_enableBounceAnimation",  "启用回弹动画" },
            { "_bounceStartIntensity",   "回弹起始强度" },
            { "_bounceTargetIntensity",  "回弹目标强度" },
            { "_bounceAnimationSpeed",   "回弹动画速度" },

            // 聚光灯
            { "_useSpotLight",             "启用聚光灯" },
            { "_spotLightIntensity",       "聚光灯强度" },
            { "_spotLightRangeMultiplier", "聚光灯范围倍增" },
            { "_spotLightFalloff",         "聚光灯衰减幂次" },
            { "_spotLightAmount",          "最大聚光灯数量" },

            // 光斑纹理
            { "_useSpotTexture",        "启用光斑纹理" },
            { "_spotTexture",           "聚光灯纹理" },
            { "_spotTextureContrast",   "光斑纹理对比度" },
            { "_spotTextureSize",       "光斑纹理大小" },
            { "_spotTextureIntensity",  "光斑纹理强度" },

            // 材质管理
            { "dontDestroyOnLoad",  "载入新场景时不销毁" },
            { "autoFindMaterials",  "自动查找材质" },
            { "targetMaterials",    "受控材质列表" },

            // 光源列表
            { "pointLights",  "场景点光源列表" },
            { "spotLights",   "场景聚光灯列表" },

            // 剔除
            { "_enableDistanceCulling",  "启用距离剔除" },
            { "_distanceCullFactor",     "距离剔除系数" },
            { "_enableFrustumCulling",   "启用视锥体剔除" },
            { "_frustumCullTolerance",   "视锥体剔除容差" },

            // 性能
            { "maxLights",                 "最大点光源数量" },
            { "updateFrequency",           "点光源更新频率 (Hz)" },
            { "spotLightUpdateFrequency",  "聚光灯更新频率 (Hz)" },
        };

        // ● 列表字段 -> 元素中文名（Unity 默认显示 Element 0/1/2）
        private static readonly Dictionary<string, string> ElementLabels = new Dictionary<string, string>
        {
            { "pointLights",      "点光源" },
            { "spotLights",       "聚光灯" },
            { "targetMaterials",  "材质" },
        };

        // ● 静态自检结果：字段名变更后映射会静默失效，这里一次性校验并在 Inspector 提示
        private static readonly string[] StaleLabels = FindStaleLabels();

        private static string[] FindStaleLabels()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            System.Type type = typeof(ComputeBufferLightManager);
            List<string> stale = new List<string>();

            foreach (string fieldName in ChineseLabels.Keys)
            {
                if (type.GetField(fieldName, flags) == null) stale.Add(fieldName);
            }
            foreach (string fieldName in ElementLabels.Keys)
            {
                if (type.GetField(fieldName, flags) == null) stale.Add(fieldName);
            }
            return stale.ToArray();
        }

        public override void OnInspectorGUI()
        {
            if (StaleLabels.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    "以下字段名已不存在，中文标签失效，请更新 ComputeBufferLightManagerEditor 的映射表：\n"
                    + string.Join("、", StaleLabels), MessageType.Warning);
            }

            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                // ● 脚本槽保持默认的对象选择框（只读）
                if (property.propertyPath == "m_Script")
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(property, true);
                    EditorGUI.EndDisabledGroup();
                    continue;
                }

                GUIContent label = ChineseLabels.TryGetValue(property.name, out string chinese)
                    ? new GUIContent(chinese, property.tooltip)
                    : new GUIContent(property.displayName, property.tooltip);

                if (ElementLabels.TryGetValue(property.name, out string elementName))
                {
                    DrawArray(property, label, elementName);
                }
                else
                {
                    EditorGUILayout.PropertyField(property, label, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>绘制列表，把 Element 0/1/2 换成"点光源 0"这类中文元素名。</summary>
        private static void DrawArray(SerializedProperty property, GUIContent label, string elementName)
        {
            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, label, true);
            if (!property.isExpanded) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(property.FindPropertyRelative("Array.size"), new GUIContent("数量"), true);

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(element, new GUIContent(elementName + " " + i), true);
            }
            EditorGUI.indentLevel--;
        }
    }
}

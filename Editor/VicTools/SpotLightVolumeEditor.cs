/// SpotLightVolume v5.0 自定义Inspector面板
/// 存档/读档/预设按钮 + 参数面板 + 射线遮挡参数
/// SpotLightVolume v6.0 重构代码，改进重复的GetComponent调用，消除 UpdateGeometry 和 UpdateMaterial 中的重复计算

using UnityEngine;
using UnityEditor;
using Vic.Runtime;

namespace VicTools
{
    [CustomEditor(typeof(SpotLightVolume))]
    [CanEditMultipleObjects]
    public class SpotLightVolumeEditor : Editor
    {
        // 距离
        private SerializedProperty lightSourceRadius;
        private SerializedProperty fallOffStart;
        private SerializedProperty maxDistance;
        // 羽化
        private SerializedProperty edgeFade;
        private SerializedProperty endFade;
        private SerializedProperty glareFrontal;
        private SerializedProperty glareBehind;
        // 外观
        private SerializedProperty intensity;
        private SerializedProperty startBoostIntensity;
        private SerializedProperty startBoostRange;
        private SerializedProperty centerFade;
        private SerializedProperty colorFromLight;
        private SerializedProperty volumeColor;
        private SerializedProperty blendMode;
        // Mesh
        private SerializedProperty coneSides;
        private SerializedProperty coneSegments;
        // 射线遮挡
        private SerializedProperty enableOcclusion;
        private SerializedProperty occlusionLayerMask;
        private SerializedProperty occlusionUpdateInterval;
        // 蒙版投影
        private SerializedProperty enableMask;
        private SerializedProperty maskTexture;
        private SerializedProperty maskIntensity;

        private const string SaveFolderBase = "Library/VicTools/SpotLightVolume";
        private const string PresetFolder = "Packages/com.youdoo.victools/Runtime/Presets/SpotLightVolume";

        void OnEnable()
        {
            lightSourceRadius = serializedObject.FindProperty("lightSourceRadius");
            fallOffStart = serializedObject.FindProperty("fallOffStart");
            maxDistance = serializedObject.FindProperty("maxDistance");
            edgeFade = serializedObject.FindProperty("edgeFade");
            endFade = serializedObject.FindProperty("endFade");
            glareFrontal = serializedObject.FindProperty("glareFrontal");
            glareBehind = serializedObject.FindProperty("glareBehind");
            intensity = serializedObject.FindProperty("intensity");
            startBoostIntensity = serializedObject.FindProperty("startBoostIntensity");
            startBoostRange = serializedObject.FindProperty("startBoostRange");
            centerFade = serializedObject.FindProperty("centerFade");
            colorFromLight = serializedObject.FindProperty("colorFromLight");
            volumeColor = serializedObject.FindProperty("volumeColor");
            blendMode = serializedObject.FindProperty("blendMode");
            coneSides = serializedObject.FindProperty("coneSides");
            coneSegments = serializedObject.FindProperty("coneSegments");
            enableOcclusion = serializedObject.FindProperty("enableOcclusion");
            occlusionLayerMask = serializedObject.FindProperty("occlusionLayerMask");
            occlusionUpdateInterval = serializedObject.FindProperty("occlusionUpdateInterval");
            maskTexture = serializedObject.FindProperty("maskTexture");
            maskIntensity = serializedObject.FindProperty("maskIntensity");
            enableMask = serializedObject.FindProperty("enableMask");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader();
            EditorGUILayout.Space(4);

            DrawSection("距离控制", () =>
            {
                EditorGUILayout.PropertyField(lightSourceRadius, new GUIContent("光源半径(始端宽度)"));
                EditorGUILayout.PropertyField(fallOffStart, new GUIContent("衰减起始"));
                EditorGUILayout.PropertyField(maxDistance, new GUIContent("最远距离"));
            });

            DrawSection("羽化控制", () =>
            {
                EditorGUILayout.PropertyField(edgeFade, new GUIContent("边缘羽化"));
                EditorGUILayout.PropertyField(endFade, new GUIContent("末端羽化"));
                EditorGUILayout.PropertyField(glareFrontal, new GUIContent("正面眩光"));
                EditorGUILayout.PropertyField(glareBehind, new GUIContent("背面眩光"));
            });

            DrawSection("外观", () =>
            {
                EditorGUILayout.PropertyField(intensity, new GUIContent("强度"));
                EditorGUILayout.PropertyField(startBoostIntensity, new GUIContent("起始亮度"));
                EditorGUILayout.PropertyField(startBoostRange, new GUIContent("起始亮度范围"));
                EditorGUILayout.PropertyField(centerFade, new GUIContent("中心渐变距离"));
                EditorGUILayout.PropertyField(blendMode, new GUIContent("混合方式"));
                EditorGUILayout.PropertyField(colorFromLight, new GUIContent("跟随灯光颜色"));
                if (!colorFromLight.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(volumeColor, new GUIContent("自定义颜色"));
                    EditorGUI.indentLevel--;
                }
            });

            DrawSection("Mesh 质量", () =>
            {
                EditorGUILayout.PropertyField(coneSides, new GUIContent("圆锥面数"));
                EditorGUILayout.PropertyField(coneSegments, new GUIContent("圆锥分段"));
            });

            DrawSection("射线遮挡", () =>
            {
                EditorGUILayout.PropertyField(enableOcclusion, new GUIContent("启用遮挡"));
                if (enableOcclusion.boolValue)
                {
                    EditorGUILayout.PropertyField(occlusionLayerMask, new GUIContent("检测层"));
                    EditorGUILayout.PropertyField(occlusionUpdateInterval, new GUIContent("检测间隔(秒)"));
                }
            });

            DrawSection("蒙版投影", () =>
            {
                EditorGUILayout.PropertyField(enableMask, new GUIContent("启用蒙版"));
                if (enableMask.boolValue)
                {
                    EditorGUILayout.PropertyField(maskTexture, new GUIContent("蒙版纹理", "黑白纹理模拟窗格投影，白色透光黑色遮光"));
                    EditorGUILayout.PropertyField(maskIntensity, new GUIContent("蒙版强度"));
                }
            });

            serializedObject.ApplyModifiedProperties();
        }

        #region UI辅助

        private static void DrawSection(string label, System.Action drawContent)
        {
            drawContent();
            EditorGUILayout.Space(4);
        }

        private new void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("探照灯体积雾", EditorStyles.boldLabel);

            DrawHeaderButton("存档", new Color(0.3f, 0.8f, 1.0f), "保存当前体积光参数和灯光角度/颜色到本地JSON文件", 50,
                () => EditorApplication.delayCall += SaveParameters);

            DrawHeaderButton("读档 ▾", new Color(0.5f, 1.0f, 0.5f), "从已保存的本地存档中加载参数", 55,
                () => ShowFileDropdown(SaveFolderBase));

            DrawHeaderButton("预设 ▾", new Color(0.9f, 0.7f, 1.0f), "加载内置预设参数（包内只读）", 55,
                () => ShowFileDropdown(PresetFolder));

            DrawHeaderButton("禁用光照", new Color(1.0f, 0.6f, 0.4f), "将SpotLight设为不产生实时照明", 60,
                DisableLightIllumination);

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawHeaderButton(string text, Color bgColor, string tooltip, float width, System.Action onClick)
        {
            GUI.backgroundColor = bgColor;
            if (GUILayout.Button(new GUIContent(text, tooltip), GUILayout.Width(width)))
                onClick();
        }

        #endregion

        #region 功能操作

        private void DisableLightIllumination()
        {
            foreach (var t in targets)
            {
                var vol = t as SpotLightVolume;
                if (vol == null) continue;

                var light = vol.GetComponent<Light>();
                if (light == null) continue;

                Undo.RecordObject(light, "Disable Light Illumination");
                light.renderMode = LightRenderMode.ForceVertex;
                light.intensity = 0f;
                light.bounceIntensity = 0f;
                light.shadows = LightShadows.None;
                EditorUtility.SetDirty(light);
            }
            Debug.Log("[SpotLightVolume] 已禁用灯光实时照明");
        }

        #endregion

        #region 存档/读档

        private void SaveParameters()
        {
            EnsureDirectory(SaveFolderBase);

            string path = EditorUtility.SaveFilePanel("保存体积光参数", SaveFolderBase, "VolumePreset", "json");
            if (string.IsNullOrEmpty(path)) return;

            SaveToFile(path);
            Debug.Log("[SpotLightVolume] 参数已保存: " + path);
        }

        /// 通用文件下拉菜单（存档和预设共用）
        private void ShowFileDropdown(string folder)
        {
            EnsureDirectory(folder);

            string[] files = System.IO.Directory.GetFiles(folder, "*.json");
            var menu = new GenericMenu();

            if (files.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("（无文件）"));
            }
            else
            {
                foreach (string file in files)
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                    string filePath = file;
                    menu.AddItem(new GUIContent(fileName), false, () =>
                    {
                        EditorApplication.delayCall += () => LoadFromFile(filePath);
                    });
                }
            }
            menu.ShowAsContext();
        }

        private void SaveToFile(string path)
        {
            var vol = target as SpotLightVolume;
            if (vol == null) return;

            var light = vol.GetComponent<Light>();
            var data = new SpotLightVolumeData
            {
                lightSourceRadius = vol.lightSourceRadius,
                fallOffStart = vol.fallOffStart,
                maxDistance = vol.maxDistance,
                edgeFade = vol.edgeFade,
                endFade = vol.endFade,
                glareFrontal = vol.glareFrontal,
                glareBehind = vol.glareBehind,
                intensity = vol.intensity,
                startBoostIntensity = vol.startBoostIntensity,
                startBoostRange = vol.startBoostRange,
                centerFade = vol.centerFade,
                colorFromLight = vol.colorFromLight,
                volumeColor = ColorToArray(vol.volumeColor),
                blendMode = (int)vol.blendMode,
                coneSides = vol.coneSides,
                coneSegments = vol.coneSegments,
                enableOcclusion = vol.enableOcclusion,
                occlusionLayerMask = (int)vol.occlusionLayerMask,
                occlusionUpdateInterval = vol.occlusionUpdateInterval,
                occlusionDetectTriggers = vol.occlusionDetectTriggers,
                spotAngle = light != null ? light.spotAngle : 30f,
                innerSpotAngle = light != null ? light.innerSpotAngle : 0f,
                lightColor = light != null ? ColorToArray(light.color) : new float[] { 1, 1, 1, 1 },
                enableMask = vol.enableMask,
                maskIntensity = vol.maskIntensity,
                maskTexturePath = vol.maskTexture != null ? AssetDatabase.GetAssetPath(vol.maskTexture) : ""
            };

            System.IO.File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }

        private void LoadFromFile(string path)
        {
            if (!System.IO.File.Exists(path)) return;

            var data = JsonUtility.FromJson<SpotLightVolumeData>(System.IO.File.ReadAllText(path));
            if (data == null) return;

            foreach (var t in targets)
            {
                var vol = t as SpotLightVolume;
                if (vol == null) continue;

                Undo.RecordObject(vol, "Load SpotLightVolume Preset");
                data.ApplyTo(vol);

                var light = vol.GetComponent<Light>();
                if (light != null)
                {
                    Undo.RecordObject(light, "Load SpotLightVolume Light Preset");
                    data.ApplyLightTo(light);
                    EditorUtility.SetDirty(light);
                }
                EditorUtility.SetDirty(vol);
            }

            Debug.Log("[SpotLightVolume] 参数已加载: " + System.IO.Path.GetFileNameWithoutExtension(path));
        }

        #endregion

        #region 工具方法

        private static void EnsureDirectory(string path)
        {
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
        }

        private static float[] ColorToArray(Color c) => new[] { c.r, c.g, c.b, c.a };

        private static Color ArrayToColor(float[] arr)
        {
            return arr != null && arr.Length == 4 ? new Color(arr[0], arr[1], arr[2], arr[3]) : Color.white;
        }

        #endregion

        #region 序列化数据

        [System.Serializable]
        private class SpotLightVolumeData
        {
            public float lightSourceRadius;
            public float fallOffStart;
            public float maxDistance;
            public float edgeFade;
            public float endFade;
            public float glareFrontal;
            public float glareBehind;
            public float intensity;
            public float startBoostIntensity;
            public float startBoostRange;
            public float centerFade;
            public bool colorFromLight;
            public float[] volumeColor;
            public int blendMode;
            public int coneSides;
            public int coneSegments;
            public bool enableOcclusion;
            public int occlusionLayerMask;
            public float occlusionUpdateInterval;
            public bool occlusionDetectTriggers;
            public float spotAngle;
            public float innerSpotAngle;
            public float[] lightColor;
            public bool enableMask;
            public float maskIntensity;
            public string maskTexturePath; // AssetDatabase 路径

            public void ApplyTo(SpotLightVolume vol)
            {
                vol.lightSourceRadius = lightSourceRadius;
                vol.fallOffStart = fallOffStart;
                vol.maxDistance = maxDistance;
                vol.edgeFade = edgeFade;
                vol.endFade = endFade;
                vol.glareFrontal = glareFrontal;
                vol.glareBehind = glareBehind;
                vol.intensity = intensity;
                vol.startBoostIntensity = startBoostIntensity;
                vol.startBoostRange = startBoostRange;
                vol.centerFade = centerFade;
                vol.colorFromLight = colorFromLight;
                if (volumeColor != null && volumeColor.Length == 4)
                    vol.volumeColor = new Color(volumeColor[0], volumeColor[1], volumeColor[2], volumeColor[3]);
                vol.blendMode = (VolumeBlendMode)blendMode;
                vol.coneSides = coneSides;
                vol.coneSegments = coneSegments;
                vol.enableOcclusion = enableOcclusion;
                vol.occlusionLayerMask = occlusionLayerMask;
                vol.occlusionUpdateInterval = occlusionUpdateInterval;
                vol.occlusionDetectTriggers = occlusionDetectTriggers;
                vol.enableMask = enableMask;
                vol.maskIntensity = maskIntensity;
                if (!string.IsNullOrEmpty(maskTexturePath))
                    vol.maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskTexturePath);
                else
                    vol.maskTexture = null;
            }

            public void ApplyLightTo(Light light)
            {
                light.spotAngle = spotAngle;
                light.innerSpotAngle = innerSpotAngle;
                if (lightColor != null && lightColor.Length == 4)
                    light.color = new Color(lightColor[0], lightColor[1], lightColor[2], lightColor[3]);
            }
        }

        #endregion
    }
}

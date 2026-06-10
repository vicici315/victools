/// SpotLightVolume v3.0 自定义Inspector面板
/// 存档/读档/预设按钮 + 参数面板

using UnityEngine;
using UnityEditor;
using Vic.Runtime;

namespace VicTools
{
    [CustomEditor(typeof(SpotLightVolume))]
    [CanEditMultipleObjects]
    public class SpotLightVolumeEditor : Editor
    {
        private SerializedProperty lightSourceRadius;
        private SerializedProperty fallOffStart;
        private SerializedProperty maxDistance;
        private SerializedProperty edgeFade;
        private SerializedProperty endFade;
        private SerializedProperty glareFrontal;
        private SerializedProperty glareBehind;
        private SerializedProperty intensity;
        private SerializedProperty colorFromLight;
        private SerializedProperty volumeColor;
        private SerializedProperty blendMode;
        private SerializedProperty coneSides;
        private SerializedProperty coneSegments;

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
            colorFromLight = serializedObject.FindProperty("colorFromLight");
            volumeColor = serializedObject.FindProperty("volumeColor");
            blendMode = serializedObject.FindProperty("blendMode");
            coneSides = serializedObject.FindProperty("coneSides");
            coneSegments = serializedObject.FindProperty("coneSegments");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ─── 顶部标题 + 存档/读档/预设按钮 ───
            DrawHeader();
            EditorGUILayout.Space(4);

            // 距离
            EditorGUILayout.LabelField("距离控制", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(lightSourceRadius, new GUIContent("光源半径(始端宽度)"));
            EditorGUILayout.PropertyField(fallOffStart, new GUIContent("衰减起始"));
            EditorGUILayout.PropertyField(maxDistance, new GUIContent("最远距离"));
            EditorGUILayout.Space(4);

            // 羽化
            EditorGUILayout.LabelField("羽化控制", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(edgeFade, new GUIContent("边缘羽化"));
            EditorGUILayout.PropertyField(endFade, new GUIContent("末端羽化"));
            EditorGUILayout.PropertyField(glareFrontal, new GUIContent("正面眩光"));
            EditorGUILayout.PropertyField(glareBehind, new GUIContent("背面眩光"));
            EditorGUILayout.Space(4);

            // 外观
            EditorGUILayout.LabelField("外观", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(intensity, new GUIContent("强度"));
            EditorGUILayout.PropertyField(blendMode, new GUIContent("混合方式"));
            EditorGUILayout.PropertyField(colorFromLight, new GUIContent("跟随灯光颜色"));
            if (!colorFromLight.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(volumeColor, new GUIContent("自定义颜色"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // Mesh质量
            EditorGUILayout.LabelField("Mesh 质量", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(coneSides, new GUIContent("圆锥面数"));
            EditorGUILayout.PropertyField(coneSegments, new GUIContent("圆锥分段"));

            serializedObject.ApplyModifiedProperties();
        }


        // ─── 顶部按钮栏 ───
        private new void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("探照灯体积雾", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
            if (GUILayout.Button(new GUIContent("存档", "保存当前体积光参数和灯光角度/颜色到本地JSON文件"), GUILayout.Width(50)))
                EditorApplication.delayCall += SaveParameters;

            GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
            if (GUILayout.Button(new GUIContent("读档 ▾", "从已保存的本地存档中加载参数"), GUILayout.Width(55)))
                ShowLoadDropdown();

            GUI.backgroundColor = new Color(0.9f, 0.7f, 1.0f);
            if (GUILayout.Button(new GUIContent("预设 ▾", "加载内置预设参数（包内只读）"), GUILayout.Width(55)))
                ShowPresetDropdown();

            GUI.backgroundColor = new Color(1.0f, 0.6f, 0.4f);
            if (GUILayout.Button(new GUIContent("禁用光照", "将SpotLight设为不产生实时照明（intensity=0, 无阴影）"), GUILayout.Width(60)))
                DisableLightIllumination();

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        // ─── 禁用光照 ───
        private void DisableLightIllumination()
        {
            foreach (var t in targets)
            {
                SpotLightVolume vol = t as SpotLightVolume;
                if (vol == null) continue;

                Light light = vol.GetComponent<Light>();
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

        // ─── 存档 ───
        private void SaveParameters()
        {
            if (!System.IO.Directory.Exists(SaveFolderBase))
                System.IO.Directory.CreateDirectory(SaveFolderBase);

            string path = EditorUtility.SaveFilePanel(
                "保存体积光参数", SaveFolderBase, "VolumePreset", "json");

            if (string.IsNullOrEmpty(path)) return;

            SaveToFile(path);
            Debug.Log("[SpotLightVolume] 参数已保存: " + path);
        }

        // ─── 读档下拉 ───
        private void ShowLoadDropdown()
        {
            if (!System.IO.Directory.Exists(SaveFolderBase))
                System.IO.Directory.CreateDirectory(SaveFolderBase);

            string[] files = System.IO.Directory.GetFiles(SaveFolderBase, "*.json");
            GenericMenu menu = new GenericMenu();

            if (files.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("（无存档）"));
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

        // ─── 预设下拉 ───
        private void ShowPresetDropdown()
        {
            if (!System.IO.Directory.Exists(PresetFolder))
                System.IO.Directory.CreateDirectory(PresetFolder);

            string[] files = System.IO.Directory.GetFiles(PresetFolder, "*.json");
            GenericMenu menu = new GenericMenu();

            if (files.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("（无预设）"));
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

        // ─── 序列化/反序列化 ───
        private void SaveToFile(string path)
        {
            SpotLightVolume vol = target as SpotLightVolume;
            if (vol == null) return;

            Light light = vol.GetComponent<Light>();

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
                colorFromLight = vol.colorFromLight,
                volumeColor = new float[] { vol.volumeColor.r, vol.volumeColor.g, vol.volumeColor.b, vol.volumeColor.a },
                blendMode = (int)vol.blendMode,
                coneSides = vol.coneSides,
                coneSegments = vol.coneSegments,
                // Light参数（仅颜色和角度）
                spotAngle = light != null ? light.spotAngle : 30f,
                innerSpotAngle = light != null ? light.innerSpotAngle : 0f,
                lightColor = light != null ? new float[] { light.color.r, light.color.g, light.color.b, light.color.a } : new float[] { 1, 1, 1, 1 }
            };

            string json = JsonUtility.ToJson(data, true);
            System.IO.File.WriteAllText(path, json);
        }

        private void LoadFromFile(string path)
        {
            if (!System.IO.File.Exists(path)) return;

            string json = System.IO.File.ReadAllText(path);
            var data = JsonUtility.FromJson<SpotLightVolumeData>(json);
            if (data == null) return;

            foreach (var t in targets)
            {
                SpotLightVolume vol = t as SpotLightVolume;
                if (vol == null) continue;

                Undo.RecordObject(vol, "Load SpotLightVolume Preset");

                vol.lightSourceRadius = data.lightSourceRadius;
                vol.fallOffStart = data.fallOffStart;
                vol.maxDistance = data.maxDistance;
                vol.edgeFade = data.edgeFade;
                vol.endFade = data.endFade;
                vol.glareFrontal = data.glareFrontal;
                vol.glareBehind = data.glareBehind;
                vol.intensity = data.intensity;
                vol.colorFromLight = data.colorFromLight;
                if (data.volumeColor != null && data.volumeColor.Length == 4)
                    vol.volumeColor = new Color(data.volumeColor[0], data.volumeColor[1], data.volumeColor[2], data.volumeColor[3]);
                vol.blendMode = (VolumeBlendMode)data.blendMode;
                vol.coneSides = data.coneSides;
                vol.coneSegments = data.coneSegments;

                // 恢复Light参数（仅颜色和角度）
                Light light = vol.GetComponent<Light>();
                if (light != null)
                {
                    Undo.RecordObject(light, "Load SpotLightVolume Light Preset");
                    light.spotAngle = data.spotAngle;
                    light.innerSpotAngle = data.innerSpotAngle;
                    if (data.lightColor != null && data.lightColor.Length == 4)
                        light.color = new Color(data.lightColor[0], data.lightColor[1], data.lightColor[2], data.lightColor[3]);
                    EditorUtility.SetDirty(light);
                }

                EditorUtility.SetDirty(vol);
            }

            Debug.Log("[SpotLightVolume] 参数已加载: " + System.IO.Path.GetFileNameWithoutExtension(path));
        }

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
            public bool colorFromLight;
            public float[] volumeColor;
            public int blendMode;
            public int coneSides;
            public int coneSegments;
            // Light参数
            public float spotAngle;
            public float innerSpotAngle;
            public float[] lightColor;
        }
    }
}

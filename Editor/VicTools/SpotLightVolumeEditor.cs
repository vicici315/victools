/// SpotLightVolume 自定义Inspector面板

using UnityEngine;
using UnityEditor;
using Vic.Runtime;

namespace VicTools
{
    [CustomEditor(typeof(SpotLightVolume))]
    [CanEditMultipleObjects]
    public class SpotLightVolumeEditor : Editor
    {
        private SerializedProperty startDistance;
        private SerializedProperty maxDistance;
        private SerializedProperty edgeFade;
        private SerializedProperty endFade;
        private SerializedProperty depthFadeDistance;
        private SerializedProperty intensity;
        private SerializedProperty colorFromLight;
        private SerializedProperty volumeColor;
        private SerializedProperty blendMode;
        private SerializedProperty coneSides;
        private SerializedProperty coneSegments;

        void OnEnable()
        {
            startDistance = serializedObject.FindProperty("startDistance");
            maxDistance = serializedObject.FindProperty("maxDistance");
            edgeFade = serializedObject.FindProperty("edgeFade");
            endFade = serializedObject.FindProperty("endFade");
            depthFadeDistance = serializedObject.FindProperty("depthFadeDistance");
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

            EditorGUILayout.LabelField("探照灯体积雾 (SpotLightVolume)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // 距离
            EditorGUILayout.LabelField("距离控制", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(startDistance, new GUIContent("起始距离"));
            EditorGUILayout.PropertyField(maxDistance, new GUIContent("最长距离"));
            EditorGUILayout.Space(4);

            // 羽化
            EditorGUILayout.LabelField("羽化控制", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(edgeFade, new GUIContent("边缘羽化"));
            EditorGUILayout.PropertyField(endFade, new GUIContent("末端羽化"));
            EditorGUILayout.PropertyField(depthFadeDistance, new GUIContent("深度混合距离"));
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
    }
}

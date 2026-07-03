// RotationControllerEditor —— RotationController 的自定义 Inspector。
// 根据 RotationMode 只显示相关参数，隐藏无关参数。

using UnityEditor;
using UnityEngine;
using Vic.Runtime;

[CustomEditor(typeof(RotationController))]
[CanEditMultipleObjects]
public class RotationControllerEditor : Editor
{
    // 基础
    private SerializedProperty rotationMode;
    private SerializedProperty rotationSpeed;
    private SerializedProperty rotationAxis;
    private SerializedProperty rotateInWorldSpace;

    // 控制选项
    private SerializedProperty isRotationEnabled;
    private SerializedProperty autoStartOnAwake;

    // 来回旋转
    private SerializedProperty pingPongAngleMin;
    private SerializedProperty pingPongAngleMax;
    private SerializedProperty pingPongEaseCurve;

    // 平滑旋转
    private SerializedProperty smoothLerpSpeed;

    // 速度波动
    private SerializedProperty useOscillatingSpeed;
    private SerializedProperty oscillationAmplitude;
    private SerializedProperty oscillationFrequency;

    private void OnEnable()
    {
        rotationMode = serializedObject.FindProperty("rotationMode");
        rotationSpeed = serializedObject.FindProperty("rotationSpeed");
        rotationAxis = serializedObject.FindProperty("rotationAxis");
        rotateInWorldSpace = serializedObject.FindProperty("rotateInWorldSpace");

        isRotationEnabled = serializedObject.FindProperty("isRotationEnabled");
        autoStartOnAwake = serializedObject.FindProperty("autoStartOnAwake");

        pingPongAngleMin = serializedObject.FindProperty("pingPongAngleMin");
        pingPongAngleMax = serializedObject.FindProperty("pingPongAngleMax");
        pingPongEaseCurve = serializedObject.FindProperty("pingPongEaseCurve");

        smoothLerpSpeed = serializedObject.FindProperty("smoothLerpSpeed");

        useOscillatingSpeed = serializedObject.FindProperty("useOscillatingSpeed");
        oscillationAmplitude = serializedObject.FindProperty("oscillationAmplitude");
        oscillationFrequency = serializedObject.FindProperty("oscillationFrequency");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var mode = (RotationMode)rotationMode.enumValueIndex;
        bool isContinuous = mode == RotationMode.Continuous || mode == RotationMode.ContinuousSmooth;
        bool isPingPong = mode == RotationMode.PingPong;

        // ── 基础设置 ──
        // EditorGUILayout.LabelField("基础设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(rotationMode);
        EditorGUILayout.PropertyField(rotationSpeed);
        EditorGUILayout.PropertyField(rotationAxis);
        if (isContinuous)
            EditorGUILayout.PropertyField(rotateInWorldSpace);

        // ── 控制选项 ──
        // EditorGUILayout.Space();
        // EditorGUILayout.LabelField("控制选项", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(isRotationEnabled);
        EditorGUILayout.PropertyField(autoStartOnAwake);

        // ── 平滑旋转设置（仅 ContinuousSmooth）──
        if (mode == RotationMode.ContinuousSmooth)
        {
            // EditorGUILayout.Space();
            // EditorGUILayout.LabelField("平滑旋转设置", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(smoothLerpSpeed);
        }

        // ── 来回旋转设置（仅 PingPong）──
        if (isPingPong)
        {
            // EditorGUILayout.Space();
            // EditorGUILayout.LabelField("来回旋转设置", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(pingPongAngleMin);
            EditorGUILayout.PropertyField(pingPongAngleMax);
            EditorGUILayout.PropertyField(pingPongEaseCurve);
        }

        // ── 速度波动设置（仅持续旋转 / 平滑持续旋转）──
        if (isContinuous)
        {
            // EditorGUILayout.Space();
            // EditorGUILayout.LabelField("速度波动设置", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useOscillatingSpeed);
            if (useOscillatingSpeed.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(oscillationAmplitude);
                EditorGUILayout.PropertyField(oscillationFrequency);
                EditorGUI.indentLevel--;
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}

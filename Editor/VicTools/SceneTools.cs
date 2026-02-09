using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace VicTools
{
    /// 场景工具类 - 提供场景操作相关的公共静态方法

    public static class SceneTools
    {
        private static Light FindMainDirectionalLight()
        {
            Light[] allLights = Object.FindObjectsOfType<Light>();
            List<Light> directionalLights = new List<Light>();
                
            // 收集所有方向灯
            foreach (Light light in allLights)
            {
                if (light.type == LightType.Directional)
                {
                    directionalLights.Add(light);
                }
            }
            if (directionalLights.Count == 0)
            {
                return null;
            }
            // 优先返回启用的方向灯
            foreach (Light light in directionalLights)
            {
                if (light.enabled)
                {
                    return light;
                }
            }
            // 如果没有启用的，返回第一个方向灯
            return directionalLights[0];
        }
        public static void ApplyLightDirectionToMaterials()
        {
            Vector3 lightDirection = Vector3.forward;
            int materialCount = 0;
            HashSet<Material> processedMaterials = new HashSet<Material>();
            Light light = FindMainDirectionalLight();
            if (light != null)
            {
                lightDirection = -light.transform.forward;
                // 只处理当前场景中用到的材质
                Renderer[] allRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                foreach (Renderer renderer in allRenderers)
                {
                    foreach (Material mat in renderer.sharedMaterials)
                    {
                        if (mat != null && !processedMaterials.Contains(mat) &&
                            mat.shader && mat.shader.name == "Custom/PBR_Mobile")
                        {
                            if (mat.HasProperty("_BakedSpecularDirection"))
                            {
                                mat.SetVector("_BakedSpecularDirection", lightDirection);
                                EditorUtility.SetDirty(mat);
                                processedMaterials.Add(mat);
                                materialCount++;
                            }
                        }
                    }
                }
            }

            // 保存更改
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            // 显示结果
            if (materialCount > 0)
            {
                EditorUtility.DisplayDialog("成功", 
                    $"已成功将灯光方向应用到 {materialCount} 个材质\n方向: {lightDirection}", 
                    "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("警告", 
                    "未在场景中找到使用 Custom/PBR_Mobile 的材质", 
                    "确定");
            }
        }
        /// 选择所有层级 - 当选择了父物体时选择所有子物体，当选择了子物体时选择父物体及所有子物体
        /// 改进：当选中对象父物体是Prefab或者直接选择的是Prefab则只选择该Prefab以下的子对象
        /// 公共函数调用方法：SceneTools.SelectAllHierarchy();
        public static void SelectAllHierarchy()
        {
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                Debug.Log("未选中任何对象");
                return;
            }

            var allObjectsToSelect = new HashSet<GameObject>();
            
            foreach (var selectedObject in selectedObjects)
            {
                // 检查当前对象或其父对象是否是Prefab实例
                var isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(selectedObject);
                var parent = selectedObject.transform.parent;
                var parentIsPrefab = parent && PrefabUtility.IsPartOfPrefabInstance(parent.gameObject);
                
                // 如果当前对象是Prefab实例或其父对象是Prefab实例，找到Prefab根对象并选择整个Prefab层级
                if (isPrefabInstance || parentIsPrefab)
                {
                    // 找到Prefab实例根对象
                    var prefabRoot = selectedObject.transform;
                    while (prefabRoot.parent && PrefabUtility.IsPartOfPrefabInstance(prefabRoot.parent.gameObject))
                    {
                        prefabRoot = prefabRoot.parent;
                    }
                    
                    // 选择Prefab实例及其所有子物体
                    allObjectsToSelect.Add(prefabRoot.gameObject);
                    AddAllChildren(prefabRoot, allObjectsToSelect);
                    continue;
                }
                
                // 如果当前选择的是父物体，选择所有子物体
                if (selectedObject.transform.childCount > 0)
                {
                    // 选择父物体及其所有子物体
                    allObjectsToSelect.Add(selectedObject);
                    AddAllChildren(selectedObject.transform, allObjectsToSelect);
                }
                // 如果当前选择的是子物体，选择父物体及所有子物体
                else if (selectedObject.transform.parent)
                {
                    // 找到根父物体
                    var rootParent = selectedObject.transform;
                    while (rootParent.parent)
                    {
                        rootParent = rootParent.parent;
                    }
                    
                    // 选择根父物体及其所有子物体
                    allObjectsToSelect.Add(rootParent.gameObject);
                    AddAllChildren(rootParent, allObjectsToSelect);
                }
                // 如果当前选择的是没有子物体也没有父物体的物体，只选择它自己
                else
                {
                    allObjectsToSelect.Add(selectedObject);
                }
            }

            if (allObjectsToSelect.Count <= 0) return;
            Selection.objects = allObjectsToSelect.ToArray<Object>();
            EditorGUIUtility.PingObject(allObjectsToSelect.First());
            Debug.Log($"已选择 {allObjectsToSelect.Count} 个层级对象");
        }

        /// 递归添加所有子物体到集合中
        private static void AddAllChildren(Transform parent, HashSet<GameObject> objectSet)
        {
            foreach (Transform child in parent)
            {
                objectSet.Add(child.gameObject);
                AddAllChildren(child, objectSet);
            }
        }

        /// 将材质赋予场景中选中的模型（支持撤销）
        /// <param name="material">要赋予的材质</param>
        public static void AssignMaterialToSelectedModels(Material material)
        {
            if (!material)
            {
                Debug.LogWarning("无法赋予材质：材质为空");
                return;
            }

            // 获取当前选中的游戏对象
            var selectedObjects = Selection.gameObjects;
            
            if (selectedObjects.Length == 0)
            {
                Debug.LogWarning("无法赋予材质：场景中没有选中的模型");
                EditorUtility.DisplayDialog("赋予材质失败", "请先在场景中选择要赋予材质的模型", "确定");
                return;
            }

            var assignedCount = 0;
            var objectsWithoutRenderer = new List<GameObject>();

            // 开始记录撤销操作
            Undo.RecordObjects(selectedObjects, $"Assign Material '{material.name}'");

            // 遍历所有选中的对象
            foreach (var o in selectedObjects)
            {
                var obj = (GameObject)o;
                // 获取对象的Renderer组件
                var renderer = obj.GetComponent<Renderer>();
                
                if (renderer)
                {
                    // 记录Renderer的材质状态用于撤销
                    Undo.RecordObject(renderer, $"Assign Material '{material.name}' to {obj.name}");
                    
                    // 将材质赋予Renderer
                    renderer.sharedMaterial = material;
                    assignedCount++;
                    Debug.Log($"已将材质 '{material.name}' 赋予对象 '{obj.name}'");
                }
                else
                {
                    objectsWithoutRenderer.Add(obj);
                }
            }

            // 显示结果
            if (assignedCount > 0)
            {
                var message = $"成功将材质 '{material.name}' 赋予 {assignedCount} 个模型";

                if (objectsWithoutRenderer.Count <= 0) return;
                message += $"\n\n有 {objectsWithoutRenderer.Count} 个对象没有Renderer组件：";
                objectsWithoutRenderer.Aggregate(message, (current, obj) => current + $"\n- {obj.name}");

                // EditorUtility.DisplayDialog("材质赋予完成", message, "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("赋予材质失败", "选中的对象都没有Renderer组件，无法赋予材质", "确定");
            }
        }

        /// 设置选择的2个以上对象放入最后选择的对象中
        public static void SetSelectedObjectsAsChildren()
        {
            // 获取当前选中的所有 GameObject
            GameObject[] selected = Selection.gameObjects;

            // 至少需要两个对象：N-1 个子物体 + 1 个父物体
            if (selected.Length < 2)
            {
                Debug.LogWarning("请至少选择两个对象：最后一个将作为父物体。");
                return;
            }

            // 最后一个选中的是父物体
            GameObject parent = selected[selected.Length - 1];
            
            // 其余为子物体（排除父物体）
            var children = selected.Take(selected.Length - 1).Where(go => go != parent).ToArray();

            if (children.Length == 0)
            {
                Debug.LogWarning("没有有效的子物体可设置。");
                return;
            }

            // 开始 Undo 组
            Undo.SetCurrentGroupName("设置对象层级");
            int group = Undo.GetCurrentGroup();

            foreach (GameObject child in children)
            {
                // ✅ 使用专门的Undo.SetTransformParent方法，确保Undo操作正确
                // 这个方法会正确处理Transform的父对象变化，保持世界坐标不变
                Undo.SetTransformParent(child.transform, parent.transform, "设置父对象: " + child.name);
            }

            // 保持父物体被选中
            Selection.objects = new Object[] { parent };

            Undo.CollapseUndoOperations(group);
            
            // 操作成功提示
            string successMessage = $"层级设置完成：成功将 {children.Length} 个对象设置为 {parent.name} 的子对象\n（世界坐标已保持，可按 Ctrl+Z 撤销操作）";
            Debug.Log(successMessage);
        }
        /// 将选中对象放置到最外层级（移除父对象）
        public static void RemoveSelectedObjectsFromParent()
        {
            // 获取当前选中的所有 GameObject
            GameObject[] selected = Selection.gameObjects;

            if (selected.Length == 0)
            {
                Debug.LogWarning("请至少选择一个对象。");
                return;
            }

            // 筛选出有父对象的对象
            var objectsWithParent = selected.Where(go => go.transform.parent != null).ToArray();

            if (objectsWithParent.Length == 0)
            {
                Debug.LogWarning("选中的对象都已经在最外层级（没有父对象）。");
                return;
            }

            // 开始 Undo 组
            Undo.SetCurrentGroupName("跳出层级");
            int group = Undo.GetCurrentGroup();

            foreach (GameObject obj in objectsWithParent)
            {
                // ✅ 使用专门的Undo.SetTransformParent方法，确保Undo操作正确
                // 这个方法会正确处理Transform的父对象变化，保持世界坐标不变
                // 将父对象设置为null表示放到场景根层级
                Undo.SetTransformParent(obj.transform, null, "移除父对象: " + obj.name);
            }

            // 保持对象被选中
            Selection.objects = objectsWithParent;

            Undo.CollapseUndoOperations(group);
            
            // 操作成功提示
            string successMessage = $"跳出层级完成：成功将 {objectsWithParent.Length} 个对象放置到最外层级\n（世界坐标已保持，可按 Ctrl+Z 撤销操作）";
            Debug.Log(successMessage);
        }
    }
}

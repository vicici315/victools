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
        
        /// 选择场景中所有非Prefab的模型对象
        /// 公共函数调用方法：SceneTools.SelectAllNoPrefab();
        public static void SelectAllNoPrefab()
        {
            // 调用新的通用选择函数，默认选择非Prefab的Mesh对象
            SelectObjectsByType(true, false, false);
        }
        
        /// 根据选项选择场景中的物体
        /// <param name="selectMesh">是否选择非Prefab的Mesh对象</param>
        /// <param name="selectPrefab">是否选择Prefab对象</param>
        /// <param name="selectLODGroup">是否选择带LODGroup的对象</param>
        /// <param name="selectMissMat">是否选择丢失材质球的模型对象</param>
        /// <param name="selectMissScript">是否选择丢失脚本的对象</param>
        /// <param name="selAct">勾选时只挑选激活的对象，取消时只挑选未激活的对象</param>
        /// <param name="selMeshObj">是否挑选Mesh对象</param>
        /// <param name="selParticleObj">是否挑选粒子对象</param>
        public static void SelectObjectsByType(bool selectMesh = true, bool selectPrefab = false, bool selectLODGroup = false, bool selectMissMat = false, bool selectMissScript = false, bool selAct = false, bool selMeshObj = false, bool selParticleObj = false, bool selParent = false)
        {
            // 辅助方法：检查对象是否符合激活状态条件
            bool CheckActivationState(GameObject obj)
            {
                if (selAct)
                {
                    // 勾选时只挑选激活的对象
                    return obj.activeSelf;
                }
                else
                {
                    // 取消时只挑选未激活的对象
                    return !obj.activeSelf;
                }
            }
            
            // 辅助方法：根据selParent获取应该选中的对象
            GameObject GetTargetObject(GameObject obj)
            {
                if (!selParent)
                {
                    return obj;
                }
                
                // 如果勾选了selectPrefab，找到上级Prefab对象
                if (selectPrefab && PrefabUtility.IsPartOfPrefabInstance(obj))
                {
                    // 获取Prefab的最外层对象（Prefab根对象）
                    var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(obj);
                    if (prefabRoot != null && prefabRoot != obj)
                    {
                        // 如果找到的Prefab根对象不是当前对象，说明当前对象在Prefab内部
                        return prefabRoot;
                    }
                    else if (obj.transform.parent != null)
                    {
                        // 如果已经是Prefab根对象，检查父对象是否也是Prefab
                        var parent = obj.transform.parent.gameObject;
                        if (PrefabUtility.IsPartOfPrefabInstance(parent))
                        {
                            // 父对象也是Prefab，选择父对象
                            return parent;
                        }
                        else
                        {
                            // 父对象不是Prefab，保持选择当前Prefab对象
                            return obj;
                        }
                    }
                    else
                    {
                        // 如果没有父对象，保持原对象
                        return obj;
                    }
                }
                else
                {
                    // 其他情况，找到最顶级的父对象（根对象）
                    return obj.transform.root.gameObject;
                }
            }
            
            // 判断是否为排除模式（所有5个选项都为false时）
            bool isExcludeMode = !selectMesh && !selectPrefab && !selectLODGroup && !selectMissMat && !selectMissScript;
            
            // 获取场景中的游戏对象
            GameObject[] allGameObjects;
            // 始终使用GetRootGameObjects获取根对象及其所有子对象，确保能遍历到所有对象
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var allObjects = new List<GameObject>();
            foreach (var root in roots)
            {
                // 获取根对象及其所有子对象（包括未激活的）
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var t in transforms)
                {
                    allObjects.Add(t.gameObject);
                }
            }
            allGameObjects = allObjects.ToArray();
            
            var selectedObjects = new HashSet<GameObject>();
            
            foreach (var gameObject in allGameObjects)
            {
                bool shouldSelect = false;
                
                if (isExcludeMode)
                {
                    // 排除模式：排除Prefab、Mesh、带LOD的对象、丢失脚本的对象
                    shouldSelect = true; // 默认选择
                    
                    // 首先检查激活状态
                    if (!CheckActivationState(gameObject))
                    {
                        shouldSelect = false;
                    }
                    
                    // 排除Prefab对象
                    if (shouldSelect && PrefabUtility.IsPartOfPrefabInstance(gameObject))
                    {
                        shouldSelect = false;
                    }
                    
                    // 排除Mesh对象
                    if (shouldSelect)
                    {
                        var renderer = gameObject.GetComponent<Renderer>();
                        if (renderer != null && renderer is MeshRenderer)
                        {
                            shouldSelect = false;
                        }
                    }
                    
                    // 排除带LODGroup的对象
                    if (shouldSelect)
                    {
                        var lodGroup = gameObject.GetComponent<LODGroup>();
                        if (lodGroup != null)
                        {
                            shouldSelect = false;
                        }
                    }
                    
                    // 排除丢失脚本的对象
                    if (shouldSelect)
                    {
                        var components = gameObject.GetComponents<Component>();
                        foreach (var component in components)
                        {
                            if (component == null)
                            {
                                shouldSelect = false;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // 正常选择模式
                    // 检查是否选择Prefab对象
                    if (selectPrefab)
                    {
                        // 检查对象是否是Prefab实例且符合激活状态条件
                        if (PrefabUtility.IsPartOfPrefabInstance(gameObject) && CheckActivationState(gameObject))
                        {
                            shouldSelect = true;
                        }
                    }
                    
                    // 检查是否选择非Prefab的Mesh对象
                    if (selectMesh && !shouldSelect)
                    {
                        // 检查对象是否是Prefab实例
                        if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
                        {
                            // 检查对象是否有Renderer组件（模型对象通常有Renderer）
                            var renderer = gameObject.GetComponent<Renderer>();
                            if (renderer != null)
                            {
                                // 进一步检查是否是MeshRenderer且符合激活状态条件
                                if (renderer is MeshRenderer && CheckActivationState(gameObject))
                                {
                                    shouldSelect = true;
                                }
                            }
                        }
                    }
                    
                    // 检查是否选择带LODGroup的对象
                    if (selectLODGroup && !shouldSelect)
                    {
                        // 检查对象是否有LODGroup组件且符合激活状态条件
                        var lodGroup = gameObject.GetComponent<LODGroup>();
                        if (lodGroup != null && CheckActivationState(gameObject))
                        {
                            shouldSelect = true;
                        }
                    }
                    
                    // 检查是否选择丢失材质球的模型对象
                    if (selectMissMat && !shouldSelect)
                    {
                        // 检查对象是否有Renderer组件
                        var renderer = gameObject.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            // 检查材质球是否丢失（材质为null或材质名称为"Default-Material"等默认材质）
                            var materials = renderer.sharedMaterials;
                            bool hasMissingMaterial = false;
                            
                            if (materials == null || materials.Length == 0)
                            {
                                hasMissingMaterial = true;
                            }
                            else
                            {
                                foreach (var material in materials)
                                {
                                    if (material == null)
                                    {
                                        hasMissingMaterial = true;
                                        break;
                                    }
                                    
                                    // 检查是否是默认材质（常见的默认材质名称）
                                    var materialName = material.name.ToLower();
                                    if (materialName.Contains("default") || 
                                        materialName.Contains("default-material") ||
                                        materialName == "material")
                                    {
                                        hasMissingMaterial = true;
                                        break;
                                    }
                                }
                            }
                            
                            if (hasMissingMaterial && CheckActivationState(gameObject))
                            {
                                shouldSelect = true;
                            }
                        }
                    }
                    
                    // 检查是否选择丢失脚本的对象
                    if (selectMissScript && !shouldSelect)
                    {
                        // 只检查当前对象本身是否有丢失的脚本
                        var components = gameObject.GetComponents<Component>();
                        bool hasMissingScript = false;
                        
                        foreach (var component in components)
                        {
                            // 如果组件为null，说明脚本丢失了
                            if (component == null)
                            {
                                hasMissingScript = true;
                                break;
                            }
                        }
                        
                        if (hasMissingScript && CheckActivationState(gameObject))
                        {
                            shouldSelect = true;
                        }
                    }
                }
                
                // 应用筛选条件：selMeshObj（模型对象）、selParticleObj（粒子对象）
                if (shouldSelect)
                {
                    // 对象类型筛选（模型对象、粒子对象）
                    bool isMeshObject = false;
                    bool isParticleObject = false;
                    
                    // 检查是否是模型对象（有MeshRenderer组件）
                    var renderer = gameObject.GetComponent<Renderer>();
                    if (renderer != null && renderer is MeshRenderer)
                    {
                        isMeshObject = true;
                    }
                    
                    // 检查是否是粒子对象（有ParticleSystem组件）
                    var particleSystem = gameObject.GetComponent<ParticleSystem>();
                    if (particleSystem != null)
                    {
                        isParticleObject = true;
                    }
                    
                    // 根据selMeshObj、selParticleObj筛选
                    bool passesTypeFilter = false;
                    
                    if (selMeshObj && isMeshObject) passesTypeFilter = true;
                    if (selParticleObj && isParticleObject) passesTypeFilter = true;
                    
                    // 如果两个选项都不勾选，则不进行类型筛选（全部通过）
                    if (!selMeshObj && !selParticleObj)
                    {
                        passesTypeFilter = true;
                    }
                    
                    shouldSelect = passesTypeFilter;
                }
                
                // 最终决定是否选择
                if (shouldSelect)
                {
                    var targetObject = GetTargetObject(gameObject);
                    // 如果使用了selParent，需要重新检查目标对象是否符合激活状态条件
                    if (selParent && targetObject != gameObject)
                    {
                        if (CheckActivationState(targetObject))
                        {
                            selectedObjects.Add(targetObject);
                        }
                    }
                    else
                    {
                        selectedObjects.Add(targetObject);
                    }
                }
            }
            
            if (selectedObjects.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "场景中没有找到符合条件的对象", "确定");
                return;
            }
            
            // 选择所有符合条件的对象
            Selection.objects = selectedObjects.ToArray();
            
            // 构建选择类型描述
            var typeDescriptions = new List<string>();
            if (isExcludeMode)
            {
                typeDescriptions.Add("排除Prefab、Mesh、带LOD的对象、丢失脚本的其他对象");
            }
            else
            {
                if (selectMesh) typeDescriptions.Add("非Prefab的Mesh对象");
                if (selectPrefab) typeDescriptions.Add("Prefab对象");
                if (selectLODGroup) typeDescriptions.Add("带LODGroup的对象");
                if (selectMissMat) typeDescriptions.Add("丢失材质球的模型对象");
                if (selectMissScript) typeDescriptions.Add("丢失脚本的对象");
            }
            
            // 添加筛选条件描述
            var filterDescriptions = new List<string>();
            if (selAct) filterDescriptions.Add("激活");
            else filterDescriptions.Add("未激活");
            
            var typeFilters = new List<string>();
            if (selMeshObj) typeFilters.Add("模型");
            if (selParticleObj) typeFilters.Add("粒子");
            if (selParent) typeFilters.Add("顶级父物体");
            
            if (typeFilters.Count > 0)
            {
                filterDescriptions.Add(string.Join("或", typeFilters));
            }
            
            var typeDescription = string.Join("、", typeDescriptions);
            var filterDescription = string.Join("、", filterDescriptions);
            
            Debug.Log($"已选择 {selectedObjects.Count} 个{typeDescription}（筛选：{filterDescription}）");
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

        // ReSharper disable Unity.PerformanceAnalysis
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

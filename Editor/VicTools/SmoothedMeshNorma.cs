// SmoothMeshNormal_1.1 添加快捷选择描边模型按钮，快速获取Meshes路径按钮
// SmoothMeshNormal_1.2 添加<覆盖>选项，支持生成平滑网格直接覆盖原有Mesh
// SmoothMeshNormal_1.3 添加<选择父对象>按钮，优化模型列表"选择"按钮只选中相应对象
// SmoothMeshNormal_1.4 添加<创建描边模型>按钮，自动克隆 _OL 描边对象并生成平滑网格，添加描边材质预置接口
// SmoothMeshNormal_1.5 添加<统一法线>按钮，将选中对象的 Mesh 法线重新计算为统一平滑法线
// SmoothMeshNormal_1.6 优化"选择描边对象"按钮：选中 _OL 对象后立即刷新列表，保留所有操作按钮控件，固定平滑mesh后缀名。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public enum SmoothedNormalsChannel
{
	VertexColor,
	Tangent,
	UV1,
	UV2,
	UV3,
	UV4
}

public class SmoothedNormalsUtility : EditorWindow
{
    #region GUI

    private string mFilePath = "";

	private const string SmoothNormalSuffix = "_SmoothNormal";

	// 覆盖模式开关，通过 EditorPrefs 持久化存储，关闭窗口或重启 Unity 后状态自动恢复
	// 勾选时：直接在原 Mesh 上写入平滑法线数据，不创建新资产文件
	// 取消时：创建带后缀的新 Mesh 资产，并将场景中的引用指向新 Mesh
	private bool mOverwrite
	{
		get { return EditorPrefs.GetBool("SmoothedNormals_Overwrite", true); }	// 默认勾选
		set { EditorPrefs.SetBool("SmoothedNormals_Overwrite", value); }
	}

	// 描边材质球，通过 EditorPrefs 持久化存储材质资产 GUID
	private Material mOutlineMaterial
	{
		get
		{
			string guid = EditorPrefs.GetString("SmoothedNormals_OutlineMaterialGUID", "");
			if (string.IsNullOrEmpty(guid)) return null;
			string path = AssetDatabase.GUIDToAssetPath(guid);
			if (string.IsNullOrEmpty(path)) return null;
			return AssetDatabase.LoadAssetAtPath<Material>(path);
		}
		set
		{
			if (value == null)
				EditorPrefs.SetString("SmoothedNormals_OutlineMaterialGUID", "");
			else
			{
				string path = AssetDatabase.GetAssetPath(value);
				string guid = AssetDatabase.AssetPathToGUID(path);
				EditorPrefs.SetString("SmoothedNormals_OutlineMaterialGUID", guid);
			}
		}
	}

	private Vector2 mScroll;

	[MenuItem("Tools/VicTools(YD)/OutlineTool: SmoothedNormal", false, 2000)]
	static void OpenTool()
	{
		GetWindow();
	}

	private static SmoothedNormalsUtility GetWindow()
	{
		var window = GetWindow<SmoothedNormalsUtility>(true, "平滑网格法线 1.6", true);
		window.minSize = new Vector2(400f, 400f);
		window.maxSize = new Vector2(400f, 5000f);
		return window;
	}

	private void OnFocus()
	{
		mMeshes = GetSelectedMeshes();
	}

	private void OnSelectionChange()
	{
		mMeshes = GetSelectedMeshes();
		Repaint();
	}

	private void OnGUI()
	{
		GUI_SelectMesh();
		GUIHelper.SeparatorSimple();
		// GUI_SelectSaveChannel();
		// GUIHelper.SeparatorSimple();
		GUI_SeletSavePath();
	}

	private void GUI_SelectMesh() 
	{
		// Mesh 列表（有选中时显示，无选中时显示提示）
		if (mMeshes != null && mMeshes.Count > 0)
		{
			mScroll = EditorGUILayout.BeginScrollView(mScroll);
			GUI.backgroundColor = Color.cyan;
			foreach (var sm in mMeshes.Values)
			{
				if (sm == null || sm.mesh == null) continue;
				GUILayout.Space(2);
				GUILayout.BeginHorizontal();
				var label = sm.mesh.name;
				if (label.Contains(SmoothNormalSuffix))
					label = label.Replace(SmoothNormalSuffix, SmoothNormalSuffix);
				GUILayout.Label(label, EditorStyles.wordWrappedMiniLabel);
				if (GUILayout.Button("选择", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
				{
					var found = new List<Object>();

					// 优先使用记录的关联对象，精确定位到具体成员，避免同名 Mesh 误选
					var assoc = sm.associatedObjects;
					if (assoc != null && assoc.Length > 0)
					{
						foreach (var obj in assoc)
						{
							if (obj is MeshFilter mf && mf != null)
								found.Add(mf.gameObject);
							else if (obj is SkinnedMeshRenderer smr && smr != null)
								found.Add(smr.gameObject);
							else if (obj is GameObject go && go != null)
								found.Add(go);
						}
					}

					// 无关联对象（如从 Project 直接选入）则按 mesh 实例引用回退搜索场景
					if (found.Count == 0)
					{
						foreach (var mf in FindObjectsOfType<MeshFilter>())
							if (mf.sharedMesh == sm.mesh) found.Add(mf.gameObject);
						foreach (var smr in FindObjectsOfType<SkinnedMeshRenderer>())
							if (smr.sharedMesh == sm.mesh) found.Add(smr.gameObject);
					}

					if (found.Count > 0)
						Selection.objects = found.ToArray();
					else
						ShowNotification(new GUIContent("场景中未找到使用该 Mesh 的对象"));
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(2);
				GUIHelper.SeparatorSimple();
			}
			GUI.backgroundColor = Color.white;
			EditorGUILayout.EndScrollView();
		}
		else
		{
			EditorGUILayout.HelpBox("请在场景或Project中选择 Mesh / 模型 / MeshFilter / SkinnedMeshRenderer，以生成平滑法线版本的网格。", MessageType.Info);
		}

		GUILayout.FlexibleSpace();

		// 操作按钮区域（始终显示）
		GUILayout.BeginHorizontal();
		GUILayout.Space(20);
		GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
		if (GUILayout.Button(new GUIContent("创建描边模型", "为选中对象克隆 _OL 描边子对象并生成平滑网格"), GUILayout.Width(115), GUILayout.Height(25)))
		{
			string matInfo = mOutlineMaterial != null
				? $"描边材质：{mOutlineMaterial.name}"
				: "⚠️ 未指定描边材质，创建后需手动赋材质";
			if (EditorUtility.DisplayDialog("创建平滑描边模型",
				"将为选中对象创建 _OL 描边子模型：\n\n" +
				"• 克隆网格对象并添加 _OL 后缀\n" +
				"• 自动生成平滑法线网格\n" +
				"• 关闭阴影投射\n" +
				$"• {matInfo}\n\n" +
				"已存在 _OL 对象的将被跳过。",
				"确认创建", "取消"))
			{
				CreateOutlineModels();
			}
		}
		// 描边材质球拖入接口
		EditorGUIUtility.labelWidth = 58;
		Material newMat = (Material)EditorGUILayout.ObjectField("描边材质", mOutlineMaterial, typeof(Material), false, GUILayout.Width(230), GUILayout.Height(25));
		if (newMat != mOutlineMaterial)
			mOutlineMaterial = newMat;
		EditorGUIUtility.labelWidth = 0;
		// 赋予材质按钮
		GUI.backgroundColor = Color.green;
		if (GUILayout.Button(new GUIContent("←", "将描边材质赋予当前选中的模型"), GUILayout.Width(25), GUILayout.Height(25)))
		{
			ApplyOutlineMaterialToSelection();
		}
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
		mOverwrite = GUILayout.Toggle(mOverwrite, new GUIContent("覆盖","生成平滑网格直接覆盖原Mesh"), GUILayout.Width(45));
		bool hasMeshes = mMeshes != null && mMeshes.Count > 0;
		string genLabel = hasMeshes && mMeshes.Count == 1 ? "生成平滑网格" : "批量生成平滑网格";
		if (GUILayout.Button(new GUIContent(genLabel,
			"将选中对象的 Mesh 按指定通道生成平滑法线网格资产。\n\n" +
			"功能说明：\n" +
			"• 对列表中的 Mesh 计算平滑法线，写入所选通道（切线/顶点色/UV）\n" +
			"• 单个对象显示为「生成平滑网格」，多个对象自动切换为「批量生成」\n" +
			"• 勾选「覆盖」时直接覆盖原始 Mesh 文件（不可撤销）\n" +
			"• 未勾选「覆盖」时在同目录生成 _SmoothedNormals 后缀的新资产\n\n" +
			"使用步骤：\n" +
			"1. 选择包含 MeshFilter/SkinnedMeshRenderer 的对象\n" +
			"2. 选择平滑法线写入的目标通道\n" +
			"3. 按需勾选「覆盖」选项\n" +
			"4. 点击此按钮生成"), GUILayout.Height(30)))
		{
			if (!hasMeshes)
			{
				ShowNotification(new GUIContent("请先选择包含 Mesh 的对象"));
			}
			else
			{
				var confirmMsg = mMeshes.Count == 1
					? $"确认为 [{mMeshes.Values.First().Name}] 生成平滑网格？"
					: $"确认批量生成 {mMeshes.Count} 个平滑网格？";
				if (mOverwrite)
					confirmMsg += "\n\n⚠️ 已开启「覆盖」模式，将直接覆盖原始 Mesh 文件，此操作不可撤销。";
				if (EditorUtility.DisplayDialog(
					mMeshes.Count == 1 ? "生成平滑网格" : "批量生成平滑网格",
					confirmMsg, "确认", "取消"))
				{
					try
					{
						var selection = new List<Object>();
						float progress = 1;
						float total = mMeshes.Count;
						foreach (var sm in mMeshes.Values)
						{
							if (sm == null) continue;
							EditorUtility.DisplayProgressBar("请稍候", (mMeshes.Count > 1 ?
								"正在批量生成平滑网格：\n" : "正在生成平滑网格：\n") + sm.Name, progress / total);
							progress++;
							Object o = CreateSmoothedMeshAsset(sm);
							if (o != null) selection.Add(o);
						}
						Selection.objects = selection.ToArray();
					}
					finally
					{
						EditorUtility.ClearProgressBar();
					}
				}
			}
		}
		if (GUILayout.Button(new GUIContent("统一法线",
			"将选中对象的 Mesh 法线重新计算为统一平滑法线。\n\n" +
			"原理：相同位置的顶点共享同一个平均法线方向，\n" +
			"消除硬边，使整个模型表面法线连续平滑。\n\n" +
			"适用场景：\n" +
			"• 修复导入模型的法线接缝/硬边问题\n" +
			"• 让描边 Shader 沿统一法线方向膨胀，避免断裂\n" +
			"• 需要全局平滑光照效果的模型\n\n" +
			"注意：勾选「覆盖」时直接修改原 Mesh，不可撤销。"),
			GUILayout.Height(30)))
		{
			UnifyMeshNormals();
		}
		GUI.backgroundColor = Color.cyan;
		if (GUILayout.Button(new GUIContent("选择父对象","选择父级对象"), GUILayout.Height(30), GUILayout.Width(80)))
		{
			SelectParentObjects();
		}
		if (GUILayout.Button(new GUIContent("选择描边对象","一键挑选所有 _OL 描边对象"), GUILayout.Height(30), GUILayout.Width(100)))
		{
			SelectOutlineObjects();
		}
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
	}

	// 统一法线：将选中对象的 Mesh 法线重新计算为统一平滑法线
	// 覆盖模式有效，不依赖后缀名筛选
	private void UnifyMeshNormals()
	{
		if (mMeshes == null || mMeshes.Count == 0) return;

		var confirmMsg = mMeshes.Count == 1
			? $"确认为 [{mMeshes.Values.First().Name}] 统一法线？"
			: $"确认批量统一 {mMeshes.Count} 个网格的法线？";
		if (mOverwrite)
			confirmMsg += "\n\n⚠️ 已开启「覆盖」模式，将直接修改原始 Mesh 法线，此操作不可撤销。";

		if (!EditorUtility.DisplayDialog("统一法线", confirmMsg, "确认", "取消"))
			return;

		try
		{
			float progress = 0;
			float total = mMeshes.Count;
			foreach (var sm in mMeshes.Values)
			{
				if (sm == null || sm.mesh == null) continue;

				EditorUtility.DisplayProgressBar("请稍候", "正在统一法线：" + sm.Name, progress / total);
				progress++;

				Mesh mesh = sm.mesh;

				if (mOverwrite)
				{
					// 覆盖模式：直接重算原 Mesh 的法线
					RecalculateUnifiedNormals(mesh);
					string srcPath = AssetDatabase.GetAssetPath(mesh);
					if (!string.IsNullOrEmpty(srcPath))
					{
						EditorUtility.SetDirty(mesh);
					}
				}
				else
				{
					// 非覆盖模式：创建新 Mesh 副本
					Mesh newMesh = Object.Instantiate(mesh);
					newMesh.name = mesh.name + "_Unified";
					RecalculateUnifiedNormals(newMesh);

					string savePath = "Assets/" + mFilePath + "/";
					if (!Directory.Exists(Application.dataPath + "/" + mFilePath))
						Directory.CreateDirectory(Application.dataPath + "/" + mFilePath);
					string assetPath = savePath + newMesh.name + ".asset";

					Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
					if (existing != null)
					{
						EditorUtility.CopySerialized(newMesh, existing);
						Object.DestroyImmediate(newMesh);
						newMesh = existing;
					}
					else
					{
						AssetDatabase.CreateAsset(newMesh, assetPath);
					}

					// 赋值回关联对象
					if (sm.associatedObjects != null)
					{
						Undo.RecordObjects(sm.associatedObjects, "统一法线");
						foreach (var o in sm.associatedObjects)
						{
							if (o is SkinnedMeshRenderer smr)
								smr.sharedMesh = newMesh;
							else if (o is MeshFilter mf)
								mf.sharedMesh = newMesh;
							EditorUtility.SetDirty(o);
						}
					}
				}
			}

			AssetDatabase.SaveAssets();
			ShowNotification(new GUIContent($"已统一 {mMeshes.Count} 个网格的法线"));

			// 如果目标对象上有 LatticeModifier，触发 Rebuild 同步法线到变形 Mesh
			RefreshLatticeModifiers();
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
	}

	// 刷新场景中关联的 LatticeModifier，使其重建变形 Mesh 以同步最新法线
	private void RefreshLatticeModifiers()
	{
		foreach (Object obj in Selection.objects)
		{
			if (!(obj is GameObject go)) continue;
			// 检查自身及父级是否有 LatticeModifier
			var lattices = go.GetComponentsInParent<LatticeModifier>(true);
			foreach (var lat in lattices)
			{
				if (lat != null && lat.IsInitialized)
				{
					lat.RebuildDeformMeshes();
					lat.MarkDirty();
					lat.ApplyDeformation();
				}
			}
			// 也检查同级的 LatticeModifier（晶格可能挂在同级对象上）
			if (go.transform.parent != null)
			{
				var siblingLattices = go.transform.parent.GetComponentsInChildren<LatticeModifier>(true);
				foreach (var lat in siblingLattices)
				{
					if (lat != null && lat.IsInitialized)
					{
						lat.RebuildDeformMeshes();
						lat.MarkDirty();
						lat.ApplyDeformation();
					}
				}
			}
		}
	}

	// 重新计算统一法线：相同位置的顶点共享同一个平均法线
	private static void RecalculateUnifiedNormals(Mesh mesh)
	{
		Vector3[] vertices = mesh.vertices;
		Vector3[] normals = mesh.normals;

		if (normals == null || normals.Length != vertices.Length)
		{
			mesh.RecalculateNormals();
			normals = mesh.normals;
		}

		// 按顶点位置聚合法线
		var normalHash = new Dictionary<Vector3, Vector3>();
		for (int i = 0; i < vertices.Length; i++)
		{
			if (!normalHash.ContainsKey(vertices[i]))
				normalHash[vertices[i]] = normals[i];
			else
				normalHash[vertices[i]] = (normalHash[vertices[i]] + normals[i]).normalized;
		}

		// 写回统一法线
		for (int i = 0; i < vertices.Length; i++)
		{
			normals[i] = normalHash[vertices[i]];
		}

		mesh.normals = normals;
	}

	// 将描边材质赋予当前选中的模型对象
	private void ApplyOutlineMaterialToSelection()
	{
		if (mOutlineMaterial == null)
		{
			ShowNotification(new GUIContent("请先指定描边材质"));
			return;
		}

		int count = 0;
		foreach (Object obj in Selection.objects)
		{
			if (!(obj is GameObject go)) continue;
			var renderers = go.GetComponentsInChildren<Renderer>(true);
			foreach (var r in renderers)
			{
				Undo.RecordObject(r, "赋予描边材质");
				r.sharedMaterials = new Material[] { mOutlineMaterial };
				EditorUtility.SetDirty(r);
				count++;
			}
		}

		if (count > 0)
			ShowNotification(new GUIContent($"已为 {count} 个渲染器赋予描边材质"));
		else
			ShowNotification(new GUIContent("未找到可赋材质的渲染器"));
	}

	// 为选中对象创建 _OL 描边子模型：克隆网格渲染器对象，添加 _OL 后缀，并自动执行平滑网格处理
	private void CreateOutlineModels()
	{
		var createdObjects = new List<GameObject>();

		foreach (Object obj in Selection.objects)
		{
			if (!(obj is GameObject go)) continue;

			// 收集该对象下所有带网格的子对象（含自身）
			var renderers = go.GetComponentsInChildren<Transform>(true);
			foreach (Transform t in renderers)
			{
				// 跳过已经是 _OL 的对象
				if (t.name.EndsWith("_OL", StringComparison.OrdinalIgnoreCase))
					continue;

				// 检查是否有 MeshRenderer/SkinnedMeshRenderer
				Mesh mesh = null;
				MeshFilter mf = t.GetComponent<MeshFilter>();
				SkinnedMeshRenderer smr = t.GetComponent<SkinnedMeshRenderer>();
				if (mf == null && smr == null) continue;
				mesh = mf != null ? mf.sharedMesh : smr.sharedMesh;
				if (mesh == null) continue;

				// 检查同级是否已存在对应的 _OL 对象
				string olName = t.name + "_OL";
				Transform existingOL = null;
				if (t.parent != null)
					existingOL = t.parent.Find(olName);
				else
				{
					// 根对象情况，在同层级查找
					var scene = t.gameObject.scene;
					foreach (var root in scene.GetRootGameObjects())
					{
						if (root.name == olName)
						{
							existingOL = root.transform;
							break;
						}
					}
				}

				if (existingOL != null) continue; // 已存在则跳过

				// 克隆对象
				GameObject clone = Object.Instantiate(t.gameObject, t.parent);
				clone.name = olName;
				clone.transform.SetSiblingIndex(t.GetSiblingIndex() + 1);
				clone.transform.localPosition = t.localPosition;
				clone.transform.localRotation = t.localRotation;
				clone.transform.localScale = t.localScale;
				Undo.RegisterCreatedObjectUndo(clone, "创建描边模型 " + olName);

				// 移除克隆对象上不需要的子对象（只保留渲染相关组件）
				// 移除所有子物体，描边模型只需要自身网格
				for (int i = clone.transform.childCount - 1; i >= 0; i--)
					Object.DestroyImmediate(clone.transform.GetChild(i).gameObject);

				// 清理克隆对象上的脚本组件，只保留 Transform 和渲染相关组件
				var components = clone.GetComponents<Component>();
				for (int i = components.Length - 1; i >= 0; i--)
				{
					var comp = components[i];
					if (comp == null) continue;
					if (comp is Transform) continue;
					if (comp is MeshFilter) continue;
					if (comp is MeshRenderer) continue;
					if (comp is SkinnedMeshRenderer) continue;
					Object.DestroyImmediate(comp);
				}

				// 描边模型不需要投射阴影
				var renderer = clone.GetComponent<Renderer>();
				if (renderer != null)
				{
					renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					// 赋予描边材质球
					if (mOutlineMaterial != null)
						renderer.sharedMaterials = new Material[] { mOutlineMaterial };
				}

				// 对克隆的 Mesh 执行平滑网格处理
				Mesh cloneMesh = null;
				MeshFilter cloneMF = clone.GetComponent<MeshFilter>();
				SkinnedMeshRenderer cloneSMR = clone.GetComponent<SkinnedMeshRenderer>();
				if (cloneMF != null)
					cloneMesh = cloneMF.sharedMesh;
				else if (cloneSMR != null)
					cloneMesh = cloneSMR.sharedMesh;

				if (cloneMesh != null)
				{
					// 创建新 Mesh 副本并执行平滑法线
					Mesh smoothed = CreateSmoothedNormalsMesh(cloneMesh, saveChannel, true);
					if (smoothed != null)
					{
						smoothed.name = cloneMesh.name + "_OL";

						// 保存为资产
						string savePath = "Assets/" + mFilePath + "/";
						if (!Directory.Exists(Application.dataPath + "/" + mFilePath))
							Directory.CreateDirectory(Application.dataPath + "/" + mFilePath);
						string assetPath = savePath + smoothed.name + ".asset";

						// 检查是否已存在同名资产
						Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
						if (existing != null)
						{
							EditorUtility.CopySerialized(smoothed, existing);
							smoothed = existing;
						}
						else
						{
							AssetDatabase.CreateAsset(smoothed, assetPath);
						}

						// 赋值回克隆对象
						if (cloneMF != null)
							cloneMF.sharedMesh = smoothed;
						else if (cloneSMR != null)
							cloneSMR.sharedMesh = smoothed;
					}
				}

				createdObjects.Add(clone);
			}
		}

		if (createdObjects.Count > 0)
		{
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Selection.objects = createdObjects.ToArray();
			ShowNotification(new GUIContent($"已创建 {createdObjects.Count} 个描边模型"));
		}
		else
		{
			ShowNotification(new GUIContent("未创建新描边模型（可能已存在 _OL 对象）"));
		}
	}

	// 在当前选中对象的子层级或同预设根下，查找名称带 "_OL" 后缀的对象并选中
	private void SelectOutlineObjects()
	{
		var result = new List<GameObject>();

		foreach (Object obj in Selection.objects)
		{
			if (!(obj is GameObject go)) continue;

			// 找到预设根（PrefabStage 或场景中的 Prefab 实例根）
			GameObject searchRoot = GetPrefabRoot(go);

			// 在 searchRoot 的所有子对象（含自身）中查找 _OL 后缀
			foreach (Transform t in searchRoot.GetComponentsInChildren<Transform>(true))
			{
				if (t.name.EndsWith("_OL", System.StringComparison.OrdinalIgnoreCase)
				    && !result.Contains(t.gameObject))
				{
					result.Add(t.gameObject);
				}
			}
		}

		if (result.Count == 0)
		{
			ShowNotification(new GUIContent("未找到带 _OL 后缀的对象"));
			return;
		}

		Selection.objects = result.ToArray();
		// 立即刷新 Mesh 列表，确保选中 _OL 对象后操作按钮保持显示
		mMeshes = GetSelectedMeshes();
		Repaint();
		ShowNotification(new GUIContent($"已选中 {result.Count} 个描边对象"));
	}

	// 获取 GameObject 所在预设实例的根节点；若不在预设中则返回自身
	private static GameObject GetPrefabRoot(GameObject go)
	{
		// 场景中的预设实例：向上找到最顶层的预设根
		GameObject prefabRoot = UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(go);
		if (prefabRoot != null) return prefabRoot;

		// 不在预设实例中：向上找到场景层级根（无父节点的对象）
		Transform t = go.transform;
		while (t.parent != null) t = t.parent;
		return t.gameObject;
	}

	// 选择选中对象的父层级对象
	// 若对象是预设实例内部节点，先找到预设实例根，再选根的父对象
	private void SelectParentObjects()
	{
		var sourceObjects = new HashSet<GameObject>();

		// 检查是否在 Prefab 编辑模式（Prefab Stage�?
		var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

		foreach (Object obj in Selection.objects)
		{
			if (!(obj is GameObject go)) continue;

			if (prefabStage != null)
			{
				// Prefab Stage 模式：直接使用选中对象，scene 检查无效
				sourceObjects.Add(go);
			}
			else if (!string.IsNullOrEmpty(go.scene.name))
			{
				// 普通场景中的 GameObject
				sourceObjects.Add(go);
			}
			else
			{
				// Project 里的 prefab 资产 �?找场景中所有该 prefab 的实�?
				string prefabAssetPath = AssetDatabase.GetAssetPath(go);
				if (string.IsNullOrEmpty(prefabAssetPath)) continue;

				foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
				{
					foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
					{
						GameObject outerRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
						if (outerRoot == null) continue;
						string instanceSourcePath = AssetDatabase.GetAssetPath(
							PrefabUtility.GetCorrespondingObjectFromOriginalSource(outerRoot));
						if (instanceSourcePath == prefabAssetPath)
						{
							sourceObjects.Add(outerRoot);
							break;
						}
					}
				}
			}
		}

		// 路径2：从 mMeshes 的关联组件反查
		if (sourceObjects.Count == 0 && mMeshes != null)
		{
			foreach (var sm in mMeshes.Values)
			{
				if (sm.associatedObjects == null) continue;
				foreach (Object assoc in sm.associatedObjects)
				{
					GameObject go = null;
					if (assoc is Component c) go = c.gameObject;
					else if (assoc is GameObject g) go = g;
					if (go != null)
						sourceObjects.Add(go);
				}
			}
		}

		var result = new HashSet<GameObject>();
		foreach (GameObject go in sourceObjects)
		{
			// 直接取父对象，不跳 prefab 根
			if (go.transform.parent != null)
				result.Add(go.transform.parent.gameObject);
		}

		if (result.Count > 0)
			Selection.objects = new List<GameObject>(result).ToArray();
		else
			Notify("所选对象已是根节点，无父对象");
	}

	// 立即显示通知，通过反射清空旧通知队列，确保新消息优先显示
	private void Notify(string message, double duration = 4.0)
	{
		// 反射清空 m_Notifications，避免旧消息阻塞新消息
		var field = typeof(EditorWindow).GetField("m_Notifications",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		if (field != null)
		{
			var list = field.GetValue(this) as System.Collections.IList;
			list?.Clear();
		}
		ShowNotification(new GUIContent(message), duration);
	}

	private void GUI_SelectSaveChannel()
	{
		float savedLabelWidth = EditorGUIUtility.labelWidth;
		EditorGUIUtility.labelWidth = 50;

		saveChannel = (SmoothedNormalsChannel)EditorGUILayout.EnumPopup("存储通道", saveChannel);
		if (saveChannel == SmoothedNormalsChannel.UV1 ||
			saveChannel == SmoothedNormalsChannel.UV2 ||
			saveChannel == SmoothedNormalsChannel.UV3 ||
			saveChannel == SmoothedNormalsChannel.UV4)
		{
			saveInTangentSpace = EditorGUILayout.Toggle("存储到切线空间", saveInTangentSpace);
		}

		EditorGUIUtility.labelWidth = savedLabelWidth;
	}

	private void GUI_SeletSavePath() 
	{
		float savedLabelWidth = EditorGUIUtility.labelWidth;
		EditorGUIUtility.labelWidth = 40;

		EditorGUILayout.BeginHorizontal();
		GUI_SelectSaveChannel();
		EditorGUILayout.LabelField("后缀名", SmoothNormalSuffix);
		if (GUILayout.Button(new GUIContent("获取","先选择Mesh对象，获取选择对象所在路径"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
		{
			string path = GetSelectedPrefabPath();
			if (!string.IsNullOrEmpty(path))
			{
				mFilePath = path;
				Repaint();
			}
			else
			{
				ShowNotification(new GUIContent("未找到所选对象的预设路径"));
			}
		}
		if (GUILayout.Button(new GUIContent("指定","指定自定义输出路径"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
		{
			string outputPath = GUIHelper.SelectPath("选择平滑网格的输出目录", mFilePath);
			if (!string.IsNullOrEmpty(outputPath))
			{
				mFilePath = outputPath;
			}
		}
		EditorGUILayout.EndHorizontal();
		mFilePath = EditorGUILayout.TextField("Assets 路径", mFilePath);
		EditorGUIUtility.labelWidth = savedLabelWidth;
	}

	// 获取当前选中对象所属预设资产的目录路径（相对 Assets/ 的子路径）
	private string GetSelectedPrefabPath()
	{
		foreach (Object obj in Selection.objects)
		{
			if (!(obj is GameObject go)) continue;

			// 优先取预设实例对应的资产路径
			GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
			string assetPath = prefabAsset != null
				? AssetDatabase.GetAssetPath(prefabAsset)
				: null;

			// 若不是预设实例，尝试直接取 Project 中选中的预设资产路径
			if (string.IsNullOrEmpty(assetPath))
				assetPath = AssetDatabase.GetAssetPath(go);

			if (string.IsNullOrEmpty(assetPath)) continue;

			// 取目录部分，去掉 "Assets/" 前缀返回相对路径
			string dir = System.IO.Path.GetDirectoryName(assetPath)
			                          ?.Replace('\\', '/');
			if (string.IsNullOrEmpty(dir)) continue;

			// 去掉 "Assets/" 前缀
			if (dir.StartsWith("Assets/"))
				dir = dir.Substring("Assets/".Length);
			else if (dir == "Assets")
				dir = "";

			return dir;
		}
		return null;
	}

    #endregion

    #region Mesh
    private class SelectedMesh
	{
		public Mesh mesh;

		// 
		public bool isAssets;

		private List<Object> _associatedObjects = new List<Object>();
		public Object[] associatedObjects
		{
			get
			{
				if (_associatedObjects.Count == 0) return null;
				return _associatedObjects.ToArray();
			}
		}

		public string Name { get { return mesh.name; }  }

		public SelectedMesh(Mesh _mesh, bool _isAssets, Object _assoObj = null)
		{
			mesh = _mesh;

			isAssets = _isAssets;

			AddAssociatedObject(_assoObj);
		}

		public void AddAssociatedObject(Object _assoObj)
		{
			if (_assoObj != null)
			{
				_associatedObjects.Add(_assoObj);
			}
		}
	}

	private Dictionary<Mesh, SelectedMesh> mMeshes;

	private SmoothedNormalsChannel saveChannel = SmoothedNormalsChannel.Tangent;

	private bool saveInTangentSpace = false;

	private Dictionary<Mesh, SelectedMesh> GetSelectedMeshes()
	{
		Dictionary<Mesh, SelectedMesh> meshDict = new Dictionary<Mesh, SelectedMesh>();

		foreach (Object o in Selection.objects)
		{
			bool isProjectAsset = !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o));

			//Assets from Project
			if (o is Mesh && !meshDict.ContainsKey(o as Mesh))
			{
				if ((o as Mesh) != null)
				{
					SelectedMesh sm = GetMeshToAdd(o as Mesh, isProjectAsset);
					if (sm != null)
						meshDict.Add(o as Mesh, sm);
				}
			}
			else if (o is GameObject && isProjectAsset)
			{
				string path = AssetDatabase.GetAssetPath(o);
				Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
				foreach (Object asset in allAssets)
				{
					if (asset is Mesh && !meshDict.ContainsKey(asset as Mesh))
					{
						if ((asset as Mesh) != null)
						{
							var sm = GetMeshToAdd(asset as Mesh, isProjectAsset);
							if (sm.mesh != null)
								meshDict.Add(asset as Mesh, sm);
						}
					}
				}
			}
			//Assets from Hierarchy
			else if (o is GameObject && !isProjectAsset)
			{
				SkinnedMeshRenderer[] skinnedMeshRenderers = (o as GameObject).GetComponentsInChildren<SkinnedMeshRenderer>();
				foreach (SkinnedMeshRenderer renderer in skinnedMeshRenderers)
				{
					if (renderer.sharedMesh != null)
					{
						if (meshDict.ContainsKey(renderer.sharedMesh))
						{
							var sm = meshDict[renderer.sharedMesh];
							sm.AddAssociatedObject(renderer);
						}
						else
						{
							if (renderer.sharedMesh.name.Contains(SmoothNormalSuffix))
							{
								meshDict.Add(renderer.sharedMesh, new SelectedMesh(renderer.sharedMesh, false));
							}
							else
							{
								if (renderer.sharedMesh != null)
								{
									var sm = GetMeshToAdd(renderer.sharedMesh, true, renderer);
									if (sm.mesh != null)
										meshDict.Add(renderer.sharedMesh, sm);
								}
							}
						}
					}
				}

				MeshFilter[] meshFilters = (o as GameObject).GetComponentsInChildren<MeshFilter>();
				foreach (MeshFilter filter in meshFilters)
				{
					if (filter.sharedMesh != null)
					{
						if (meshDict.ContainsKey(filter.sharedMesh))
						{
							var sm = meshDict[filter.sharedMesh];
							sm.AddAssociatedObject(filter);
						}
						else
						{
							if (filter.sharedMesh.name.Contains(SmoothNormalSuffix))
							{
								meshDict.Add(filter.sharedMesh, new SelectedMesh(filter.sharedMesh, false));
							}
							else
							{
								if (filter.sharedMesh != null)
								{
									var sm = GetMeshToAdd(filter.sharedMesh, true, filter);
									if (sm.mesh != null)
										meshDict.Add(filter.sharedMesh, sm);
								}
							}
						}
					}
				}
			}
		}

		return meshDict;
	}

	private SelectedMesh GetMeshToAdd(Mesh mesh, bool isProjectAsset, Object _assoObj = null)
	{
		var meshPath = AssetDatabase.GetAssetPath(mesh);
		var meshAsset = AssetDatabase.LoadAssetAtPath(meshPath, typeof(Mesh)) as Mesh;
		//If null, it can be a built-in Unity mesh
		if (meshAsset == null)
		{
			return new SelectedMesh(mesh, isProjectAsset, _assoObj);
		}
		var meshName = mesh.name;
		if (!AssetDatabase.IsMainAsset(meshAsset))
		{
			var main = AssetDatabase.LoadMainAssetAtPath(meshPath);
			meshName = main.name + " - " + meshName + "_" + mesh.GetInstanceID();
		}

		var sm = new SelectedMesh(mesh, isProjectAsset, _assoObj);
		return sm;
	}

	private Mesh CreateSmoothedMeshAsset(SelectedMesh originalMesh)
	{
		// ── 覆盖模式：直接在原 Mesh 上写入平滑法线数据，不创建新资产 ──
		if (mOverwrite)
		{
			Mesh srcMesh = originalMesh.mesh;
			string srcPath = AssetDatabase.GetAssetPath(srcMesh);

			// 直接修改原 Mesh（createMesh=false）
			Mesh result = CreateSmoothedNormalsMesh(srcMesh, saveChannel, false);
			if (result == null)
			{
				ShowNotification(new GUIContent("无法生成网格：" + originalMesh.Name));
				return null;
			}

			// 如果原 Mesh 是项目资产，标记脏并保存
			if (!string.IsNullOrEmpty(srcPath))
			{
				EditorUtility.SetDirty(srcMesh);
				AssetDatabase.SaveAssets();
			}

			return result;
		}

		// ── 非覆盖模式：创建新的 Mesh 资产 ──
		string savePath = Application.dataPath + "/" + mFilePath;
		if (!Directory.Exists(savePath)) 
			Directory.CreateDirectory(savePath);
		string assetPath = "Assets/" + mFilePath + "/";
		string originalMeshName = originalMesh.Name;
		string newAssetName = originalMeshName + SmoothNormalSuffix + ".asset";
		if (originalMeshName.Contains(SmoothNormalSuffix))
		{
			newAssetName = originalMeshName + ".asset";
		}
		assetPath += newAssetName;
		Mesh existingAsset = AssetDatabase.LoadAssetAtPath(assetPath, typeof(Mesh)) as Mesh;
		bool assetExists = (existingAsset != null) && originalMesh.isAssets;
		if (assetExists)
		{
			originalMesh.mesh = existingAsset;
		}

		Mesh newMesh = CreateSmoothedNormalsMesh(originalMesh.mesh, saveChannel, 
			!originalMesh.isAssets || !(originalMesh.isAssets && assetExists));

		if (newMesh == null)
		{
			ShowNotification(new GUIContent("无法生成网格：" + originalMesh.Name));
		}
		else 
		{
			if (originalMesh.associatedObjects != null)
			{
				Undo.RecordObjects(originalMesh.associatedObjects, "将平滑网格赋值到选中对象");

				foreach (var o in originalMesh.associatedObjects)
				{
					if (o is SkinnedMeshRenderer)
					{
						(o as SkinnedMeshRenderer).sharedMesh = newMesh;
					}
					else if (o is MeshFilter)
					{
						(o as MeshFilter).sharedMesh = newMesh;
					}
					else
					{
						Debug.LogWarning("未识别的关联对象：" + o + "，类型：" + o.GetType());
					}
					EditorUtility.SetDirty(o);
				}
			}

			if (!assetExists)
				AssetDatabase.CreateAsset(newMesh, assetPath);
		}

		return newMesh;
	}

	public Mesh CreateSmoothedNormalsMesh(Mesh originMesh, SmoothedNormalsChannel saveChannel, bool createMesh)
	{
		if (originMesh == null)
		{
			Debug.LogWarning("传入的原始网格为空，无法生成平滑法线版本。");
			return null;
		}

		//Create new mesh
		Mesh newMesh = createMesh ? new Mesh() : originMesh;
		if (createMesh)
		{ 
			newMesh.vertices = originMesh.vertices;
			newMesh.normals = originMesh.normals;
			newMesh.tangents = originMesh.tangents;
			newMesh.uv = originMesh.uv;
			newMesh.uv2 = originMesh.uv2;
			newMesh.uv3 = originMesh.uv3;
			newMesh.uv4 = originMesh.uv4;
			newMesh.colors32 = originMesh.colors32;
			newMesh.triangles = originMesh.triangles;
			newMesh.bindposes = originMesh.bindposes;
			newMesh.boneWeights = originMesh.boneWeights;

			if (originMesh.blendShapeCount > 0)
				CopyBlendShapes(originMesh, newMesh);

			newMesh.subMeshCount = originMesh.subMeshCount;
			if (newMesh.subMeshCount > 1)
				for (var i = 0; i < newMesh.subMeshCount; i++)
					newMesh.SetTriangles(originMesh.GetTriangles(i), i);
		}

		//Calculate smoothed normals
		var averageNormalsHash = new Dictionary<Vector3, Vector3>();
		for (var i = 0; i < newMesh.vertexCount; i++)
		{
			if (!averageNormalsHash.ContainsKey(newMesh.vertices[i]))
				averageNormalsHash.Add(newMesh.vertices[i], newMesh.normals[i]);
			else
				averageNormalsHash[newMesh.vertices[i]] = 
					(averageNormalsHash[newMesh.vertices[i]] + newMesh.normals[i]).normalized;
		}

		//Convert to Array
		var averageNormals = new Vector3[newMesh.vertexCount];
		for (var i = 0; i < newMesh.vertexCount; i++)
		{
			averageNormals[i] = averageNormalsHash[newMesh.vertices[i]];
		}

		// Store in Vertex Colors
		if (saveChannel == SmoothedNormalsChannel.VertexColor)
		{
			var colors = new Color[newMesh.vertexCount];
			for (var i = 0; i < newMesh.vertexCount; i++)
			{
				var r = (averageNormals[i].x * 0.5f) + 0.5f;
				var g = (averageNormals[i].y * 0.5f) + 0.5f;
				var b = (averageNormals[i].z * 0.5f) + 0.5f;

				colors[i] = new Color(r, g, b, 1);
			}
			newMesh.colors = colors;
		}

		// Store in Tangents
		if (saveChannel == SmoothedNormalsChannel.Tangent)
		{
			var tangents = new Vector4[newMesh.vertexCount];
			for (var i = 0; i < newMesh.vertexCount; i++)
			{
				tangents[i] = new Vector4(averageNormals[i].x, averageNormals[i].y, averageNormals[i].z, 0f);
			}
			newMesh.tangents = tangents;
		}

		// Store in UVs
		if (saveChannel == SmoothedNormalsChannel.UV1 || 
			saveChannel == SmoothedNormalsChannel.UV2 || 
			saveChannel == SmoothedNormalsChannel.UV3 || 
			saveChannel == SmoothedNormalsChannel.UV4)
		{
			int uvIndex = -1;
			switch (saveChannel)
			{
				case SmoothedNormalsChannel.UV1: uvIndex = 1; break;
				case SmoothedNormalsChannel.UV2: uvIndex = 2; break;
				case SmoothedNormalsChannel.UV3: uvIndex = 3; break;
				case SmoothedNormalsChannel.UV4: uvIndex = 4; break;
				default: Debug.LogError("无效的平滑法线 UV 通道：" + saveChannel); break;
			}
			if (saveInTangentSpace)
			{
				var uv = new Vector2[newMesh.vertexCount];
				var tangents = newMesh.tangents;
				var normals = newMesh.normals;
				var bitangent = Vector3.one;
				for (var j = 0; j < newMesh.vertexCount; j++)
				{
                    bitangent = (Vector3.Cross(normals[j], tangents[j]) * tangents[j].w).normalized;
                    var bakeNormal = Vector3.Normalize(new Vector3(
                            Vector3.Dot(tangents[j], averageNormals[j]),
                            Vector3.Dot(bitangent, averageNormals[j]),
                            Vector3.Dot(normals[j], averageNormals[j])));
                    uv[j] = new Vector2(bakeNormal.x * 0.5f + 0.5f, bakeNormal.y * 0.5f + 0.5f);
                }
				newMesh.SetUVs(uvIndex, uv);
			}
			else 
			{
				newMesh.SetUVs(uvIndex, new List<Vector3>(averageNormals));
			}
		}

		return newMesh;
	}

	private static void CopyBlendShapes(Mesh originalMesh, Mesh newMesh)
	{
		for (int i = 0; i < originalMesh.blendShapeCount; i++)
		{
			string shapeName = originalMesh.GetBlendShapeName(i);
			int frameCount = originalMesh.GetBlendShapeFrameCount(i);
			for (var j = 0; j < frameCount; j++)
			{
				Vector3[] dv = new Vector3[originalMesh.vertexCount];
				Vector3[] dn = new Vector3[originalMesh.vertexCount];
				Vector3[] dt = new Vector3[originalMesh.vertexCount];

				float frameWeight = originalMesh.GetBlendShapeFrameWeight(i, j);
				originalMesh.GetBlendShapeFrameVertices(i, j, dv, dn, dt);
				newMesh.AddBlendShapeFrame(shapeName, frameWeight, dv, dn, dt);
			}
		}
	}
	#endregion

	public class GUIHelper 
	{
		public static GUIStyle _LineStyle;
		public static GUIStyle LineStyle
		{
			get
			{
				if (_LineStyle == null)
				{
					_LineStyle = new GUIStyle();
					_LineStyle.normal.background = EditorGUIUtility.whiteTexture;
					_LineStyle.stretchWidth = true;
				}

				return _LineStyle;
			}
		}

		public static void GUILine(float height = 2f)
		{
			GUILine(Color.black, height);
		}

		public static void GUILine(Color color, float height = 2f)
		{
			var position = GUILayoutUtility.GetRect(0f, float.MaxValue, height, height, LineStyle);

			if (Event.current.type == EventType.Repaint)
			{
				var orgColor = GUI.color;
				GUI.color = orgColor * color;
				LineStyle.Draw(position, false, false, false, false);
				GUI.color = orgColor;
			}
		}

		public static void GUILine(Rect position, Color color, float height = 2f)
		{
			if (Event.current.type == EventType.Repaint)
			{
				var orgColor = GUI.color;
				GUI.color = orgColor * color;
				LineStyle.Draw(position, false, false, false, false);
				GUI.color = orgColor;
			}
		}

		public static void SeparatorSimple()
		{
			var color = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.65f, 0.65f, 0.65f);
			GUILine(color, 1);
			GUILayout.Space(1);
		}

		public static string SelectPath(string label, string startDir)
		{
			string output = null;

			if (startDir.Length > 0 && startDir[0] != '/')
			{
				startDir = "/" + startDir;
			}

			string startPath = Application.dataPath.Replace(@"\", "/") + startDir;
			if (!Directory.Exists(startPath))
			{
				startPath = Application.dataPath;
			}

			var path = EditorUtility.OpenFolderPanel(label, startPath, "");
			if (!string.IsNullOrEmpty(path))
			{
				var validPath = SystemToUnityPath(ref path);
				if (validPath)
				{
					if (path == "Assets")
						output = "/";
					else
						output = path.Substring("Assets/".Length);
				}
				else
				{
					EditorApplication.Beep();
					EditorUtility.DisplayDialog("路径无效",
						"所选路径无效。\n\n" +
						"请选择项目 \"Assets\" 文件夹内的目录！", "确定");
				}
			}

			return output;
		}

		public static bool SystemToUnityPath(ref string sysPath)
		{
			if (sysPath.IndexOf(Application.dataPath) < 0)
			{
				return false;
			}

			sysPath = string.Format("Assets{0}", sysPath.Replace(Application.dataPath, ""));
			return true;
		}
	}
}

using System.Collections;
using UnityEngine;
using UnityEditor;

namespace VicTools
{
    // 样式类获取方法：var style = EditorStyle.Get;
	public class EditorStyle
	{
		private static EditorStyle style = null;

		public GUIStyle area;
        public GUIStyle areaPadded;

		// 保留字段以保持向后兼容性，但实际使用属性
		public GUIStyle normalButton => NormalButton;
		public GUIStyle normalButton_Y => NormalButton_Y;
		public GUIStyle normalButton_R => NormalButton_R;
		public GUIStyle normalButton_G => NormalButton_G;
		public GUIStyle normalButton_B => NormalButton_B;
		public GUIStyle menuButton => MenuButton;
		public GUIStyle menuButtonSelected => MenuButtonSelected;
		public GUIStyle smallSquareButton;

		public GUIStyle heading => Heading;
		public GUIStyle subheading => Subheading;
		public GUIStyle subheading2 => Subheading2;

		public GUIStyle boldLabelNoStretch => BoldLabelNoStretch;

		public GUIStyle normalfont => Normalfont;
		public GUIStyle normalfont_Hui => Normalfont_Hui;
		public GUIStyle normalfont_Hui_Wrap => Normalfont_Hui_Wrap;
		public GUIStyle normalfont_Hui_Cen => Normalfont_Hui_Cen;
		public GUIStyle link;

		public GUIStyle toggle => Toggle;

		// 私有字段用于属性实现
		private GUIStyle _normalButton;
		private GUIStyle _normalButton_Y;
		private GUIStyle _normalButton_R;
		private GUIStyle _normalButton_G;
		private GUIStyle _normalButton_B;
		private GUIStyle _menuButton;
		private GUIStyle _menuButtonSelected;
		private GUIStyle _heading;
		private GUIStyle _subheading;
		private GUIStyle _subheading2;
		private GUIStyle _boldLabelNoStretch;
		private GUIStyle _normalfont;
		private GUIStyle _normalfont_Hui;
		private GUIStyle _normalfont_Hui_Wrap;
		private GUIStyle _normalfont_Hui_Cen;
		private GUIStyle _toggle;

        // public Texture2D saveIconSelected;
        // public Texture2D saveIconUnselected;

		
		/// 获取 EditorStyle 单例实例的静态属性
		/// 使用懒加载模式：只有在第一次访问时才创建实例
		/// 如果实例为 null，则立即创建实例，确保不会返回null
		public static EditorStyle Get 
		{ 
			get
			{ 
				if (style == null) 
				{
					// 立即创建实例，而不是依赖延迟调用
					style = new EditorStyle();
				}
				return style; 
			} 
		}

		/// Unity 编辑器初始化时自动调用的方法
		/// [InitializeOnLoadMethod] 特性确保此方法在以下时机执行：
		/// 1. Unity 编辑器启动时
		/// 2. 脚本重新编译后
		/// 3. 项目加载时
		/// 
		/// 解决原始问题：
		/// - 原始代码在编辑器重启后，menuButtonSelected.normal.background 背景色失效
		/// - 原因：静态字段在编辑器重启后被重置，MakeTex 创建的纹理失效
		/// - 解决方案：通过此特性确保在正确的时机重新创建样式实例
		[InitializeOnLoadMethod]
		private static void InitializeStyles()
		{
			// 延迟初始化，确保EditorStyles已经可用
			EditorApplication.delayCall += () =>
			{
				// 创建新的 EditorStyle 实例
				// 这会重新执行构造函数中的所有样式初始化逻辑
				// 包括重新创建 MakeTex 方法生成的所有纹理
				style = new EditorStyle();
			};
		}

		public EditorStyle()
		{
			// 带填充的区域，在布局中使用：EditorGUILayout.BeginVertical(style.area);
			area = new GUIStyle();
			area.padding = new RectOffset(10, 10, 10, 10);  //RectOffset(int left, int right, int top, int bottom)
            area.wordWrap = true;   //自动换行

            // An area with more padding.
            areaPadded = new GUIStyle();
            areaPadded.padding = new RectOffset(20, 20, 20, 20);
            areaPadded.wordWrap = true;

			// 初始化不依赖EditorStyles的样式
			link = new GUIStyle();
			link.fontSize = 16;
			if(EditorGUIUtility.isProSkin)
				link.normal.textColor = new Color (0.2f, 0.6f, 0.7f);
			else
				link.normal.textColor = new Color (0.11f, 0.11f, 0.8f);

            // saveIconSelected = AssetDatabase.LoadAssetAtPath<Texture2D>("Editor/es3Logo16x16.png");
            // saveIconUnselected = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/VicTools/VT16x16-bw.png");
        }

		// 使用属性而不是字段，确保在访问时EditorStyles已经可用
		public GUIStyle NormalButton
		{
			get
			{
				if (_normalButton == null)
				{
					_normalButton = new GUIStyle(EditorStyles.miniButton != null ? EditorStyles.miniButton : (EditorStyles.label != null ? EditorStyles.label : new GUIStyle()));
					_normalButton.fontSize = 14;
					_normalButton.fixedHeight = 24;
					_normalButton.hover.textColor = new Color(0.99f, 0.99f, 0.99f);
				}
				return _normalButton;
			}
		}

		public GUIStyle NormalButton_Y
		{
			get
			{
				if (_normalButton_Y == null)
				{
					_normalButton_Y = new GUIStyle(NormalButton);
					_normalButton_Y.normal.textColor = new Color(0.88f, 0.78f, 0.2f);
					_normalButton_Y.hover.textColor = new Color(0.98f, 0.88f, 0.12f);
				}
				return _normalButton_Y;
			}
		}

		public GUIStyle NormalButton_R
		{
			get
			{
				if (_normalButton_R == null)
				{
					_normalButton_R = new GUIStyle(NormalButton);
					_normalButton_R.normal.textColor = new Color(0.88f, 0.3f, 0.2f);
					_normalButton_R.hover.textColor = new Color(0.98f, 0.33f, 0.1f);
					_normalButton_R.fontSize = 15;
					_normalButton_R.fontStyle = FontStyle.Bold;
				}
				return _normalButton_R;
			}
		}

		public GUIStyle NormalButton_G
		{
			get
			{
				if (_normalButton_G == null)
				{
					_normalButton_G = new GUIStyle(NormalButton);
					_normalButton_G.normal.textColor = new Color(0.3f, 0.88f, 0.2f);
					_normalButton_G.hover.textColor = new Color(0.33f, 0.98f, 0.1f);
				}
				return _normalButton_G;
			}
		}

		public GUIStyle NormalButton_B
		{
			get
			{
				if (_normalButton_B == null)
				{
					_normalButton_B = new GUIStyle(NormalButton);
					_normalButton_B.normal.textColor = new Color(0.4f, 0.63f, 0.98f);
					_normalButton_B.hover.textColor = new Color(0.01f, 0.24f, 0.69f);
				}
				return _normalButton_B;
			}
		}

		public GUIStyle MenuButton
		{
			get
			{
				if (_menuButton == null)
				{
					_menuButton = new GUIStyle(EditorStyles.toolbarButton != null ? EditorStyles.toolbarButton : (EditorStyles.label != null ? EditorStyles.label : new GUIStyle()));
					_menuButton.fontStyle = FontStyle.Normal;
					_menuButton.fontSize = 15;
					_menuButton.fixedHeight = 24;
				}
				return _menuButton;
			}
		}

		public GUIStyle MenuButtonSelected
		{
			get
			{
				if (_menuButtonSelected == null)
				{
					_menuButtonSelected = new GUIStyle(MenuButton);
					_menuButtonSelected.fontStyle = FontStyle.Bold;
					_menuButtonSelected.normal.textColor = new Color (0.92f, 0.8f, 0.27f);
					_menuButtonSelected.normal.background = CreatePersistentTexture(new Color(0.63f, 0.53f, 0.3f, 0.3f));
					_menuButtonSelected.hover.textColor = new Color (1.0f, 0.9f, 0.4f);
					_menuButtonSelected.hover.background = CreatePersistentTexture(new Color(0.4f, 0.38f, 0.27f));
				}
				return _menuButtonSelected;
			}
		}

		public GUIStyle Heading
		{
			get
			{
				if (_heading == null)
				{
					_heading = new GUIStyle(EditorStyles.label != null ? EditorStyles.label : new GUIStyle());
					_heading.fontStyle = FontStyle.Bold;
					_heading.fontSize = 22;
				}
				return _heading;
			}
		}

		public GUIStyle Subheading
		{
			get
			{
				if (_subheading == null)
				{
					_subheading = new GUIStyle(Heading);
					_subheading.fontSize = 18;
				}
				return _subheading;
			}
		}

		public GUIStyle Subheading2
		{
			get
			{
				if (_subheading2 == null)
				{
					_subheading2 = new GUIStyle(Heading);
					_subheading2.fontSize = 14;
				}
				return _subheading2;
			}
		}

		public GUIStyle Normalfont
		{
			get
			{
				if (_normalfont == null)
				{
					_normalfont = new GUIStyle(EditorStyles.label != null ? EditorStyles.label : new GUIStyle());
					_normalfont.fontSize = 14;
				}
				return _normalfont;
			}
		}

		public GUIStyle Normalfont_Hui
		{
			get
			{
				if (_normalfont_Hui == null)
				{
					_normalfont_Hui = new GUIStyle(Normalfont);
					_normalfont_Hui.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
				}
				return _normalfont_Hui;
			}
		}

		public GUIStyle Normalfont_Hui_Wrap
		{
			get
			{
				if (_normalfont_Hui_Wrap == null)
				{
					_normalfont_Hui_Wrap = new GUIStyle(Normalfont_Hui);
					_normalfont_Hui_Wrap.wordWrap = true;
				}
				return _normalfont_Hui_Wrap;
			}
		}

		public GUIStyle Normalfont_Hui_Cen
		{
			get
			{
				if (_normalfont_Hui_Cen == null)
				{
					_normalfont_Hui_Cen = new GUIStyle(Normalfont_Hui);
					_normalfont_Hui_Cen.wordWrap = true;
					_normalfont_Hui_Cen.alignment = TextAnchor.MiddleCenter;
				}
				return _normalfont_Hui_Cen;
			}
		}

		public GUIStyle BoldLabelNoStretch
		{
			get
			{
				if (_boldLabelNoStretch == null)
				{
					_boldLabelNoStretch = new GUIStyle(EditorStyles.label != null ? EditorStyles.label : new GUIStyle());
					_boldLabelNoStretch.stretchWidth = false;
					_boldLabelNoStretch.fontStyle = FontStyle.Bold;
				}
				return _boldLabelNoStretch;
			}
		}

		public GUIStyle Toggle
		{
			get
			{
				if (_toggle == null)
				{
					_toggle = new GUIStyle(EditorStyles.toggle != null ? EditorStyles.toggle : new GUIStyle());
					_toggle.stretchWidth = false;
				}
				return _toggle;
			}
		}

        // 创建纯色纹理的方法
        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            // 正确设置HideFlags.HideAndDontSave，避免Unity编辑器试图持久化临时纹理
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }

		/// 创建临时纹理的方法
		/// 修复Unity断言失败问题：正确设置HideFlags.HideAndDontSave
		/// 确保临时纹理不会被Unity编辑器持久化

		/// <param name="color">纹理颜色</param>
		/// <param name="width">纹理宽度（默认2）</param>
		/// <param name="height">纹理高度（默认2）</param>
		/// <returns>Texture2D对象</returns>
		private Texture2D CreatePersistentTexture(Color color, int width = 2, int height = 2)
		{
			Texture2D texture = MakeTex(width, height, color);
			// 正确设置HideFlags.HideAndDontSave，避免Unity编辑器试图持久化临时纹理
			// 这解决了断言失败：'!(o->TestHideFlag(Object::kDontSaveInEditor) && (options & kAllowDontSaveObjectsToBePersistent) == 0)'
			texture.hideFlags = HideFlags.HideAndDontSave;
			return texture;
		}
	}
}

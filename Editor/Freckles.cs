#region

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

#endregion

namespace Freckles
{
	public class Freckles : EditorWindow
	{
		#region Publics

		#region Shader parameters

		public Vector4 freckleBounds = new Vector4(0, 1, 0, 1);
		public bool freckleMask = true;
		public float freckleScale = 400;
		[Range(0f, 1f)] public float freckleSize = 0.3f;
		[Range(-10f, 10f)] public float freckleRandomness = 1;
		[Range(0f, 1f)] public float freckleAmount = 0.1f;
		[Range(0f, 2f)] public float freckleRoundness = 1;

		#endregion

		public float threadGroupSize = 8;
		public float dispatchCooldown = 0.5f;

		public bool debugLog = true;

		public Material PreviewMaterial
		{
			get => _previewMaterial;
			set
			{
				if (_previewMaterial != value)
				{
					if (OnMaterialChange != null)
					{
						OnMaterialChange(value);
					}
				}
			}
		}

		#endregion

		#region Privates

		private int _kernel;

		private Material _previewMaterial;
		private RenderTexture _result;
		private ComputeShader _shader;
		private Texture _input;

		#region Flags

		private bool _currentTextureOriginallysRGB;
		private TextureImporterCompression _currentTextureOriginalCompression;

		// TODO: make enum instead
		private bool _shouldDispatch;
		private double _lastDispatch;

		#endregion

		#endregion

		#region Constants

		private static readonly int InputID = Shader.PropertyToID("Input");
		private static readonly int ResultID = Shader.PropertyToID("Result");

		private static readonly int FreckleBoundsID = Shader.PropertyToID("_FreckleBounds");
		private static readonly int FreckleMaskID = Shader.PropertyToID("_FreckleMask");
		private static readonly int FreckleScaleID = Shader.PropertyToID("_FreckleScale");
		private static readonly int FreckleSizeID = Shader.PropertyToID("_FreckleSize");
		private static readonly int FreckleRandomnessID = Shader.PropertyToID("_FreckleRandomness");
		private static readonly int FreckleAmountID = Shader.PropertyToID("_FreckleAmount");
		private static readonly int FreckleRoundnessID = Shader.PropertyToID("_FreckleRoundness");

		private static readonly int sRGBID = Shader.PropertyToID("_sRGB");

		#endregion

		#region Methods

		private Freckles()
		{
			LogWrapper("Constructor called", debugLog);

			OnMaterialChange += MaterialChangeHandler;
		}

		private void GUISettingsBody()
		{
			EditorGUILayout.Space(10);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.Space(10);
			EditorGUILayout.BeginVertical();
			EditorGUILayout.Space(10);

			PreviewMaterial = (Material) EditorGUILayout.ObjectField("", PreviewMaterial, typeof(Material), false, GUILayout.Width(200));

			EditorGUILayout.Space(10);

			if (GUILayout.Button("Apply booth face location preset", GUILayout.Width(200), GUILayout.Height(30)))
			{
				freckleBounds = new Vector4(0.4f, 0.6f, 0.79f, 0.86f);
				freckleMask = true;
			}

			EditorGUILayout.Space(10);

			freckleMask = EditorGUILayout.Toggle("Freckle Nose Mask", freckleMask);
			freckleBounds = EditorGUILayout.Vector4Field("Freckle Bounds", freckleBounds);
			freckleScale = EditorGUILayout.FloatField("Freckle Scale", freckleScale);
			freckleSize = EditorGUILayout.Slider("Freckle Size", freckleSize, 0.01f, 1f);
			freckleRandomness = EditorGUILayout.Slider("Freckle Randomness", freckleRandomness, -10f, 10f);
			freckleAmount = EditorGUILayout.Slider("Freckle Amount", freckleAmount, 0f, 1f);
			freckleRoundness = EditorGUILayout.Slider("Freckle Roundness", freckleRoundness, 0f, 2f);

			EditorGUILayout.Space(25);

			threadGroupSize = EditorGUILayout.FloatField("Thread group size", threadGroupSize);
			dispatchCooldown = EditorGUILayout.Slider("Shader dispatch cooldown", dispatchCooldown, 0.1f, 1f);
			debugLog = EditorGUILayout.Toggle("Debug logging", debugLog);

			EditorGUILayout.Space(30);

			if (GUILayout.Button("Save to texture", GUILayout.Width(200), GUILayout.Height(30)))
			{
				string path = AssetDatabase.GetAssetPath(_input);
				string newPath = NewPath(path, "_freckles.");

				AssetDatabase.CopyAsset(path, newPath);

				Dispatch(false);
				SaveTexture(_result, newPath);
				RevertTexture(newPath);

				EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture>(newPath));
			}

			if (_result != null)
			{
				Rect rect = GUILayoutUtility.GetRect(position.width, position.height, GUI.skin.box);
				EditorGUI.DrawPreviewTexture(rect, _result);
			}

			EditorGUILayout.Space(10);
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(10);
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.Space(10);
		}

		private void Dispatch(bool sRGB)
		{
			LogWrapper("Dispatch", debugLog);
			LogWrapper("srgb =" + sRGB, debugLog);


			if (_input != null)
			{
				_shader.SetVector(FreckleBoundsID, freckleBounds);
				_shader.SetBool(FreckleMaskID, freckleMask);
				_shader.SetFloat(FreckleScaleID, freckleScale);
				_shader.SetFloat(FreckleSizeID, freckleSize);
				_shader.SetFloat(FreckleRandomnessID, freckleRandomness);
				_shader.SetFloat(FreckleAmountID, freckleAmount);
				_shader.SetFloat(FreckleRoundnessID, freckleRoundness);

				_shader.SetBool(sRGBID, sRGB);

				_shader.SetTexture(_kernel, InputID, _input);
				_shader.SetTexture(_kernel, ResultID, _result);

				_shader.Dispatch(_kernel, Mathf.CeilToInt(_result.width / threadGroupSize), Mathf.CeilToInt(_result.height / threadGroupSize), 1);
			}
		}

		#endregion

		#region Unity Event functions

		private void OnEnable()
		{
			LogWrapper("OnEnable called", debugLog);

			titleContent = new GUIContent("Freckles");
			autoRepaintOnSceneChange = false;
			wantsMouseMove = false;
			wantsMouseEnterLeaveWindow = false;
			minSize = new Vector2(300, 300);

			// Unity throws "UnityExceptions" if you call FindObjectsOfTypeAll or get_timeSinceStartup from a ScriptableObject Constructor or instance field initializer so we assign these here
			// https://docs.unity3d.com/2019.4/Documentation/Manual/script-Serialization.html
			_lastDispatch = EditorApplication.timeSinceStartup;
			_shader = Resources.Load<ComputeShader>("Freckles");
			_kernel = _shader.FindKernel("freckles");

			// GameObject body = GameObject.Find("Body");
			// SkinnedMeshRenderer renderer = body.GetComponent<SkinnedMeshRenderer>();
			// Material[] materials = renderer.sharedMaterials;
			// Material material = materials.FirstOrDefault(x => x.name == "Body");
			// if (material != null)
			// {
			// 	Material = material;
			// }
		}

		private void OnDestroy()
		{
			LogWrapper("OnDestroy called", debugLog);

			if (_input != null)
			{
				RevertTexture(_input);
				_previewMaterial.mainTexture = _input;
				_input = null;
			}

			DestroyImmediate(_result);
		}

		private void OnGUI()
		{
			LogWrapper("OnGUI called", debugLog);

			EditorGUI.BeginChangeCheck();

			GUISettingsBody();

			if (EditorGUI.EndChangeCheck())
			{
				LogWrapper("EndChangeCheck", debugLog);
				_shouldDispatch = true;
			}
		}

		private void OnDisable()
		{
			LogWrapper("OnDisable called", debugLog);

			if (_input != null)
			{
				RevertTexture(_input);
				_previewMaterial.mainTexture = _input;
				_input = null;
			}
		}

		private void OnInspectorUpdate()
		{
			LogWrapper("OnInspectorUpdate called", debugLog);

			double currentTime = EditorApplication.timeSinceStartup;

			if (_shouldDispatch && _lastDispatch + dispatchCooldown < currentTime)
			{
				Dispatch(true);
				_shouldDispatch = false;
				_lastDispatch = EditorApplication.timeSinceStartup;
			}
		}

		#endregion

		#region Events, Delegates & Handlers

		public delegate void MaterialChangeDelegate(Material value);

		public event MaterialChangeDelegate OnMaterialChange;

		private void MaterialChangeHandler(Material value)
		{
			LogWrapper("MaterialChangeHandler", debugLog);

			if (_input != null)
			{
				RevertTexture(_input);
				_previewMaterial.mainTexture = _input;
			}

			_previewMaterial = value;

			if (value != null)
			{
				if (value.mainTexture != null)
				{
					_input = value.mainTexture;
					PrepareTexture();
					_result = GetWritableTempRTFromTexture(value.mainTexture);
					PreviewMaterial.mainTexture = _result;
				}
				else
				{
					_input = null;
					_result = null;
				}
			}
			else
			{
				_input = null;
				_result = null;
			}
		}

		#endregion

		#region Static Methods

		private static string NewPath(string path, string freckle)
		{
			string directoryName = Path.GetDirectoryName(path);
			string[] filename = Path.GetFileName(path).Split('.');
			string newFileName = filename[0] + freckle + filename[1];
			string newPath = directoryName + "/" + newFileName;
			newPath = newPath.Replace('\\', '/');
			return newPath;
		}

		private static void SetsRGB(bool sRGB, string path, out bool wassRGB)
		{
			TextureImporter a = (TextureImporter) AssetImporter.GetAtPath(path);
			wassRGB = a.sRGBTexture;
			a.sRGBTexture = sRGB;
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			AssetDatabase.Refresh();
		}

		private static void SetsRGB(bool sRGB, Texture texture, out bool wassRGB)
		{
			string path = AssetDatabase.GetAssetPath(texture);
			SetsRGB(sRGB, path, out wassRGB);
		}

		private static void SetCompression(bool sRGB, Texture texture, out TextureImporterCompression originalCompression)
		{
			string path = AssetDatabase.GetAssetPath(texture);
			SetCompression(sRGB, path, out originalCompression);
		}

		private static void SetCompression(bool sRGB, string path, out TextureImporterCompression originalCompression)
		{
			TextureImporter a = (TextureImporter) AssetImporter.GetAtPath(path);
			originalCompression = a.textureCompression;
			a.textureCompression = TextureImporterCompression.Uncompressed;
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			AssetDatabase.Refresh();
		}

		private void PrepareTexture()
		{
			string path = AssetDatabase.GetAssetPath(_input);
			TextureImporter t = (TextureImporter) AssetImporter.GetAtPath(path);
			_currentTextureOriginalCompression = t.textureCompression;
			_currentTextureOriginallysRGB = t.sRGBTexture;

			t.isReadable = true;
			t.textureCompression = TextureImporterCompression.Uncompressed;
			t.sRGBTexture = false;
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			AssetDatabase.Refresh();
		}

		private void RevertTexture(Texture tex)
		{
			string path = AssetDatabase.GetAssetPath(tex);
			RevertTexture(path);
		}

		private void RevertTexture(string path)
		{
			TextureImporter t = (TextureImporter) AssetImporter.GetAtPath(path);
			t.textureCompression = _currentTextureOriginalCompression;
			t.sRGBTexture = _currentTextureOriginallysRGB;
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			AssetDatabase.Refresh();
		}

		private static void SetReadable(string path)
		{
			TextureImporter importer = (TextureImporter) AssetImporter.GetAtPath(path);
			importer.isReadable = true;
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
		}

		private static void SetReadable(Texture tex)
		{
			string path = AssetDatabase.GetAssetPath(tex);
			SetReadable(path);
		}

		private static void SaveTexture(RenderTexture rt, string path)
		{
			byte[] bytes = Encode(ToTexture2D(rt), path);
			File.WriteAllBytes(path, bytes);
			AssetDatabase.Refresh();
		}

		private static Texture2D ToTexture2D(RenderTexture rTex)
		{
			Texture2D tex = new Texture2D(rTex.width, rTex.height, TextureFormat.RGBAFloat, false);
			RenderTexture.active = rTex;
			tex.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
			tex.Apply();
			RenderTexture.active = null;
			return tex;
		}

		private static byte[] Encode(Texture2D toTexture2D, string filename)
		{
			byte[] bytes = new byte[] { };
			if (filename.EndsWith(".png"))
			{
				bytes = toTexture2D.EncodeToPNG();
			}
			else if (filename.EndsWith(".jpg"))
			{
				bytes = toTexture2D.EncodeToJPG();
			}
			else if (filename.EndsWith(".exr"))
			{
				bytes = toTexture2D.EncodeToEXR();
			}
			else if (filename.EndsWith(".tga"))
			{
				bytes = toTexture2D.EncodeToTGA();
			}
			else
			{
				Debug.LogWarning("Please use a png/jpg/exr/tga file");
			}

			return bytes;
		}

		private static void LogWrapper(string message, bool debug)
		{
			if (debug)
			{
				Debug.Log(message);
			}
		}

		private static RenderTexture GetWritableTempRTFromTexture(Texture source)
		{
			RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(source.width, source.height, RenderTextureFormat.ARGBFloat, 0);
			RenderTexture rt = new RenderTexture(rtDesc)
			{
				enableRandomWrite = true,
				useMipMap = false, // source.mipmapCount > 1,
				autoGenerateMips = false, // source.mipmapCount > 1,
				graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
			};
			rt.Create();
			return rt;
		}

		[MenuItem("Tools/freckles")]
		private static void ShowWindow()
		{
			Freckles frecklesWindow = GetWindow<Freckles>("Freckles");
			frecklesWindow.Show();
		}

		#endregion
	}
}
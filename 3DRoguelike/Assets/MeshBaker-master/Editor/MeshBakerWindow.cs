/*
 * Copyright 2024 Oleg Dzhuraev <godlikeaurora@gmail.com>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * Modified for this project (URP):
 *  - 굽기 과정은 MeshBakerCore로 옮기고 이 창은 설정 UI만 담당
 *  - 선택한 오브젝트의 자식에 있는 메시도 함께 굽기 (부모 하나만 골라도 됨)
 *  - "원본 메시 끄기"는 오브젝트 대신 렌더러만 끔 (콜라이더 유지), 라이트맵 UV 생성은 선택 사항
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InsaneOne.DevTools
{
	public sealed class MeshBakerWindow : EditorWindow
	{
		static readonly List<int> supportedAtlasSizes = new List<int>
		{
			512, 1024, 2048, 4096, 8192
		};

		int selectedAtlasSizeId = 3;

		bool bakeNormals = true;
		bool bakeSpecular = true;
		bool bakeAo;
		bool sRgbFlagOnSpecular;
		bool saveMesh = true;
		bool generateLightmapUVs;

		MeshFilter targetMeshFilter;
		bool disableOriginalRenderers = true;

		string bakePostfix = "00";
		string bakeFolder = "Baked";

		[MenuItem("Tools/Mesh Baker")]
		public static void ShowWindow()
		{
			var wnd = GetWindow<MeshBakerWindow>();
			wnd.titleContent = new GUIContent("Mesh Baker");
			wnd.minSize = new Vector2(400, 400);
		}

		public void OnGUI()
		{
			EditorGUILayout.HelpBox("This tool allows you to combine several meshes into one. Also, it combines their textures to one atlas for draw calls optimization.\nURP Lit and Built-in Standard materials are supported.", MessageType.Info);

			GUILayout.Label("Scene settings", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Target mesh filter is filled automatically. But allows you to select MeshFilter component, where should be baken combined mesh.", MessageType.Info);
			targetMeshFilter = EditorGUILayout.ObjectField("Target mesh filter", targetMeshFilter, typeof(MeshFilter), true) as MeshFilter;
			disableOriginalRenderers = EditorGUILayout.Toggle(new GUIContent("Disable original renderers", "Only MeshRenderers are disabled, so colliders keep working."), disableOriginalRenderers);

			GUILayout.Label("Bake settings", EditorStyles.boldLabel);

			GUILayout.BeginHorizontal();
			GUILayout.Label($"Max final atlas size: <b>{GetAtlasSize()}</b>", BakerStyles.RichLabelStyle);
			if (GUILayout.Button("-"))
				ChangeAtlasSize(false);
			if (GUILayout.Button("+"))
				ChangeAtlasSize(true);
			GUILayout.EndHorizontal();

			bakeNormals = EditorGUILayout.Toggle("Normals atlas", bakeNormals);
			bakeSpecular = EditorGUILayout.Toggle("Specular/Metallic atlas", bakeSpecular);
			bakeAo = EditorGUILayout.Toggle("AO atlas", bakeAo);
			sRgbFlagOnSpecular = EditorGUILayout.Toggle(new GUIContent("sRGB on Specular/Metallic", "By design it should be DISABLED on these maps. But some assets was made with this flag enabled. So you can set it enabled here, if your assets using it and you want to keep same look for them."), sRgbFlagOnSpecular);
			saveMesh = EditorGUILayout.Toggle(new GUIContent("Save mesh to assets", "Mesh can be stored directly on scene, or, for better management and scene size reduce, it can be stored in assets."), saveMesh);
			generateLightmapUVs = EditorGUILayout.Toggle(new GUIContent("Generate lightmap UVs", "Slow on large meshes. Needed only for baked lighting."), generateLightmapUVs);

			bakeFolder = SanitizeFolder(EditorGUILayout.TextField("Bake folder name", bakeFolder));
			bakePostfix = SanitizeFilename(EditorGUILayout.TextField("Baked files postfix", bakePostfix));

			var prevEnabled = GUI.enabled;
			GUILayout.Space(10);

			var renderers = CollectSelected();
			if (renderers.Count < 2)
			{
				EditorGUILayout.HelpBox("Select at least 2 meshes (or a parent object containing them) to begin bake process.", MessageType.Info);
				GUI.enabled = false;
			}
			else
			{
				EditorGUILayout.HelpBox($"{renderers.Count} meshes will be baked. Bake process can take some time.", MessageType.Info);
			}

			if (GUILayout.Button("Bake"))
			{
				try
				{
					targetMeshFilter = MeshBakerCore.Bake(renderers, new MeshBakeSettings
					{
						atlasSize = GetAtlasSize(),
						bakeNormals = bakeNormals,
						bakeSpecular = bakeSpecular,
						bakeAo = bakeAo,
						sRgbOnSpecular = sRgbFlagOnSpecular,
						saveMesh = saveMesh,
						generateLightmapUVs = generateLightmapUVs,
						disableOriginalRenderers = disableOriginalRenderers,
						folder = bakeFolder,
						postfix = bakePostfix,
						target = targetMeshFilter
					});
				}
				catch (InvalidOperationException e)
				{
					EditorUtility.DisplayDialog("Mesh Baker", e.Message, "OK");
				}
			}

			GUI.enabled = prevEnabled;
		}

		/// <summary>선택한 오브젝트와 그 자식의 켜진 MeshRenderer (합친 결과를 넣을 대상은 제외)</summary>
		List<MeshRenderer> CollectSelected()
		{
			var set = new HashSet<MeshRenderer>();
			foreach (var go in Selection.gameObjects)
			foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
				if (r.enabled && (targetMeshFilter == null || r.gameObject != targetMeshFilter.gameObject))
					set.Add(r);
			return new List<MeshRenderer>(set);
		}

		void ChangeAtlasSize(bool increase)
		{
			selectedAtlasSizeId += increase ? 1 : -1;

			if (selectedAtlasSizeId < 0)
				selectedAtlasSizeId = supportedAtlasSizes.Count - 1;
			else if (selectedAtlasSizeId >= supportedAtlasSizes.Count)
				selectedAtlasSizeId = 0;
		}

		int GetAtlasSize() => supportedAtlasSizes[selectedAtlasSizeId];

		static string SanitizeFilename(string input)
		{
			var invalids = Path.GetInvalidFileNameChars();
			return string.Join("_", input.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
		}

		/// <summary>하위 폴더(a/b)는 허용</summary>
		static string SanitizeFolder(string input)
		{
			var parts = input.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++) parts[i] = SanitizeFilename(parts[i]);
			return string.Join("/", parts);
		}
	}
}

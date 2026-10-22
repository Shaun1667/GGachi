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
 *  - MeshBakerWindow의 굽기 과정을 이 파일로 옮겨 메뉴·스크립트에서도 호출할 수 있게 함
 *  - URP Lit 속성 이름(_BaseMap, _BaseColor)과 키워드(_NORMALMAP, _METALLICSPECGLOSSMAP, _SPECULAR_SETUP, _OCCLUSIONMAP)를 사용
 *    (없으면 Built-in Standard 이름 _MainTex, _Color로 대체)
 *  - 원본 텍스처의 Read/Write 설정을 바꾸지 않고 원본 파일(PNG/JPG) 또는 GPU 복사로 읽음
 *  - 재질마다 텍스처 해상도를 그대로 두고 아틀라스에 넣음 (가장 작은 해상도로 줄이지 않음)
 *  - 재질 색(_BaseColor)을 알베도에 곱해 넣고, 같은 메시를 여러 재질이 쓰는 경우도 재질 기준으로 UV를 옮김
 *  - 아틀라스 최대 크기를 텍스처 가져오기 설정(Max Size)에도 반영, 큰 알베도는 JPG로 저장해 용량을 줄임
 *  - 원본은 오브젝트를 끄지 않고 렌더러만 꺼서 콜라이더·조명은 그대로 동작
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InsaneOne.DevTools
{
	public sealed class MeshBakeSettings
	{
		/// <summary>아틀라스 최대 한 변 (모든 텍스처가 들어가지 않으면 비율대로 줄어듦)</summary>
		public int atlasSize = 4096;
		public bool bakeNormals = true;
		public bool bakeSpecular = true;
		public bool bakeAo;
		public bool sRgbOnSpecular;
		public bool saveMesh = true;
		public bool generateLightmapUVs;
		public bool disableOriginalRenderers = true;
		/// <summary>Assets 아래 폴더 (예: "Baked/BossArena")</summary>
		public string folder = "Baked";
		public string postfix = "00";
		/// <summary>합친 메시를 넣을 곳. 없으면 원점에 "CombinedMeshes"를 만든다</summary>
		public MeshFilter target;
	}

	public static class MeshBakerCore
	{
		enum TextureType { Albedo, Normal, Specular, Metallic, AO }

		const int Padding = 4; // 밉맵에서 이웃 텍스처 색이 번지지 않게 둘 여백 (픽셀)

		static readonly string[] AlbedoProps = { "_BaseMap", "_MainTex" };
		static readonly string[] ColorProps = { "_BaseColor", "_Color" };

		/// <summary>
		/// 렌더러들을 메시 하나 + 재질 하나로 굽는다. 재질이 하나(서브메시 하나)인 렌더러만 받는다.
		/// 반환: 합친 메시가 들어간 MeshFilter
		/// </summary>
		public static MeshFilter Bake(IList<MeshRenderer> renderers, MeshBakeSettings s)
		{
			var items = new List<(MeshRenderer renderer, MeshFilter filter, Material material)>();
			foreach (var r in renderers)
			{
				if (r == null || !r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null)
					continue;
				if (r.sharedMaterials.Length != 1 || mf.sharedMesh.subMeshCount != 1 || r.sharedMaterial == null)
					throw new InvalidOperationException($"'{r.name}'은(는) 재질이 하나인 메시만 구울 수 있습니다 (재질 {r.sharedMaterials.Length}개, 서브메시 {mf.sharedMesh.subMeshCount}개). 굽기를 멈춥니다.");
				items.Add((r, mf, r.sharedMaterial));
			}
			if (items.Count < 2)
				throw new InvalidOperationException("구울 메시가 2개 이상 필요합니다.");

			EnsureFolder("Assets/" + s.folder);

			// 재질 기준으로 아틀라스 칸을 나눈다 (같은 재질 = 같은 칸)
			var materials = new List<Material>();
			var materialIndex = new Dictionary<Material, int>();
			foreach (var it in items)
			{
				if (materialIndex.ContainsKey(it.material)) continue;
				materialIndex[it.material] = materials.Count;
				materials.Add(it.material);
			}

			bool specularWorkflow = IsSpecularWorkflow(materials[0]);
			TextureType specType = specularWorkflow ? TextureType.Specular : TextureType.Metallic;

			try
			{
				EditorUtility.DisplayProgressBar("Mesh Baker", "텍스처 읽는 중", 0.1f);
				var albedos = new Texture2D[materials.Count];
				for (int i = 0; i < materials.Count; i++)
				{
					Texture src = GetTexture(materials[i], AlbedoProps);
					if (src == null)
						throw new InvalidOperationException($"재질 '{materials[i].name}'에 알베도 텍스처가 없습니다. 굽기를 멈춥니다.");
					Texture2D tex = ReadTexture(src, false);
					Tint(tex, GetColor(materials[i]));
					albedos[i] = FitInside(tex, s.atlasSize);
				}

				EditorUtility.DisplayProgressBar("Mesh Baker", "알베도 아틀라스 만드는 중", 0.3f);
				var albedoAtlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
				Rect[] rects = albedoAtlas.PackTextures(albedos, Padding, s.atlasSize, false);

				string dir = "Assets/" + s.folder + "/";
				var finalMat = MakeMaterial(materials[0], s, specularWorkflow);
				SetTexture(finalMat, AlbedoProps, SaveAtlas(albedoAtlas, TextureType.Albedo, s, dir));

				if (s.bakeNormals)
				{
					EditorUtility.DisplayProgressBar("Mesh Baker", "노멀 아틀라스 만드는 중", 0.45f);
					var atlas = ComposeAtlas(albedoAtlas.width, albedoAtlas.height, rects, materials, new Color(0.5f, 0.5f, 1f, 1f),
						m => m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") ? ReadTexture(m.GetTexture("_BumpMap"), true) : null);
					finalMat.SetTexture("_BumpMap", SaveAtlas(atlas, TextureType.Normal, s, dir));
					if (finalMat.HasProperty("_BumpScale")) finalMat.SetFloat("_BumpScale", 1f);
				}

				if (s.bakeSpecular)
				{
					EditorUtility.DisplayProgressBar("Mesh Baker", "금속/스페큘러 아틀라스 만드는 중", 0.6f);
					string prop = specularWorkflow ? "_SpecGlossMap" : "_MetallicGlossMap";
					var atlas = ComposeAtlas(albedoAtlas.width, albedoAtlas.height, rects, materials, new Color(0.5f, 0.5f, 0.5f, 1f),
						m => m.HasProperty(prop) && m.GetTexture(prop) ? ReadTexture(m.GetTexture(prop), false) : null);
					finalMat.SetTexture(prop, SaveAtlas(atlas, specType, s, dir));
				}

				if (s.bakeAo)
				{
					EditorUtility.DisplayProgressBar("Mesh Baker", "AO 아틀라스 만드는 중", 0.7f);
					var atlas = ComposeAtlas(albedoAtlas.width, albedoAtlas.height, rects, materials, Color.white,
						m => m.HasProperty("_OcclusionMap") && m.GetTexture("_OcclusionMap") ? ReadTexture(m.GetTexture("_OcclusionMap"), false) : null);
					finalMat.SetTexture("_OcclusionMap", SaveAtlas(atlas, TextureType.AO, s, dir));
				}
				SetKeywords(finalMat, s, specularWorkflow);
				SaveAsset(finalMat, $"{dir}BakedMaterial{s.postfix}.mat");

				EditorUtility.DisplayProgressBar("Mesh Baker", "메시 합치는 중", 0.8f);
				var target = s.target;
				if (target == null)
				{
					var go = new GameObject("CombinedMeshes");
					Undo.RegisterCreatedObjectUndo(go, "Mesh Bake");
					target = go.AddComponent<MeshFilter>();
				}
				if (!target.TryGetComponent(out MeshRenderer targetRenderer))
					targetRenderer = target.gameObject.AddComponent<MeshRenderer>();

				var mesh = CombineMeshes(items, rects, materialIndex, target.transform, s.generateLightmapUVs);
				if (s.saveMesh)
					mesh = SaveAsset(mesh, $"{dir}BakedMesh{s.postfix}.asset");

				Undo.RecordObject(target, "Mesh Bake");
				Undo.RecordObject(targetRenderer, "Mesh Bake");
				target.sharedMesh = mesh;
				targetRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{dir}BakedMaterial{s.postfix}.mat");

				if (s.disableOriginalRenderers)
				{
					foreach (var it in items)
					{
						if (it.renderer == targetRenderer) continue;
						Undo.RecordObject(it.renderer, "Mesh Bake");
						it.renderer.enabled = false;
					}
				}
				return target;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		// ───────────────────────── 텍스처 ─────────────────────────

		/// <summary>
		/// 텍스처를 읽을 수 있는 복사본으로. 원본 PNG/JPG 파일이 있으면 그 파일을 그대로 읽고(가져오기 압축 전 원본),
		/// 없으면 GPU에서 복사한다. 노멀맵은 압축 형식(BC5/DXT5nm)을 풀어 일반 RGB 노멀로 돌려준다
		/// </summary>
		static Texture2D ReadTexture(Texture source, bool isNormal)
		{
			string path = AssetDatabase.GetAssetPath(source);
			string ext = Path.GetExtension(path).ToLowerInvariant();
			if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
			{
				var file = new Texture2D(2, 2, TextureFormat.RGBA32, false, isNormal);
				if (file.LoadImage(File.ReadAllBytes(path), false))
					return file;
			}

			var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
				isNormal ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
			Graphics.Blit(source, rt);
			var prev = RenderTexture.active;
			RenderTexture.active = rt;
			var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, isNormal);
			tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
			tex.Apply();
			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(rt);

			if (isNormal && IsImportedAsNormalMap(path))
			{
				// 압축된 노멀: x = r*a (BC5는 a=1, DXT5nm은 r=1), y = g, z는 계산
				var px = tex.GetPixels();
				for (int i = 0; i < px.Length; i++)
				{
					float x = px[i].r * px[i].a * 2f - 1f;
					float y = px[i].g * 2f - 1f;
					float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
					px[i] = new Color(x * 0.5f + 0.5f, y * 0.5f + 0.5f, z * 0.5f + 0.5f, 1f);
				}
				tex.SetPixels(px);
				tex.Apply();
			}
			return tex;
		}

		static bool IsImportedAsNormalMap(string path)
		{
			return AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType == TextureImporterType.NormalMap;
		}

		static void Tint(Texture2D tex, Color color)
		{
			if (color == Color.white) return;
			var px = tex.GetPixels();
			for (int i = 0; i < px.Length; i++) px[i] *= color;
			tex.SetPixels(px);
			tex.Apply();
		}

		/// <summary>한 변이 max를 넘으면 비율대로 줄인다</summary>
		static Texture2D FitInside(Texture2D tex, int max)
		{
			if (tex.width <= max && tex.height <= max) return tex;
			float k = (float)max / Mathf.Max(tex.width, tex.height);
			return Resized(tex, Mathf.Max(1, Mathf.RoundToInt(tex.width * k)), Mathf.Max(1, Mathf.RoundToInt(tex.height * k)));
		}

		static Texture2D Resized(Texture2D tex, int width, int height)
		{
			if (tex.width == width && tex.height == height) return tex;
			var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
			copy.SetPixels(tex.GetPixels());
			ThirdParty.TextureScale.Bilinear(copy, width, height);
			return copy;
		}

		/// <summary>알베도 아틀라스와 같은 칸 배치로 다른 맵 아틀라스를 만든다. 맵이 없는 재질 칸은 기본색</summary>
		static Texture2D ComposeAtlas(int width, int height, Rect[] rects, List<Material> materials, Color fill, Func<Material, Texture2D> read)
		{
			// 8192 아틀라스는 Color[]로 1GB가 넘어서 Color32로 다룬다
			var atlas = new Texture2D(width, height, TextureFormat.RGBA32, false);
			var px = new Color32[width * height];
			Color32 fill32 = fill;
			for (int i = 0; i < px.Length; i++) px[i] = fill32;
			atlas.SetPixels32(px);
			px = null;
			for (int i = 0; i < materials.Count; i++)
			{
				Texture2D map = read(materials[i]);
				if (map == null) continue;
				int x = Mathf.RoundToInt(rects[i].x * width), y = Mathf.RoundToInt(rects[i].y * height);
				int w = Mathf.RoundToInt(rects[i].width * width), h = Mathf.RoundToInt(rects[i].height * height);
				atlas.SetPixels32(x, y, w, h, Resized(map, w, h).GetPixels32());
			}
			atlas.Apply();
			return atlas;
		}

		static Texture2D SaveAtlas(Texture2D atlas, TextureType type, MeshBakeSettings s, string dir)
		{
			// 4096을 넘는 색 맵은 PNG면 수십 MB라 JPG로 (노멀은 압축 손실이 눈에 띄어 PNG 유지)
			bool jpg = type != TextureType.Normal && Mathf.Max(atlas.width, atlas.height) > 4096;
			string fileName = $"Baked{type}{s.postfix}{(jpg ? ".jpg" : ".png")}";
			string other = $"{dir}Baked{type}{s.postfix}{(jpg ? ".png" : ".jpg")}";
			if (File.Exists(other)) AssetDatabase.DeleteAsset(other);

			File.WriteAllBytes(dir + fileName, jpg ? atlas.EncodeToJPG(95) : atlas.EncodeToPNG());
			AssetDatabase.ImportAsset(dir + fileName, ImportAssetOptions.ForceUpdate);
			if (AssetImporter.GetAtPath(dir + fileName) is TextureImporter importer)
			{
				importer.textureType = type == TextureType.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
				importer.sRGBTexture = type == TextureType.Albedo || ((type == TextureType.Metallic || type == TextureType.Specular) && s.sRgbOnSpecular);
				importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(atlas.width, atlas.height));
				importer.SaveAndReimport();
			}
			return AssetDatabase.LoadAssetAtPath<Texture2D>(dir + fileName);
		}

		// ───────────────────────── 재질 ─────────────────────────

		static bool IsSpecularWorkflow(Material m)
		{
			if (m.HasProperty("_WorkflowMode")) return m.GetFloat("_WorkflowMode") < 0.5f; // URP Lit: 0 = Specular, 1 = Metallic
			return m.HasProperty("_SpecGlossMap") && m.GetTexture("_SpecGlossMap");
		}

		static Material MakeMaterial(Material original, MeshBakeSettings s, bool specular)
		{
			var material = new Material(original.shader);
			material.CopyPropertiesFromMaterial(original);
			foreach (string p in AlbedoProps)
			{
				if (!material.HasProperty(p)) continue;
				material.SetTextureScale(p, Vector2.one);
				material.SetTextureOffset(p, Vector2.zero);
			}
			foreach (string p in ColorProps)
				if (material.HasProperty(p)) material.SetColor(p, Color.white); // 색은 알베도에 이미 곱해 넣음
			return material;
		}

		static void SetKeywords(Material m, MeshBakeSettings s, bool specular)
		{
			if (s.bakeNormals) m.EnableKeyword("_NORMALMAP");
			if (s.bakeSpecular)
			{
				m.EnableKeyword("_METALLICSPECGLOSSMAP"); // URP
				m.EnableKeyword(specular ? "_SPECGLOSSMAP" : "_METALLICGLOSSMAP"); // Built-in
			}
			if (specular) m.EnableKeyword("_SPECULAR_SETUP");
			if (s.bakeAo) m.EnableKeyword("_OCCLUSIONMAP");
		}

		static Texture GetTexture(Material m, string[] props)
		{
			foreach (string p in props)
				if (m.HasProperty(p) && m.GetTexture(p)) return m.GetTexture(p);
			return null;
		}

		static void SetTexture(Material m, string[] props, Texture tex)
		{
			foreach (string p in props)
				if (m.HasProperty(p)) m.SetTexture(p, tex);
		}

		static Color GetColor(Material m)
		{
			foreach (string p in ColorProps)
				if (m.HasProperty(p)) return m.GetColor(p);
			return Color.white;
		}

		// ───────────────────────── 메시 ─────────────────────────

		static Mesh CombineMeshes(List<(MeshRenderer renderer, MeshFilter filter, Material material)> items, Rect[] rects,
			Dictionary<Material, int> materialIndex, Transform target, bool lightmapUVs)
		{
			var combine = new CombineInstance[items.Count];
			long vertices = 0;
			for (int i = 0; i < items.Count; i++)
			{
				var src = items[i].filter.sharedMesh;
				var copy = Object.Instantiate(src);
				Rect rect = rects[materialIndex[items[i].material]];
				var uvs = src.uv;
				for (int v = 0; v < uvs.Length; v++)
					uvs[v] = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, uvs[v].x), Mathf.Lerp(rect.yMin, rect.yMax, uvs[v].y));
				copy.uv = uvs;
				combine[i] = new CombineInstance
				{
					mesh = copy,
					transform = target.worldToLocalMatrix * items[i].filter.transform.localToWorldMatrix
				};
				vertices += src.vertexCount;
			}

			var mesh = new Mesh { name = "BakedMesh" };
			mesh.indexFormat = vertices >= 65536 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.CombineMeshes(combine, true, true);
			foreach (var c in combine) Object.DestroyImmediate(c.mesh);
			mesh.RecalculateBounds();
			if (lightmapUVs) Unwrapping.GenerateSecondaryUVSet(mesh);
			return mesh;
		}

		// ───────────────────────── 에셋 ─────────────────────────

		/// <summary>같은 경로에 이미 있으면 GUID를 유지한 채 내용만 바꾼다 (다시 구워도 참조가 끊기지 않게)</summary>
		static T SaveAsset<T>(T asset, string path) where T : Object
		{
			var existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing == null)
			{
				AssetDatabase.CreateAsset(asset, path);
				return asset;
			}
			EditorUtility.CopySerialized(asset, existing);
			EditorUtility.SetDirty(existing);
			AssetDatabase.SaveAssets();
			return existing;
		}

		static void EnsureFolder(string path)
		{
			string[] parts = path.Split('/');
			string current = parts[0];
			for (int i = 1; i < parts.Length; i++)
			{
				string next = current + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
				current = next;
			}
		}
	}
}

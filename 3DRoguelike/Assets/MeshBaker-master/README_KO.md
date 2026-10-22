# Mesh Baker (URP 수정판) 사용 설명서

여러 메시를 **메시 하나 + 재질 하나**로 합치는 에디터 도구입니다.
각 재질의 텍스처는 텍스처 한 장(아틀라스)으로 모읍니다.
그리는 횟수(드로우콜)를 줄이는 최적화에 씁니다.

원본: [OlegDzhuraev/MeshBaker](https://github.com/OlegDzhuraev/MeshBaker) (Apache License 2.0).
이 버전은 URP에서 쓸 수 있게 고친 것입니다. 바뀐 내용은 `Editor/MeshBakerCore.cs`와 `Editor/MeshBakerWindow.cs` 머리말에 적혀 있습니다.

---

## ⚠ 먼저 꼭 알아 둘 것: 움직이지 않는 오브젝트에만 쓰세요

굽고 나면 여러 오브젝트가 **메시 하나로 합쳐집니다.** 그래서 다음 오브젝트에는 쓰면 안 됩니다.

- 따로 **움직이거나 회전·크기가 바뀌는** 오브젝트: 문, 엘리베이터, 흔들리는 소품
- **애니메이션**이 있는 오브젝트, 캐릭터처럼 뼈가 있는 **Skinned Mesh**
- 게임 중에 **부서지거나 사라지거나 켜고 꺼지는** 오브젝트
- 개별로 **색·재질을 바꾸는** 오브젝트: 피격 깜빡임, 선택 강조

굽기에 맞는 것은 바닥, 벽, 기둥, 바위, 배경 소품처럼 **처음부터 끝까지 그 자리에 있는 것**입니다.

굽고 나서 원본을 옮겨도 합쳐진 메시는 따라 움직이지 않습니다. 원본 배치를 바꿨다면 **다시 구워야** 합니다.

---

## 설치

1. Unity에서 **Assets → Import Package → Custom Package…**를 누르고 `MeshBaker_URP.unitypackage`를 고릅니다.
2. `Assets/MeshBaker-master` 폴더가 생기면 끝입니다.

- 지원: Unity 6 (6000.x), URP Lit 재질, Built-in Standard 재질
- 에디터 전용 도구입니다. 빌드에는 들어가지 않습니다. `ThirdParty/TextureScale.cs`만 런타임 폴더에 있는데, 게임 코드에서 부르지 않으면 영향이 없습니다.

---

## 사용 방법 (창)

1. 위쪽 메뉴에서 **Tools → Mesh Baker**를 엽니다.
2. 씬에서 합칠 오브젝트를 선택합니다.
   - **부모 오브젝트 하나만 골라도** 그 아래 자식 메시가 모두 들어갑니다.
   - 꺼져 있는 렌더러는 빠집니다.
3. 설정을 고르고 **Bake**를 누릅니다.
4. 결과가 `CombinedMeshes` 오브젝트로 생깁니다. Target mesh filter를 지정했다면 그곳에 생깁니다.
   - 원본은 지워지지 않고 **렌더러만 꺼집니다.**
   - 되돌리려면 `Ctrl+Z`를 누르세요.

### 설정

| 항목 | 설명 |
|---|---|
| Target mesh filter | 합친 메시를 넣을 MeshFilter. 비우면 원점에 새로 만듭니다 |
| Disable original renderers | 원본의 **MeshRenderer만** 끕니다. 콜라이더·조명·스크립트는 그대로 동작합니다 |
| Max final atlas size | 아틀라스 최대 크기(512~8192). 다 들어가지 않으면 비율대로 줄어듭니다 |
| Normals atlas | 노멀맵을 합칩니다. 노멀맵이 없는 재질 칸은 평평한 노멀로 채웁니다 |
| Specular/Metallic atlas | 금속/스페큘러 맵을 합칩니다. 맵이 없으면 끄는 것이 좋습니다 |
| AO atlas | 앰비언트 오클루전 맵을 합칩니다 |
| sRGB on Specular/Metallic | 원본 맵이 sRGB로 만들어진 경우에만 켭니다 |
| Save mesh to assets | 합친 메시를 에셋 파일로 저장합니다. 끄면 씬 파일에 저장돼 씬이 커집니다 |
| Generate lightmap UVs | 베이크 라이트맵용 UV를 만듭니다. 정점이 많으면 **매우 느립니다.** 실시간 조명만 쓴다면 끄세요 |
| Bake folder name | 결과 폴더 (`Assets/` 아래, `Baked/Arena`처럼 하위 폴더도 가능) |
| Baked files postfix | 결과 파일 이름 뒤에 붙는 글자 |

### 결과 파일 (`Assets/<폴더>/`)

- `BakedMesh<postfix>.asset`: 합친 메시
- `BakedMaterial<postfix>.mat`: 합친 재질 (첫 번째 재질의 셰이더·설정을 복사)
- `BakedAlbedo<postfix>.png/.jpg`: 색 아틀라스. 4096보다 크면 용량 때문에 JPG로 저장합니다
- `BakedNormal<postfix>.png`, `BakedMetallic…`, `BakedAO…`: 켠 맵의 아틀라스

같은 폴더·postfix로 다시 구우면 파일을 덮어씁니다. 이때 **에셋 GUID는 그대로**라서 씬의 참조가 끊기지 않습니다.

---

## 사용 방법 (코드)

메뉴나 자동화 스크립트에서 이렇게 부를 수 있습니다. 에디터 코드에서만 쓸 수 있습니다.

```csharp
using InsaneOne.DevTools;

var renderers = new List<MeshRenderer>(root.GetComponentsInChildren<MeshRenderer>());
var target = new GameObject("Baked").AddComponent<MeshFilter>();
target.gameObject.AddComponent<MeshRenderer>();

MeshBakerCore.Bake(renderers, new MeshBakeSettings
{
    atlasSize = 4096,
    bakeNormals = true,
    bakeSpecular = false,
    folder = "Baked/MyLevel",
    postfix = "",
    target = target
});
```

다시 구울 때는 꺼 둔 원본 렌더러를 먼저 다시 켜고 이전 결과 오브젝트를 지운 뒤 부르세요.
그래야 몇 번을 실행해도 결과가 같습니다.

---

## 주의사항과 제한

**메시·재질**
- 메시마다 **재질이 하나(서브메시 하나)**여야 합니다. 재질이 여러 개인 메시가 섞여 있으면 굽기를 멈추고 알려 줍니다.
- 메시 UV가 **0~1 범위 안**이어야 합니다. 텍스처를 반복(타일링)해서 쓰는 바닥 같은 메시는 아틀라스에서 무늬가 깨집니다. 그런 메시는 빼고 구우세요.
- 재질 타일링/오프셋 값은 무시됩니다. 재질 색(`_BaseColor`)은 알베도에 곱해서 넣습니다.
- 합친 재질은 **첫 번째 재질의 셰이더와 설정**을 따릅니다. Smoothness, 투명/불투명 등이 그 예입니다.
  - 투명 재질과 불투명 재질, 서로 다른 셰이더는 섞어 굽지 마세요.
- Emission, Detail 맵, Height 맵은 합치지 않습니다.

**텍스처**
- 원본 텍스처의 가져오기 설정(Read/Write 등)은 바꾸지 않습니다. 원본 PNG/JPG 파일을 직접 읽고, 그 밖의 형식은 GPU에서 복사해 읽습니다.
- 텍스처는 각자 해상도대로 들어갑니다. 전부 합친 크기가 Max atlas size를 넘으면 모두 같은 비율로 작아집니다.
- 8192 아틀라스는 메모리를 많이 씁니다. 굽는 동안 잠깐 수 GB를 쓸 수 있습니다.

**성능·용량**
- 합친 메시는 **화면 밖 부분만 따로 빼고 그리는 컬링이 되지 않습니다.** 메시 하나로 묶이기 때문입니다.
  - 넓은 맵을 통째로 합치면 오히려 느려질 수 있습니다.
  - 카메라에 함께 보이는 구역 단위로 나눠 구우세요.
- 정점 65,535개를 넘으면 32비트 인덱스를 씁니다. PC와 콘솔에서는 문제없고, 아주 오래된 모바일 기기에서는 안 될 수 있습니다.
- 메시 파일이 클 수 있습니다. 예를 들어 정점 47만 개짜리 메시는 약 60MB입니다.
  - GitHub는 50MB가 넘으면 경고하고 100MB부터 막습니다.
  - 필요하면 Git LFS를 쓰거나 결과 폴더를 저장소에서 빼세요.
- LOD Group이 붙은 오브젝트는 LOD를 잃습니다. 모든 단계가 함께 합쳐지기 때문입니다. LOD 오브젝트는 빼고 구우세요.

**기타**
- 원본 오브젝트는 남기고 렌더러만 끕니다. 그래서 **콜라이더는 원본 것을 그대로 씁니다.** 원본을 지우면 충돌도 사라집니다.
- 굽기 결과가 마음에 들지 않으면 `Ctrl+Z`로 되돌리거나, 합친 오브젝트를 지우고 원본 렌더러를 다시 켜면 됩니다.

---

## 문제 해결

| 증상 | 원인과 해결 |
|---|---|
| "재질이 하나인 메시만 구울 수 있습니다" | 재질이 여러 개인 메시가 선택됨. 빼고 굽거나 메시를 재질별로 나누세요 |
| "알베도 텍스처가 없습니다" | 색 텍스처가 없는 재질이 섞임. 텍스처가 있는 재질만 구우세요 |
| 합친 뒤 무늬가 깨짐 | UV가 0~1을 벗어나는(타일링) 메시가 섞임 |
| 합친 뒤 흐릿함 | 텍스처가 아틀라스에 다 안 들어가 줄어듦. Max atlas size를 키우세요 |
| 합친 뒤 분홍색 | 첫 번째 재질의 셰이더가 현재 렌더 파이프라인과 맞지 않음 |
| 굽기가 매우 오래 걸림 | Generate lightmap UVs가 켜져 있음. 실시간 조명이면 끄세요 |

---

## 라이선스

Apache License 2.0. `License.md`를 참고하세요.
`ThirdParty/TextureScale.cs`는 Eric Haines의 코드입니다(CC BY-SA 3.0).

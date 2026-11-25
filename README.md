# MD5Viewer

OpenGL-based Doom 3 MD5 mesh and animation viewer written in C# with OpenTK and Windows Forms.

The repository includes the Cyberdemon MD5 mesh, textures, and animations so the viewer builds and runs with no additional assets.

![Cyberdemon animation preview](md5-sample.gif)

## Features

- Doom 3 `.md5mesh` and `.md5anim` loading
- Skeletal animation blended in joint-local space (lerp translation, SLERP rotation), then concatenated through the hierarchy, as in Doom 3
- Doom 3 cycle timing: the duplicated last frame of looping animations is not replayed, so loops are seamless
- Root-motion lock: walk cycles play in place instead of snapping back every loop
- Bind-pose tangent-space basis skinned with joint quaternions; normals are shared across UV seams
- Indexed, streamed vertex upload (static index buffer, dynamic vertex buffer)
- Energy-normalised Blinn-Phong shading with diffuse, normal map, and RGB specular, plus fill light and hemispherical ambient
- sRGB-correct diffuse and specular sampling (linear lighting, exact sRGB output)
- Strict validation of MD5 indices, joint hierarchy, and frame data
- Animation selector, play/pause, and timeline scrubbing
- Multiple diagnostic render modes (wireframe, points, texture, lighting, normal map, specular map, bump relief, TBN)
- Back-face culling with optional winding flip (`F`)
- Normal-map Y-flip toggle for OpenGL/DirectX convention switching (`N`)

## Requirements

- Windows
- .NET 10 SDK
- OpenGL 3.3-compatible GPU driver

## Build and run

```powershell
dotnet restore source/MD5Viewer.sln
dotnet run --project source/MD5Viewer/MD5Viewer.csproj
```

Release build:

```powershell
dotnet build source/MD5Viewer.sln -c Release
```

The project copies the contents of `cyberdemon/` into the `Assets` output directory automatically at build time.

## Controls

| Input | Action |
|---|---|
| Left drag | Orbit camera |
| Right drag | Pan camera |
| Scroll wheel | Zoom |
| Right double-click | Reset camera |
| `N` | Toggle normal-map Y direction |
| `F` | Toggle front-face winding |

The **Render Mode** dropdown provides: Full Render, Points, Wireframe, Texture, Lighting, Normal Map, Specular Map, Bump Map, and TBN diagnostic views.

## Render modes

| Mode | Description |
|---|---|
| FullRender | Blinn-Phong with diffuse + normal map + specular |
| Lighting | Same shading using geometry normals only (no bump) |
| Texture | Raw diffuse texture in gamma space |
| NormalMap | Decoded world-space bump normal visualised as colour |
| SpecularMap | Raw specular texture |
| BumpMap | Grazing-light relief diagnostic |
| DiagnosticTBN | How much the tangent (R), bitangent (G) and normal (B) face the viewer; mirrored UV islands are darker |
| Wireframe | CCW triangles in solid-colour overlay |
| Points | Vertex positions |

## Project layout

```
MD5Viewer/
├── cyberdemon/          Doom 3 Cyberdemon mesh, animations, and textures
└── source/
    └── MD5Viewer/
        ├── MD5Model.cs  MD5 mesh + animation loader and skeletal skinning
        ├── Viewport.cs  OpenGL render loop, shaders, camera, texture loading
        ├── Form1.cs     Main window: animation selector, timeline, controls
        ├── MyMath.cs    Quaternion utilities (rotate, slerp, multiply)
        ├── Vertex.cs    Interleaved vertex (position, normal, UV, TBN)
        └── TgaImage.cs  Minimal TGA loader (8/24/32-bit, raw and RLE)
```

## Asset notice

The bundled Cyberdemon and Doom 3-related assets are included for educational and technical demonstration purposes. Doom 3 and its assets are the property of their respective copyright holders. Review the applicable asset licence and distribution rights before redistributing or using them commercially.

using System.Diagnostics;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.WinForms;
using System.IO;
using GLFWException = OpenTK.Windowing.GraphicsLibraryFramework.GLFWException;
using PixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;

namespace MD5Viewer
{
    public enum RenderPhase
    {
        FullRender,
        Points,
        Wireframe,
        Texture,
        Light,
        NormalMap,
        SpecularMap,
        BumpMap,
        DiagnosticTBN
    }

    public partial class Viewport : GLControl
    {
        private const int FloatsPerVertex = 14;

        /// <summary>Largest time step fed to the animation, so stalls (window drag, breakpoints) do not cause jumps.</summary>
        private const float MaxFrameDelta = 0.1f;

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly float[] gridVertices;

        private Vector3 modelCenter;
        private Vector3 panOffset;
        private Vector3 lightPosition = new Vector3(80, -160, 180);
        private Vector3 cameraEye;
        private float modelRadius = 80f;
        private float rotx = 15f;   // camera pitch in degrees (positive = above the target)
        private float roty = 25f;
        private float cameraDistance = 180f;
        private int lastx;
        private int lasty;

        private int shaderProgram;
        private int modelVao;
        private int modelVbo;
        private int modelEbo;
        private int gridVao;
        private int gridVbo;
        private int diffuseTextureId;
        private int normalTextureId;
        private int specularTextureId;
        private int modelBufferBytes;
        private int vertexCount;
        private int indexCount;
        private int[] meshIndexOffsets = [];
        private int[] meshIndexCounts = [];
        private bool[] meshIsEffect = [];
        private float[] vertexUploadBuffer = [];
        private MD5Model uploadedTopology;
        private string diffuseFile;
        private string normalFile;
        private string specularFile;
        private bool glReady;
        private bool modelDirty;
        private bool normalYFlip = false;
        private bool flipWinding = false;

        // Uniform locations, looked up once after linking.
        private int uModelLoc, uViewLoc, uProjectionLoc, uNormalMatrixLoc;
        private int uLightPositionLoc, uFillDirectionLoc, uCameraPosLoc, uNormalYFlipLoc, uModeLoc;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MD5Model MD5Model { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public RenderPhase Phase { get; set; } = RenderPhase.FullRender;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPaused { get; set; }

        /// <summary>Called each render tick with (currentFrame, totalFrames) for the timeline UI.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Action<int, int> FrameCallback { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string RenderStatus =>
            $"GL: {glReady}, vertices: {vertexCount}, triangles: {indexCount / 3}, radius: {modelRadius:0.0}, distance: {cameraDistance:0.0}, error: {lastGlError}";

        private ErrorCode lastGlError = ErrorCode.NoError;

        public Viewport()
        {
            InitializeComponent();
            gridVertices = BuildGridVertices();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!TryMakeCurrent()) return;
            InitializeOpenGl();
            glReady = true;

            if (MD5Model != null)
            {
                InitializeSceneResources();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!CanUseOpenGl() || Width <= 0 || Height <= 0)
            {
                return;
            }

            if (!TryMakeCurrent()) return;
            GL.Viewport(0, 0, Width, Height);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!CanUseOpenGl())
            {
                return;
            }

            Render();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            glReady = false;
            base.OnHandleDestroyed(e);
        }

        private bool CanUseOpenGl() =>
            glReady && IsHandleCreated && !IsDisposed && !Disposing;

        private bool TryMakeCurrent()
        {
            if (!IsHandleCreated || IsDisposed) return false;

            try
            {
                MakeCurrent();
                return true;
            }
            catch (GLFWException exception)
            {
                Debug.WriteLine($"OpenGL context is unavailable: {exception.Message}");
                return false;
            }
        }

        private void ReleaseOpenGlResources()
        {
            if (!glReady) return;

            timer?.Stop();
            if (TryMakeCurrent())
            {
                DeleteTexture(ref diffuseTextureId);
                DeleteTexture(ref normalTextureId);
                DeleteTexture(ref specularTextureId);
                if (modelVbo != 0) GL.DeleteBuffer(modelVbo);
                if (modelEbo != 0) GL.DeleteBuffer(modelEbo);
                if (gridVbo != 0) GL.DeleteBuffer(gridVbo);
                if (modelVao != 0) GL.DeleteVertexArray(modelVao);
                if (gridVao != 0) GL.DeleteVertexArray(gridVao);
                if (shaderProgram != 0) GL.DeleteProgram(shaderProgram);
            }

            modelVbo = modelEbo = gridVbo = modelVao = gridVao = shaderProgram = 0;
            modelBufferBytes = 0;
            uploadedTopology = null;
            glReady = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Keep behavior for direct focus, but expose public toggles for Form-level interception
            if (e.KeyCode == Keys.N)
            {
                ToggleNormalYFlip();
            }
            else if (e.KeyCode == Keys.F)
            {
                ToggleFlipWinding();
            }
        }

        // Public toggles so the parent form can intercept keys (KeyPreview) and avoid
        // other controls (e.g. ComboBox) stealing keyboard focus and changing render mode.
        public void ToggleNormalYFlip()
        {
            normalYFlip = !normalYFlip;
            Debug.WriteLine($"Normal Y flip toggled: {normalYFlip}");
            Invalidate();
        }

        public void ToggleFlipWinding()
        {
            flipWinding = !flipWinding;
            Debug.WriteLine($"Flip triangle winding toggled: {flipWinding}");
            Invalidate();
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool NormalYFlip
        {
            get => normalYFlip;
            set { normalYFlip = value; Invalidate(); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool FlipWinding
        {
            get => flipWinding;
            set { flipWinding = value; Invalidate(); }
        }

        public void MarkModelDirty() => modelDirty = true;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Motion(e.X, e.Y, e.Button);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Typical wheel delta is ±120; scale appropriately to zoom smoothly
            float wheelDelta = e.Delta / 120f;
            cameraDistance = MathF.Max(modelRadius * 0.05f, cameraDistance - wheelDelta * modelRadius * 0.1f);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            lastx = e.X;
            lasty = e.Y;

            if (e.Button == MouseButtons.Right && e.Clicks > 1)
            {
                ResetCamera();
                Invalidate();
            }
        }

        public void LoadScene(MD5Model model, string diffuseFile, string normalFile, string specularFile)
        {
            MD5Model = model;
            this.diffuseFile = diffuseFile;
            this.normalFile = normalFile;
            this.specularFile = specularFile;

            if (!glReady)
            {
                modelDirty = true;
                return;
            }

            InitializeSceneResources();
        }

        private void InitializeSceneResources()
        {
            if (!TryMakeCurrent()) return;
            DeleteTexture(ref diffuseTextureId);
            DeleteTexture(ref normalTextureId);
            DeleteTexture(ref specularTextureId);

            // Colour textures (diffuse, specular) are sRGB-encoded and are decoded to linear
            // by the sampler; normal maps store vectors and must be sampled as linear data.
            diffuseTextureId = File.Exists(diffuseFile) ? BuildTexture(diffuseFile, true) : BuildWhiteTexture();
            normalTextureId = File.Exists(normalFile) ? BuildTexture(normalFile, false) : BuildFlatNormalTexture();
            specularTextureId = File.Exists(specularFile) ? BuildTexture(specularFile, true) : BuildBlackTexture();
            UploadModel();
            modelDirty = false;
            FrameModel();
            Invalidate();
        }

        private void Motion(int x, int y, MouseButtons mb)
        {
            int diffx = x - lastx;
            int diffy = y - lasty;

            lastx = x;
            lasty = y;
            // Left  = rotate/orbit around target
            // Right = pan (translate target in screen space)
            // Wheel = zoom
            if (mb == MouseButtons.Left)
            {
                rotx += 0.45f * diffy;
                roty += 0.45f * diffx;
                rotx = MathHelper.Clamp(rotx, -89f, 89f);
            }
            else if (mb == MouseButtons.Right)
            {
                // Move the target in the camera's right/up plane so panning matches screen movement
                float panScale = MathF.Max(0.005f, cameraDistance * 0.0015f);
                Vector3 forward = OrbitDirection();
                Vector3 right = Vector3.Cross(Vector3.UnitZ, forward);
                right = right.LengthSquared < 0.0001f ? Vector3.UnitX : Vector3.Normalize(right);
                Vector3 camUp = Vector3.Normalize(Vector3.Cross(forward, right));
                panOffset -= right * (diffx * panScale) + camUp * (diffy * panScale);
            }

            Invalidate();
        }

        private void InitializeOpenGl()
        {
            GL.ClearColor(Color.FromArgb(255, 14, 14, 20));
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);

            // Texture rows are tightly packed RGBA8, but make the assumption explicit.
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            shaderProgram = CreateShaderProgram(VertexShaderSource, FragmentShaderSource);
            CacheUniformLocations();

            modelVao = GL.GenVertexArray();
            modelVbo = GL.GenBuffer();
            modelEbo = GL.GenBuffer();
            gridVao = GL.GenVertexArray();
            gridVbo = GL.GenBuffer();

            ConfigureModelVertexArray(modelVao, modelVbo, modelEbo);
            ConfigureGridVertexArray(gridVao, gridVbo);

            GL.BindBuffer(BufferTarget.ArrayBuffer, gridVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, gridVertices.Length * sizeof(float), gridVertices, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);

            GL.Viewport(0, 0, Width, Height);
        }

        private void CacheUniformLocations()
        {
            uModelLoc = GL.GetUniformLocation(shaderProgram, "uModel");
            uViewLoc = GL.GetUniformLocation(shaderProgram, "uView");
            uProjectionLoc = GL.GetUniformLocation(shaderProgram, "uProjection");
            uNormalMatrixLoc = GL.GetUniformLocation(shaderProgram, "uNormalMatrix");
            uLightPositionLoc = GL.GetUniformLocation(shaderProgram, "uLightPosition");
            uFillDirectionLoc = GL.GetUniformLocation(shaderProgram, "uFillDirection");
            uCameraPosLoc = GL.GetUniformLocation(shaderProgram, "uCameraPos");
            uNormalYFlipLoc = GL.GetUniformLocation(shaderProgram, "uNormalYFlip");
            uModeLoc = GL.GetUniformLocation(shaderProgram, "uMode");

            // Sampler units never change, so assign them once.
            GL.UseProgram(shaderProgram);
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uDiffuseTexture"), 0);
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uNormalTexture"), 1);
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uSpecularTexture"), 2);
            GL.UseProgram(0);
        }

        private void Render()
        {
            if (!TryMakeCurrent()) return;

            float dt = MathF.Min((float)clock.Elapsed.TotalSeconds, MaxFrameDelta);
            clock.Restart();

            if (MD5Model != null)
            {
                if (!IsPaused)
                {
                    MD5Model.Update(dt);
                    modelDirty = true;
                }
                if (modelDirty)
                {
                    UploadModel();
                    modelDirty = false;
                }
                FrameCallback?.Invoke(MD5Model.CurrentFrame, MD5Model.TotalFrames);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.UseProgram(shaderProgram);

            // Keep the near/far ratio bounded for depth precision.
            float farPlane = cameraDistance + modelRadius * 4f;
            float nearPlane = MathF.Max(0.05f, MathF.Min(modelRadius * 0.02f, farPlane / 10000f));
            float aspect = MathF.Max(1f, Width) / MathF.Max(1f, Height);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), aspect, nearPlane, farPlane);
            Matrix4 view = BuildViewMatrix();

            SetCommonUniforms(Matrix4.Identity, view, projection);

            DrawGrid();
            DrawModel();

            GL.BindVertexArray(0);
            GL.UseProgram(0);
            lastGlError = GL.GetError();
            SwapBuffers();
        }

        private void DrawGrid()
        {
            GL.Disable(EnableCap.CullFace);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
            GL.Uniform1(uModeLoc, 99);
            GL.BindVertexArray(gridVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, gridVertices.Length / 8);
            GL.Enable(EnableCap.CullFace);
        }

        private void DrawModel()
        {
            if (indexCount == 0) return;

            GL.BindVertexArray(modelVao);
            GL.FrontFace(flipWinding ? FrontFaceDirection.Cw : FrontFaceDirection.Ccw);

            switch (Phase)
            {
                case RenderPhase.Points:
                    GL.PointSize(3f);
                    GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Point);
                    break;
                case RenderPhase.Wireframe:
                    GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                    break;
                default:
                    GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                    break;
            }

            // Pass 1: opaque surfaces with depth writes and back-face culling.
            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, diffuseTextureId);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, normalTextureId);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, specularTextureId);
            GL.Uniform1(uModeLoc, (int)Phase);

            for (int m = 0; m < meshIndexCounts.Length; m++)
            {
                if (!meshIsEffect[m]) DrawMesh(m);
            }

            // Pass 2: Doom 3 effect materials (muzzle flashes, glows) are normally two-sided and
            // additive. Without their .mtr stages they get an emissive fallback, drawn after all
            // opaque geometry with depth testing on but depth writes off.
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            GL.DepthMask(false);
            GL.Uniform1(uModeLoc, 98);

            for (int m = 0; m < meshIndexCounts.Length; m++)
            {
                if (meshIsEffect[m]) DrawMesh(m);
            }

            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.FrontFace(FrontFaceDirection.Ccw);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private void DrawMesh(int mesh)
        {
            if (meshIndexCounts[mesh] == 0) return;
            GL.DrawElements(PrimitiveType.Triangles, meshIndexCounts[mesh], DrawElementsType.UnsignedInt, meshIndexOffsets[mesh] * sizeof(uint));
        }

        /// <summary>
        /// Without parsing Doom 3 .mtr files the blend stages of a material are unknown, so
        /// effect surfaces are recognised by the conventional naming of such materials.
        /// </summary>
        private static bool IsEffectMaterial(string shader)
        {
            if (string.IsNullOrEmpty(shader)) return false;
            string name = shader.Replace('\\', '/');
            name = name[(name.LastIndexOf('/') + 1)..];

            string[] markers = ["_fx", "fx_", "flash", "flare", "glow", "smoke", "fire", "spark", "beam"];
            foreach (string marker in markers)
            {
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void UploadModel()
        {
            if (MD5Model == null)
            {
                vertexCount = indexCount = 0;
                meshIndexOffsets = Array.Empty<int>();
                meshIndexCounts = Array.Empty<int>();
                meshIsEffect = Array.Empty<bool>();
                uploadedTopology = null;
                return;
            }

            // The topology is static for a model, so the index buffer is uploaded only once.
            if (!ReferenceEquals(uploadedTopology, MD5Model))
            {
                UploadIndices();
                uploadedTopology = MD5Model;
            }

            IReadOnlyList<Vertex[]> meshes = MD5Model.MeshVertices;
            int totalVertices = 0;
            for (int m = 0; m < meshes.Count; m++) totalVertices += meshes[m].Length;

            int floatCount = totalVertices * FloatsPerVertex;
            if (vertexUploadBuffer.Length != floatCount)
            {
                vertexUploadBuffer = new float[floatCount];
            }

            float[] data = vertexUploadBuffer;
            int o = 0;
            for (int m = 0; m < meshes.Count; m++)
            {
                Vertex[] vertices = meshes[m];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vertex v = vertices[i];
                    data[o++] = v.Position.X;  data[o++] = v.Position.Y;  data[o++] = v.Position.Z;
                    data[o++] = v.Normal.X;    data[o++] = v.Normal.Y;    data[o++] = v.Normal.Z;
                    data[o++] = v.Texture.X;   data[o++] = v.Texture.Y;
                    data[o++] = v.Tangent.X;   data[o++] = v.Tangent.Y;   data[o++] = v.Tangent.Z;
                    data[o++] = v.Bitangent.X; data[o++] = v.Bitangent.Y; data[o++] = v.Bitangent.Z;
                }
            }

            vertexCount = totalVertices;
            int dataBytes = floatCount * sizeof(float);
            GL.BindBuffer(BufferTarget.ArrayBuffer, modelVbo);
            if (dataBytes != modelBufferBytes)
            {
                GL.BufferData(BufferTarget.ArrayBuffer, dataBytes, data, BufferUsageHint.StreamDraw);
                modelBufferBytes = dataBytes;
            }
            else if (dataBytes > 0)
            {
                // Orphan the old storage first so the driver need not wait for the GPU to finish with it.
                GL.BufferData(BufferTarget.ArrayBuffer, dataBytes, IntPtr.Zero, BufferUsageHint.StreamDraw);
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, dataBytes, data);
            }
        }

        private void UploadIndices()
        {
            IReadOnlyList<Vertex[]> meshes = MD5Model.MeshVertices;
            IReadOnlyList<int[]> indices = MD5Model.MeshIndices;
            int meshCount = MD5Model.MeshCount;

            meshIndexOffsets = new int[meshCount];
            meshIndexCounts = new int[meshCount];
            meshIsEffect = new bool[meshCount];

            int total = 0;
            for (int m = 0; m < meshCount; m++) total += indices[m].Length;

            uint[] combined = new uint[total];
            int indexOffset = 0;
            int vertexOffset = 0;
            for (int m = 0; m < meshCount; m++)
            {
                int[] source = indices[m];
                for (int i = 0; i < source.Length; i++)
                {
                    combined[indexOffset + i] = (uint)(source[i] + vertexOffset);
                }

                meshIndexOffsets[m] = indexOffset;
                meshIndexCounts[m] = source.Length;
                meshIsEffect[m] = IsEffectMaterial(MD5Model.GetMeshShader(m));
                indexOffset += source.Length;
                vertexOffset += meshes[m].Length;
            }

            indexCount = total;
            GL.BindVertexArray(modelVao);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, modelEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, total * sizeof(uint), combined, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);
        }

        private void FrameModel()
        {
            if (MD5Model == null || !MD5Model.TryGetBounds(out Vector3 min, out Vector3 max))
            {
                return;
            }

            modelCenter = (min + max) * 0.5f;
            modelRadius = MathF.Max(1f, (max - min).Length * 0.5f);
            lightPosition = modelCenter + new Vector3(modelRadius * 0.65f, -modelRadius * 1.4f, modelRadius * 1.9f);
            ResetCamera();
        }

        private void ResetCamera()
        {
            panOffset = Vector3.Zero;
            rotx = 15f;
            roty = 25f;
            cameraDistance = MathF.Max(30f, modelRadius / MathF.Sin(MathHelper.DegreesToRadians(22.5f)) * 1.18f);
        }

        private void SetCommonUniforms(Matrix4 model, Matrix4 view, Matrix4 projection)
        {
            // Normal matrix: inverse-transpose of the model matrix's upper 3x3, computed once on the CPU.
            Matrix3 normalMatrix = Matrix3.Transpose(Matrix3.Invert(new Matrix3(model)));

            // Fill light: opposite the key light horizontally, lower and dimmer.
            Vector3 keyDirection = lightPosition - modelCenter;
            Vector3 fillDirection = new Vector3(-keyDirection.X, -keyDirection.Y, keyDirection.Z * 0.5f);
            fillDirection = fillDirection.LengthSquared > 1e-6f ? Vector3.Normalize(fillDirection) : Vector3.UnitZ;

            GL.UniformMatrix4(uModelLoc, false, ref model);
            GL.UniformMatrix4(uViewLoc, false, ref view);
            GL.UniformMatrix4(uProjectionLoc, false, ref projection);
            GL.UniformMatrix3(uNormalMatrixLoc, false, ref normalMatrix);
            GL.Uniform3(uLightPositionLoc, lightPosition);
            GL.Uniform3(uFillDirectionLoc, fillDirection);
            GL.Uniform3(uCameraPosLoc, cameraEye);
            GL.Uniform1(uNormalYFlipLoc, normalYFlip ? 1 : 0);
        }

        /// <summary>Unit vector from the orbit target to the camera.</summary>
        private Vector3 OrbitDirection()
        {
            float yaw = MathHelper.DegreesToRadians(roty);
            float pitch = MathHelper.DegreesToRadians(rotx);
            return new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Cos(pitch) * MathF.Sin(yaw),
                MathF.Sin(pitch));
        }

        private Matrix4 BuildViewMatrix()
        {
            Vector3 target = modelCenter + panOffset;
            cameraEye = target + OrbitDirection() * cameraDistance;
            return Matrix4.LookAt(cameraEye, target, Vector3.UnitZ);
        }

        private static void ConfigureModelVertexArray(int vao, int vbo, int ebo)
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            // The element buffer binding is part of the VAO state.
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);

            int stride = FloatsPerVertex * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, stride, 11 * sizeof(float));
        }

        private static void ConfigureGridVertexArray(int vao, int vbo)
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            int stride = 8 * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            // Attributes 3/4 (tangent frame) are unused by the grid; generic values are set
            // explicitly because disabled attributes read the current generic attribute.
            GL.DisableVertexAttribArray(3);
            GL.VertexAttrib3(3, 1f, 0f, 0f);
            GL.DisableVertexAttribArray(4);
            GL.VertexAttrib3(4, 0f, 1f, 0f);
        }

        private static int BuildTexture(string file, bool srgb)
        {
            if (Path.GetExtension(file).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                TgaImage tga = TgaImage.Load(file);
                return UploadTexture(tga.Width, tga.Height, PixelFormat.Rgba, tga.Rgba, srgb);
            }

            // Rows stay top-down, exactly like the TGA loader produces them: MD5 texture
            // coordinates use v = 0 for the top of the image, so no vertical flip is wanted.
            using Bitmap image = new Bitmap(file);
            Rectangle rect = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData bitmapdata = image.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                if (bitmapdata.Stride != image.Width * 4)
                {
                    throw new NotSupportedException($"Unexpected bitmap stride in '{file}'.");
                }
                return UploadTexture(image.Width, image.Height, PixelFormat.Bgra, bitmapdata.Scan0, srgb);
            }
            finally
            {
                image.UnlockBits(bitmapdata);
            }
        }

        private static int UploadTexture<T>(int width, int height, PixelFormat format, T pixels, bool srgb)
        {
            int texid = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texid);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            PixelInternalFormat internalFormat = srgb ? PixelInternalFormat.Srgb8Alpha8 : PixelInternalFormat.Rgba8;

            switch (pixels)
            {
                case byte[] bytes:
                    GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, format, PixelType.UnsignedByte, bytes);
                    break;
                case IntPtr pointer:
                    GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, format, PixelType.UnsignedByte, pointer);
                    break;
                default:
                    throw new ArgumentException("Unsupported pixel source.", nameof(pixels));
            }

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            return texid;
        }

        private static int BuildFlatNormalTexture() => BuildSolidTexture(new byte[] { 128, 128, 255, 255 });

        private static int BuildWhiteTexture() => BuildSolidTexture(new byte[] { 255, 255, 255, 255 });

        // A missing specular map means "no specular", not full-intensity specular.
        private static int BuildBlackTexture() => BuildSolidTexture(new byte[] { 0, 0, 0, 255 });

        private static int BuildSolidTexture(byte[] rgba)
        {
            int texid = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texid);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            return texid;
        }

        private static void DeleteTexture(ref int texture)
        {
            if (texture != 0)
            {
                GL.DeleteTexture(texture);
                texture = 0;
            }
        }

        private static int CreateShaderProgram(string vertexSource, string fragmentSource)
        {
            int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
            int fragmentShader = 0;
            int program = 0;
            try
            {
                fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);
                program = GL.CreateProgram();
                GL.AttachShader(program, vertexShader);
                GL.AttachShader(program, fragmentShader);
                GL.LinkProgram(program);

                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
                if (status == 0)
                {
                    string log = GL.GetProgramInfoLog(program);
                    throw new InvalidOperationException("OpenGL shader link failed: " + log);
                }

                GL.DetachShader(program, vertexShader);
                GL.DetachShader(program, fragmentShader);
                return program;
            }
            catch
            {
                if (program != 0) GL.DeleteProgram(program);
                throw;
            }
            finally
            {
                GL.DeleteShader(vertexShader);
                if (fragmentShader != 0) GL.DeleteShader(fragmentShader);
            }
        }

        private static int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                throw new InvalidOperationException(type + " compile failed: " + log);
            }

            return shader;
        }

        private static float[] BuildGridVertices()
        {
            List<float> data = new List<float>();
            for (int i = -100; i <= 100; i += 10)
            {
                AddLineVertex(data, i, -100, 0);
                AddLineVertex(data, i, 100, 0);
                AddLineVertex(data, -100, i, 0);
                AddLineVertex(data, 100, i, 0);
            }

            return data.ToArray();
        }

        private static void AddLineVertex(List<float> data, float x, float y, float z)
        {
            data.Add(x);
            data.Add(y);
            data.Add(z);
            data.Add(0);
            data.Add(0);
            data.Add(1);
            data.Add(0);
            data.Add(0);
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            if (MD5Model != null)
            {
                Invalidate();
            }
        }

        private const string VertexShaderSource = """
            #version 330 core

            layout (location = 0) in vec3 aPosition;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in vec2 aTexCoord;
            layout (location = 3) in vec3 aTangent;
            layout (location = 4) in vec3 aBitangent;

            uniform mat4 uModel;
            uniform mat3 uNormalMatrix;
            uniform mat4 uView;
            uniform mat4 uProjection;

            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec3 vTangent;
            out vec3 vBitangent;
            out vec2 vTexCoord;

            void main()
            {
                vec4 world = uModel * vec4(aPosition, 1.0);
                vWorldPos  = world.xyz;
                // Normals use the inverse-transpose; tangents follow the surface, so they use the model matrix.
                vNormal    = uNormalMatrix * aNormal;
                vTangent   = mat3(uModel) * aTangent;
                vBitangent = mat3(uModel) * aBitangent;
                vTexCoord  = aTexCoord;
                gl_Position = uProjection * uView * world;
            }
            """;

        private const string FragmentShaderSource = """
            #version 330 core

            in vec3 vWorldPos;
            in vec3 vNormal;
            in vec3 vTangent;
            in vec3 vBitangent;
            in vec2 vTexCoord;

            uniform sampler2D uDiffuseTexture;   // sRGB texture: sampled as linear
            uniform sampler2D uNormalTexture;    // linear tangent-space normal map
            uniform sampler2D uSpecularTexture;  // sRGB texture: sampled as linear
            uniform vec3      uLightPosition;
            uniform vec3      uFillDirection;
            uniform vec3      uCameraPos;
            uniform int       uMode;
            uniform int       uNormalYFlip;

            out vec4 FragColor;

            const float GLOSS          = 32.0;
            const float KEY_INTENSITY  = 1.0;
            const float FILL_INTENSITY = 0.20;
            const vec3  SKY_AMBIENT    = vec3(0.16, 0.17, 0.20);
            const vec3  GROUND_AMBIENT = vec3(0.07, 0.06, 0.05);

            // Exact sRGB transfer function (the default framebuffer is not sRGB-encoded).
            vec3 LinearToSrgb(vec3 c)
            {
                c = clamp(c, 0.0, 1.0);
                vec3 low  = c * 12.92;
                vec3 high = 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055;
                return mix(low, high, step(vec3(0.0031308), c));
            }

            // Orthonormal TBN (Gram-Schmidt) with handedness from the interpolated bitangent,
            // so mirrored UV islands are lit correctly.
            mat3 TangentFrame(vec3 N)
            {
                vec3 T = vTangent - N * dot(N, vTangent);
                T = dot(T, T) > 1e-12 ? normalize(T) : normalize(cross(N, abs(N.z) < 0.9 ? vec3(0, 0, 1) : vec3(1, 0, 0)));
                float handedness = dot(cross(N, T), vBitangent) < 0.0 ? -1.0 : 1.0;
                return mat3(T, cross(N, T) * handedness, N);
            }

            vec3 DecodeNormal(vec3 encoded)
            {
                vec3 n = encoded * 2.0 - 1.0;
                if (uNormalYFlip == 1) n.y = -n.y;
                return dot(n, n) > 1e-8 ? normalize(n) : vec3(0.0, 0.0, 1.0);
            }

            // Energy-normalised Blinn-Phong for a light of unit irradiance. Diffuse is
            // albedo * N.L (the BRDF's 1/pi is folded into the light intensity). Doom 3
            // specular maps encode absolute specular intensity, so no Fresnel term is added.
            vec3 DirectLight(vec3 N, vec3 Ngeom, vec3 L, vec3 V, vec3 albedo, vec3 specColor, float intensity)
            {
                float NdotL = dot(N, L);
                // A bumped normal must not receive light where the actual surface faces away from it.
                float selfShadow = clamp(dot(Ngeom, L) * 4.0, 0.0, 1.0);
                if (NdotL <= 0.0 || selfShadow <= 0.0) return vec3(0.0);

                vec3  H      = normalize(L + V);
                float NdotH  = max(dot(N, H), 0.0);
                float normalization = (GLOSS + 8.0) / 8.0;
                vec3  spec   = specColor * normalization * pow(NdotH, GLOSS);

                return (albedo + spec) * NdotL * selfShadow * intensity;
            }

            vec3 Shade(vec3 N, vec3 Ngeom, vec3 V, vec3 albedo, vec3 specColor)
            {
                vec3 L = normalize(uLightPosition - vWorldPos);
                vec3 color = DirectLight(N, Ngeom, L, V, albedo, specColor, KEY_INTENSITY);
                color += albedo * max(dot(N, uFillDirection), 0.0) * FILL_INTENSITY;
                // Hemispherical ambient (Z is up in Doom 3 space).
                color += albedo * mix(GROUND_AMBIENT, SKY_AMBIENT, N.z * 0.5 + 0.5);
                return color;
            }

            void main()
            {
                if (uMode == 99) { FragColor = vec4(0.28, 0.30, 0.33, 1.0); return; }             // grid
                if (uMode == 98) { FragColor = vec4(0.65, 0.16, 0.025, 0.42); return; }           // effect fallback
                if (uMode == 1)  { FragColor = vec4(1.0, 0.85, 0.25, 1.0); return; }              // points
                if (uMode == 2)  { FragColor = vec4(0.10, 0.85, 1.0, 1.0); return; }              // wireframe

                vec3 Ngeom = normalize(vNormal);
                // Back faces (visible only with culling disabled or flipped winding) need a flipped normal.
                if (!gl_FrontFacing) Ngeom = -Ngeom;

                vec3 V = normalize(uCameraPos - vWorldPos);

                vec3 albedo    = texture(uDiffuseTexture,  vTexCoord).rgb;
                vec3 specColor = texture(uSpecularTexture, vTexCoord).rgb;
                mat3 TBN       = TangentFrame(Ngeom);
                vec3 bumpN     = normalize(TBN * DecodeNormal(texture(uNormalTexture, vTexCoord).rgb));

                vec3 color;
                if (uMode == 0)       // FullRender: diffuse + normal map + specular
                {
                    color = LinearToSrgb(Shade(bumpN, Ngeom, V, albedo, specColor));
                }
                else if (uMode == 3)  // Texture: raw diffuse
                {
                    color = LinearToSrgb(albedo);
                }
                else if (uMode == 4)  // Light: same shading with geometry normals only
                {
                    color = LinearToSrgb(Shade(Ngeom, Ngeom, V, albedo, specColor));
                }
                else if (uMode == 5)  // NormalMap: world-space bumped normal
                {
                    color = bumpN * 0.5 + 0.5;
                }
                else if (uMode == 6)  // SpecularMap: raw specular texture
                {
                    color = LinearToSrgb(specColor);
                }
                else if (uMode == 7)  // BumpMap: grazing-light relief diagnostic
                {
                    vec3  grazL  = normalize(vec3(-0.80, -0.20, 0.18));
                    float baseD  = max(dot(Ngeom, grazL), 0.0);
                    float bumpD  = max(dot(bumpN, grazL), 0.0);
                    float relief = clamp((bumpD - baseD) * 3.2 + 0.5, 0.0, 1.0);
                    color = LinearToSrgb(albedo * (0.15 + relief * 1.40));
                }
                else if (uMode == 8)  // DiagnosticTBN: tangent (R), bitangent (G), normal (B) facing the viewer
                {
                    color = vec3(abs(dot(TBN[0], V)), abs(dot(TBN[1], V)), abs(dot(TBN[2], V)));
                    // Mirrored UV islands (left-handed frames) are shown darker.
                    if (dot(cross(TBN[2], TBN[0]), TBN[1]) < 0.0) color *= 0.45;
                }
                else
                {
                    color = vec3(1.0, 0.0, 1.0);
                }

                FragColor = vec4(color, 1.0);
            }
            """;
    }
}

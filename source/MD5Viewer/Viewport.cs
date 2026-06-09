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
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly float[] gridVertices;

        private Vector3 modelCenter;
        private Vector3 panOffset;
        private Vector3 lightPosition = new Vector3(80, -160, 180);
        private Vector3 cameraEye;
        private float modelRadius = 80f;
        private float rotx = -12f;
        private float roty = 25f;
        private float cameraDistance = 180f;
        private int lastx;
        private int lasty;

        private int shaderProgram;
        private int modelVao;
        private int modelVbo;
        private int gridVao;
        private int gridVbo;
        private int diffuseTextureId;
        private int normalTextureId;
        private int specularTextureId;
        private int modelBufferBytes;
        private int   vertexCount;
        private int[] meshVertexOffsets = [];
        private int[] meshVertexCounts  = [];
        private string[] meshShaders    = [];
        private bool glReady;
        private bool modelDirty;
        private bool normalYFlip = false;
        private bool flipWinding = false;

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
                if (gridVbo != 0) GL.DeleteBuffer(gridVbo);
                if (modelVao != 0) GL.DeleteVertexArray(modelVao);
                if (gridVao != 0) GL.DeleteVertexArray(gridVao);
                if (shaderProgram != 0) GL.DeleteProgram(shaderProgram);
            }

            modelVbo = gridVbo = modelVao = gridVao = shaderProgram = 0;
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

        // Expose properties so UI can read/set states directly
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

            if (!glReady)
            {
                return;
            }

            if (!TryMakeCurrent()) return;
            DeleteTexture(ref diffuseTextureId);
            DeleteTexture(ref normalTextureId);
            DeleteTexture(ref specularTextureId);
            diffuseTextureId = BuildTexture(diffuseFile, true);
            normalTextureId = File.Exists(normalFile) ? BuildTexture(normalFile, false) : BuildFlatNormalTexture();
            specularTextureId = File.Exists(specularFile) ? BuildTexture(specularFile, false) : BuildWhiteTexture();
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
            // Controls mapping now:
            // Left  = rotate/orbit around target
            // Right = pan (translate target in screen space)
            // Wheel = zoom
            if (mb == MouseButtons.Left)
            {
                // Orbit: change pitch (rotx) and yaw (roty)
                rotx += 0.45f * diffy;
                roty += 0.45f * diffx;
                rotx = MathHelper.Clamp(rotx, -89f, 89f);
            }
            else if (mb == MouseButtons.Right)
            {
                // Pan: move the model target in camera's right/up plane so panning matches screen movement
                float panScale = MathF.Max(0.005f, cameraDistance * 0.0015f);
                float yaw = MathHelper.DegreesToRadians(roty);
                float pitch = MathHelper.DegreesToRadians(rotx);
                Vector3 forward = new Vector3(
                    MathF.Cos(pitch) * MathF.Cos(yaw),
                    MathF.Cos(pitch) * MathF.Sin(yaw),
                    MathF.Sin(pitch));
                Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, forward));
                if (right.LengthSquared < 0.0001f) right = Vector3.UnitX;
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

            shaderProgram = CreateShaderProgram(VertexShaderSource, FragmentShaderSource);
            modelVao = GL.GenVertexArray();
            modelVbo = GL.GenBuffer();
            gridVao = GL.GenVertexArray();
            gridVbo = GL.GenBuffer();

            ConfigureModelVertexArray(modelVao, modelVbo);
            ConfigureGridVertexArray(gridVao, gridVbo);

            GL.BindBuffer(BufferTarget.ArrayBuffer, gridVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, gridVertices.Length * sizeof(float), gridVertices, BufferUsageHint.StaticDraw);

            GL.Viewport(0, 0, Width, Height);
        }

        private void Render()
        {
            if (!TryMakeCurrent()) return;

            float dt = (float)clock.Elapsed.TotalSeconds;
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

            float nearPlane = MathF.Max(0.05f, modelRadius / 500f);
            float farPlane = MathF.Max(1000f, cameraDistance + modelRadius * 8f);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), MathF.Max(1f, Width) / MathF.Max(1f, Height), nearPlane, farPlane);
            Matrix4 view = BuildViewMatrix();

            SetCommonUniforms(Matrix4.Identity, view, projection);

            DrawGrid();
            DrawModel();

            GL.BindVertexArray(0);
            GL.UseProgram(0);
            SwapBuffers();
        }

        private void DrawGrid()
        {
            GL.Disable(EnableCap.CullFace);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uMode"), 99);
            GL.BindVertexArray(gridVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, gridVertices.Length / 8);
            GL.Enable(EnableCap.CullFace);
        }

        private void DrawModel()
        {
            if (vertexCount == 0) return;

            GL.BindVertexArray(modelVao);
            GL.FrontFace(flipWinding ? FrontFaceDirection.Cw : FrontFaceDirection.Ccw);

            // Set polygon mode once
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

            int uDiffuse  = GL.GetUniformLocation(shaderProgram, "uDiffuseTexture");
            int uNormal   = GL.GetUniformLocation(shaderProgram, "uNormalTexture");
            int uSpecular = GL.GetUniformLocation(shaderProgram, "uSpecularTexture");
            int uMode     = GL.GetUniformLocation(shaderProgram, "uMode");

            for (int m = 0; m < meshVertexCounts.Length; m++)
            {
                if (meshVertexCounts[m] == 0) continue;

                string shader = meshShaders.Length > m ? meshShaders[m] : "";
                bool isMainBody = shader.Contains("cyberdemon", StringComparison.OrdinalIgnoreCase);

                if (isMainBody)
                {
                    GL.Enable(EnableCap.CullFace);
                    GL.Disable(EnableCap.Blend);
                    GL.DepthMask(true);
                    // Full PBR rendering with all textures
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, diffuseTextureId);
                    GL.Uniform1(uDiffuse, 0);
                    GL.ActiveTexture(TextureUnit.Texture1);
                    GL.BindTexture(TextureTarget.Texture2D, normalTextureId);
                    GL.Uniform1(uNormal, 1);
                    GL.ActiveTexture(TextureUnit.Texture2);
                    GL.BindTexture(TextureTarget.Texture2D, specularTextureId);
                    GL.Uniform1(uSpecular, 2);
                    GL.Uniform1(uMode, (int)Phase);
                }
                else
                {
                    // Effect materials are commonly two-sided/additive in Doom 3.
                    // Without their .mtr stages, use an emissive fallback and avoid
                    // incorrectly culling billboard-like effect geometry.
                    GL.Disable(EnableCap.CullFace);
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                    GL.DepthMask(false);
                    GL.Uniform1(uMode, 98);
                }

                GL.DrawArrays(PrimitiveType.Triangles, meshVertexOffsets[m], meshVertexCounts[m]);
            }

            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.FrontFace(FrontFaceDirection.Ccw);
        }

        private void UploadModel()
        {
            if (MD5Model == null)
            {
                vertexCount = 0;
                meshVertexOffsets = Array.Empty<int>();
                meshVertexCounts  = Array.Empty<int>();
                meshShaders       = Array.Empty<string>();
                return;
            }

            // Build per-mesh offset/count/shader tables
            int meshCount = MD5Model.MeshCount;
            meshVertexOffsets = new int[meshCount];
            meshVertexCounts  = new int[meshCount];
            meshShaders       = new string[meshCount];

            Vertex[] vertices = MD5Model.GetCombinedVertices();
            int offset = 0;
            for (int m = 0; m < meshCount; m++)
            {
                int count = MD5Model.Meshes[m].Length;
                meshVertexOffsets[m] = offset;
                meshVertexCounts[m]  = count;
                meshShaders[m]       = MD5Model.GetMeshShader(m);
                offset += count;
            }

            float[] data = new float[vertices.Length * 14];
            for (int i = 0; i < vertices.Length; i++)
            {
                int o = i * 14;
                data[o + 0] = vertices[i].Position.X;
                data[o + 1] = vertices[i].Position.Y;
                data[o + 2] = vertices[i].Position.Z;
                data[o + 3] = vertices[i].Normal.X;
                data[o + 4] = vertices[i].Normal.Y;
                data[o + 5] = vertices[i].Normal.Z;
                data[o + 6] = vertices[i].Texture.X;
                data[o + 7] = vertices[i].Texture.Y;
                data[o + 8] = vertices[i].Tangent.X;
                data[o + 9] = vertices[i].Tangent.Y;
                data[o + 10] = vertices[i].Tangent.Z;
                data[o + 11] = vertices[i].Bitangent.X;
                data[o + 12] = vertices[i].Bitangent.Y;
                data[o + 13] = vertices[i].Bitangent.Z;
            }

            vertexCount  = vertices.Length;
            GL.BindBuffer(BufferTarget.ArrayBuffer, modelVbo);
            int dataBytes = data.Length * sizeof(float);
            if (dataBytes != modelBufferBytes)
            {
                GL.BufferData(BufferTarget.ArrayBuffer, dataBytes, data, BufferUsageHint.DynamicDraw);
                modelBufferBytes = dataBytes;
            }
            else if (dataBytes > 0)
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, dataBytes, data);
            }
        }

        private void FrameModel()
        {
            if (MD5Model == null)
            {
                return;
            }

            Vertex[] vertices = MD5Model.GetCombinedVertices();
            if (vertices.Length == 0)
            {
                return;
            }

            Vector3 min = vertices[0].Position;
            Vector3 max = vertices[0].Position;
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector3.ComponentMin(min, vertices[i].Position);
                max = Vector3.ComponentMax(max, vertices[i].Position);
            }

            modelCenter = (min + max) * 0.5f;
            modelRadius = MathF.Max(1f, (max - min).Length * 0.5f);
            lightPosition = modelCenter + new Vector3(modelRadius * 0.65f, -modelRadius * 1.4f, modelRadius * 1.9f);
            ResetCamera();
        }

        private void ResetCamera()
        {
            panOffset = Vector3.Zero;
            rotx = -12f;
            roty = 25f;
            cameraDistance = MathF.Max(30f, modelRadius / MathF.Sin(MathHelper.DegreesToRadians(22.5f)) * 1.18f);
        }

        private void SetCommonUniforms(Matrix4 model, Matrix4 view, Matrix4 projection)
        {
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uModel"),      false, ref model);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uView"),       false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uProjection"), false, ref projection);
            GL.Uniform3(GL.GetUniformLocation(shaderProgram, "uLightPosition"), lightPosition);
            GL.Uniform3(GL.GetUniformLocation(shaderProgram, "uCameraPos"),    cameraEye);
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uNormalYFlip"), normalYFlip ? 1 : 0);
        }

        private Matrix4 BuildViewMatrix()
        {
            float yaw   = MathHelper.DegreesToRadians(roty);
            float pitch = MathHelper.DegreesToRadians(rotx);
            Vector3 target    = modelCenter + panOffset;
            Vector3 direction = new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Cos(pitch) * MathF.Sin(yaw),
                MathF.Sin(pitch));
            cameraEye = target + direction * cameraDistance;
            return Matrix4.LookAt(cameraEye, target, Vector3.UnitZ);
        }

        private static void ConfigureModelVertexArray(int vao, int vbo)
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            int stride = 14 * sizeof(float);
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
            GL.DisableVertexAttribArray(3);
            GL.VertexAttrib3(3, 1f, 0f, 0f);
            GL.DisableVertexAttribArray(4);
            GL.VertexAttrib3(4, 0f, 1f, 0f);
        }

        private static int BuildTexture(string file, bool srgb)
        {
            if (Path.GetExtension(file).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                return BuildTgaTexture(file, srgb);
            }

            using Bitmap image = new Bitmap(file);
            image.RotateFlip(RotateFlipType.RotateNoneFlipY);

            Rectangle rect = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData bitmapdata = image.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int texid = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texid);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexImage2D(TextureTarget.Texture2D, 0, srgb ? PixelInternalFormat.Srgb8Alpha8 : PixelInternalFormat.Rgba8, image.Width, image.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmapdata.Scan0);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            image.UnlockBits(bitmapdata);
            return texid;
        }

        private static int BuildTgaTexture(string file, bool srgb)
        {
            TgaImage image = TgaImage.Load(file);
            int texid = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texid);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexImage2D(TextureTarget.Texture2D, 0, srgb ? PixelInternalFormat.Srgb8Alpha8 : PixelInternalFormat.Rgba8, image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Rgba);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            return texid;
        }

        private static int BuildFlatNormalTexture()
        {
            return BuildSolidTexture(new byte[] { 128, 128, 255, 255 });
        }

        private static int BuildWhiteTexture()
        {
            return BuildSolidTexture(new byte[] { 255, 255, 255, 255 });
        }

        private static int BuildSolidTexture(byte[] rgba)
        {
            int texid = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texid);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
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
            int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);
            int program = GL.CreateProgram();
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
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            return program;
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
            uniform mat4 uView;
            uniform mat4 uProjection;

            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec3 vTangent;
            out vec3 vBitangent;
            out vec2 vTexCoord;

            void main()
            {
                vec4 world    = uModel * vec4(aPosition, 1.0);
                mat3 normalMat = mat3(transpose(inverse(uModel)));
                vWorldPos  = world.xyz;
                vNormal    = normalize(normalMat * aNormal);
                vTangent   = normalize(mat3(uModel) * aTangent);
                vBitangent = normalize(mat3(uModel) * aBitangent);
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

            uniform sampler2D uDiffuseTexture;
            uniform sampler2D uNormalTexture;
            uniform sampler2D uSpecularTexture;
            uniform vec3      uLightPosition;
            uniform vec3      uCameraPos;
            uniform int       uMode;
            uniform int       uNormalYFlip;

            out vec4 FragColor;

            // Reconstruct a proper orthonormal TBN and return the world-space bump normal.
            vec3 BumpNormal(vec3 N, vec3 rawTangentNormal)
            {
                vec3 T = normalize(vTangent - N * dot(N, vTangent));
                vec3 B = normalize(cross(N, T));
                vec3 storedB = vBitangent;
                // Ensure handedness matches the stored bitangent.
                if (dot(B, storedB) < 0.0) B = -B;
                return normalize(mat3(T, B, N) * rawTangentNormal);
            }

            // Blinn-Phong specular (physically more correct highlight shape than Phong).
            float Specular(vec3 N, vec3 L, vec3 V, float gloss)
            {
                if (dot(N, L) <= 0.0) return 0.0;
                vec3 H = normalize(L + V);
                return pow(max(dot(N, H), 0.0), gloss);
            }

            vec3 LinearToGamma(vec3 c) { return pow(clamp(c, 0.0, 1.0), vec3(1.0 / 2.2)); }

            void main()
            {
                // --- geometry normals ---
                vec3 N = normalize(vNormal);

                // --- lighting vectors (all in world space) ---
                vec3 L = normalize(uLightPosition - vWorldPos);
                vec3 V = normalize(uCameraPos     - vWorldPos);   // correct: camera to fragment

                // --- textures ---
                vec3 diffuseLin   = texture(uDiffuseTexture,  vTexCoord).rgb;
                vec3 normalSample = texture(uNormalTexture,   vTexCoord).rgb;
                vec3 specMask     = texture(uSpecularTexture, vTexCoord).rgb;

                // Decode normal map.
                vec3 tangentN;
                tangentN.xy = (normalSample.xy * 2.0 - 1.0) * 1.15;  // slight boost for crispness
                if (uNormalYFlip == 1)
                {
                    tangentN.y  = -tangentN.y;
                }
                tangentN.z  = normalSample.z * 2.0 - 1.0;
                tangentN    = normalize(tangentN);

                // --- normals ---
                vec3 bumpN = BumpNormal(N, tangentN);

                // --- secondary lights ---
                vec3 fillL = normalize(vec3(-L.x, -L.y, L.z) * vec3(1.0, 1.0, 0.5)); // wrap-around fill
                vec3 rimL  = -L;                                                        // rim from opposite side

                // ---------------------------------------------------------------
                // FullRender (0): diffuse + normal map + specular, all assets used
                // ---------------------------------------------------------------
                if (uMode == 0)
                {
                    float kD      = max(dot(bumpN, L),     0.0);
                    float kFill   = max(dot(bumpN, fillL), 0.0) * 0.20;
                    float kRim    = pow(1.0 - max(dot(bumpN, V), 0.0), 3.0) * 0.28;
                    float kSpec   = Specular(bumpN, L, V, 64.0) * 0.85;
                    float ambient = 0.15;  // lower = bump contrast more visible
                    vec3 lit = diffuseLin * (ambient + kD * 1.10 + kFill + kRim) + specMask * kSpec;
                    FragColor = vec4(LinearToGamma(lit), 1.0);
                }
                // ---------------------------------------------------------------
                // Points (1) / Wireframe (2): flat colour overlays
                // ---------------------------------------------------------------
                else if (uMode == 1)
                {
                    FragColor = vec4(1.0, 0.85, 0.25, 1.0);
                }
                else if (uMode == 2)
                {
                    FragColor = vec4(0.10, 0.85, 1.0, 1.0);
                }
                // ---------------------------------------------------------------
                // Texture (3): raw diffuse
                // ---------------------------------------------------------------
                else if (uMode == 3)
                {
                    FragColor = vec4(LinearToGamma(diffuseLin), 1.0);
                }
                // ---------------------------------------------------------------
                // Light (4): full Blinn-Phong with geometry normals only (no bump)
                // ---------------------------------------------------------------
                else if (uMode == 4)
                {
                    float kD      = max(dot(N, L),     0.0);
                    float kFill   = max(dot(N, fillL), 0.0) * 0.22;
                    float kRim    = pow(1.0 - max(dot(N, V), 0.0), 3.0) * 0.18;
                    float kSpec   = Specular(N, L, V, 96.0) * 0.70;
                    float ambient = 0.30;
                    vec3 lit = diffuseLin * (ambient + kD * 0.90 + kFill + kRim) + specMask * kSpec;
                    FragColor = vec4(LinearToGamma(lit), 1.0);
                }
                // ---------------------------------------------------------------
                // NormalMap (5): show decoded world-space bump direction
                // ---------------------------------------------------------------
                else if (uMode == 5)
                {
                    FragColor = vec4(bumpN * 0.5 + 0.5, 1.0);
                }
                // ---------------------------------------------------------------
                // SpecularMap (6): raw specular texture
                // ---------------------------------------------------------------
                else if (uMode == 6)
                {
                    FragColor = vec4(specMask, 1.0);
                }
                // ---------------------------------------------------------------
                // BumpMap (7): grazing diagnostic - shows relief / bump contrast
                // ---------------------------------------------------------------
                else if (uMode == 7)
                {
                    // Use a low-angle grazing light to make bump detail pop.
                    vec3 grazL    = normalize(vec3(-0.80, -0.20, 0.18));
                    float baseD   = max(dot(N,     grazL), 0.0);
                    float bumpD   = max(dot(bumpN, grazL), 0.0);
                    float relief  = clamp((bumpD - baseD) * 3.2 + 0.5, 0.0, 1.0);
                    float kSpec   = Specular(bumpN, grazL, V, 48.0) * 1.20;
                    vec3 lit      = diffuseLin * (0.15 + relief * 1.40) + specMask * kSpec;
                    FragColor     = vec4(LinearToGamma(lit), 1.0);
                }
                // ---------------------------------------------------------------
                // DiagnosticTBN (8): T=red, B=green, N=blue in world space
                // ---------------------------------------------------------------
                else if (uMode == 8)
                {
                    vec3 T  = normalize(vTangent);
                    vec3 B  = normalize(vBitangent);
                    vec3 Nw = normalize(vNormal);
                    FragColor = vec4(abs(T) * 0.5 + vec3(abs(B.x), abs(B.y), abs(Nw.z)) * 0.5, 1.0);
                }
                // ---------------------------------------------------------------
                // Missing Doom 3 effect material fallback (98)
                // ---------------------------------------------------------------
                else if (uMode == 98)
                {
                    FragColor = vec4(0.65, 0.16, 0.025, 0.42);
                }
                // ---------------------------------------------------------------
                // Grid / fallback (99)
                // ---------------------------------------------------------------
                else
                {
                    FragColor = vec4(0.28, 0.30, 0.33, 1.0);
                }
            }
            """;
    }
}

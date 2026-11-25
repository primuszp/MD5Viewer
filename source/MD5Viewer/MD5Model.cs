using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OpenTK.Mathematics;

namespace MD5Viewer
{
    public class MD5Model
    {
        private struct TextBuffer
        {
            public string String;
            public int[] Ints;
            public float[] Floats;
        }

        private struct MD5Vertex
        {
            /// <summary>Index of the first weight of this vertex.</summary>
            public int StartWeight;

            /// <summary>Number of consecutive weights (starting at StartWeight) that influence this vertex.</summary>
            public int CountWeight;

            /// <summary>Texture coordinates (Doom 3 convention: v = 0 is the top of the image).</summary>
            public Vector2 UV;
        }

        private struct MD5Weight
        {
            /// <summary>Joint this weight is attached to.</summary>
            public int Joint;

            /// <summary>Contribution of this weight, in [0, 1]. The biases of one vertex sum to 1.</summary>
            public float Bias;

            /// <summary>Weight position in the joint's local space.</summary>
            public Vector3 Pos;

            // Bind-pose tangent frame expressed in the joint's local space, so it can be
            // skinned with the same joint rotations as the position.
            public Vector3 Normal;
            public Vector3 Tangent;
            public Vector3 Bitangent;
        }

        /// <summary>A joint in model (object) space.</summary>
        private struct MD5Joint
        {
            public string Name;
            public int Parent;
            public Vector3 Pos;
            public Quaternion Orient;
        }

        /// <summary>A joint relative to its parent (the space MD5 animation data is stored in).</summary>
        private struct LocalJoint
        {
            public Vector3 Pos;
            public Quaternion Orient;
        }

        private struct MD5Mesh
        {
            /// <summary>Material name (defined in the Doom 3 .mtr files).</summary>
            public string Shader;
            public int NumVert;
            public int NumTris;
            public int NumWeights;
            public int[][] Faces;
            public MD5Vertex[] Verts;
            public MD5Weight[] Weights;
        }

        private sealed class Animation
        {
            public string AnimName;
            public int NumFrames;
            public int NumJoints;
            public int FrameRate;

            /// <summary>Parent index per joint (from the animation hierarchy).</summary>
            public int[] Parents;

            /// <summary>Per-frame joints in parent-relative space: [frame, joint].</summary>
            public LocalJoint[,] Frames;
            public MD5BoundingBox[] Boxes;

            /// <summary>Playback time in seconds, kept in [0, Duration).</summary>
            public float Time;

            /// <summary>
            /// Number of frame-to-frame intervals in one cycle. Doom 3 exports looping
            /// animations with the last frame equal to the first one, so a cycle covers
            /// frames 0 .. NumFrames-1 and wraps without blending the last frame into frame 0
            /// (see idMD5Anim::ConvertTimeToFrame).
            /// </summary>
            public int CycleFrames => Math.Max(1, NumFrames - 1);

            public float Duration => (float)CycleFrames / FrameRate;
        }

        private struct MD5BoundingBox
        {
            public Vector3 Min;
            public Vector3 Max;
        }

        private struct MD5JointInfo
        {
            public string Name;
            public int Parent;
            public int Flags;
            public int StartIndex;
        }

        // Format version (Doom 3 uses 10)
        private int versionNr;
        private int numMeshes;
        private int numJoints;

        /// <summary>The skeleton stored in the .md5mesh file (bind pose), in model space.</summary>
        private MD5Joint[] bPSkeleton;

        /// <summary>The current animated skeleton, in model space.</summary>
        private MD5Joint[] skeleton;

        private readonly List<Animation> animations = new List<Animation>();
        private Animation curAnim;

        private MD5Mesh[] model;

        // Skinned output. Vertex arrays are allocated once and overwritten every update;
        // index arrays are static because the topology never changes.
        private readonly List<Vertex[]> meshVertices = new List<Vertex[]>();
        private readonly List<int[]> meshIndices = new List<int[]>();

        private bool animated;

        /// <summary>Skinned vertices of each mesh (one entry per MD5 vertex).</summary>
        public IReadOnlyList<Vertex[]> MeshVertices => meshVertices;

        /// <summary>Triangle indices of each mesh, referencing <see cref="MeshVertices"/> (counter-clockwise front faces).</summary>
        public IReadOnlyList<int[]> MeshIndices => meshIndices;

        public bool HasAnimation => animated;

        public int MeshCount => meshVertices.Count;

        /// <summary>
        /// When enabled (default), the translation of the root joint is held at its first-frame
        /// value, so animations with root motion (e.g. walk cycles) play in place instead of
        /// sliding away and snapping back at every loop. Doom 3 extracts this motion and
        /// applies it to the entity instead of the mesh.
        /// </summary>
        public bool LockRootMotion { get; set; } = true;

        public string GetMeshShader(int index) =>
            (index >= 0 && index < numMeshes && model != null) ? model[index].Shader ?? "" : "";

        public void Load(string filename)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filename);

            animations.Clear();
            curAnim = null;
            animated = false;
            skeleton = null;
            bPSkeleton = null;
            model = null;
            versionNr = numJoints = numMeshes = 0;
            meshVertices.Clear();
            meshIndices.Clear();

            int meshIndex = 0;
            int jointsRead = 0;
            TextBuffer buffer = new TextBuffer();

            using (StreamReader file = new StreamReader(filename))
            {
                string line;
                while ((line = file.ReadLine()) != null)
                {
                    CleanString(ref line);
                    if (line.Length == 0) continue;

                    if (ParseLine(ref buffer, line, "MD5Version %i"))
                    {
                        versionNr = buffer.Ints[0];
                        continue;
                    }

                    if (ParseLine(ref buffer, line, "numJoints %i"))
                    {
                        numJoints = buffer.Ints[0];
                        if (numJoints <= 0) throw new InvalidDataException($"Invalid joint count in '{filename}'.");
                        bPSkeleton = new MD5Joint[numJoints];
                        continue;
                    }

                    if (ParseLine(ref buffer, line, "numMeshes %i"))
                    {
                        numMeshes = buffer.Ints[0];
                        if (numMeshes <= 0) throw new InvalidDataException($"Invalid mesh count in '{filename}'.");
                        model = new MD5Mesh[numMeshes];
                        continue;
                    }

                    if (line == "joints {")
                    {
                        if (bPSkeleton == null) throw new InvalidDataException($"'joints' block before 'numJoints' in '{filename}'.");

                        for (int i = 0; i < numJoints; i++)
                        {
                            line = ReadRequiredLine(file, "joint");
                            CleanString(ref line);

                            RequireParsed(ref buffer, line, "%s %i ( %f %f %f ) ( %f %f %f )", filename);
                            int parent = buffer.Ints[0];
                            if (parent < -1 || parent >= i)
                            {
                                throw new InvalidDataException($"Joint {i} has invalid parent {parent} in '{filename}'.");
                            }

                            bPSkeleton[i].Name = buffer.String;
                            bPSkeleton[i].Parent = parent;
                            bPSkeleton[i].Pos = new Vector3(buffer.Floats[0], buffer.Floats[1], buffer.Floats[2]);
                            bPSkeleton[i].Orient = new Quaternion(buffer.Floats[3], buffer.Floats[4], buffer.Floats[5], 1);
                            MyMath.ComputeWComponent(ref bPSkeleton[i].Orient);
                        }
                        jointsRead = numJoints;
                        continue;
                    }

                    if (line == "mesh {")
                    {
                        if (model == null || meshIndex >= numMeshes)
                        {
                            throw new InvalidDataException($"More mesh blocks than declared by 'numMeshes' in '{filename}'.");
                        }

                        ParseMeshBlock(file, ref model[meshIndex], ref buffer, filename);
                        meshIndex++;
                    }
                }
            }

            if (versionNr != 10 || numJoints <= 0 || jointsRead != numJoints || numMeshes <= 0 || meshIndex != numMeshes)
            {
                throw new InvalidDataException($"Invalid or unsupported MD5 mesh '{filename}'.");
            }

            for (int k = 0; k < numMeshes; k++)
            {
                ValidateMesh(model[k], k, filename);
            }

            InitializeBindPoseBasis();
            BuildOutputBuffers();

            skeleton = bPSkeleton;
            ComputingVertexPositions(bPSkeleton);
        }

        private void ParseMeshBlock(StreamReader file, ref MD5Mesh mesh, ref TextBuffer buffer, string filename)
        {
            while (true)
            {
                string line = ReadRequiredLine(file, "mesh");
                CleanString(ref line);
                if (line == "}") break;
                if (line.Length == 0) continue;

                if (line.StartsWith("shader", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the original spelling of the material name (ParseLine lower-cases).
                    mesh.Shader = line.Length > 6 ? line[6..].Trim() : "";
                    continue;
                }

                if (ParseLine(ref buffer, line, "numverts %i"))
                {
                    mesh.NumVert = RequireCount(buffer.Ints[0], "numverts", filename);
                    mesh.Verts = new MD5Vertex[mesh.NumVert];

                    for (int i = 0; i < mesh.NumVert; i++)
                    {
                        line = ReadRequiredLine(file, "vertex");
                        RequireParsed(ref buffer, line, "vert %i ( %f %f ) %i %i", filename);
                        int index = RequireIndex(buffer.Ints[0], mesh.NumVert, "vert", filename);
                        mesh.Verts[index].UV = new Vector2(buffer.Floats[0], buffer.Floats[1]);
                        mesh.Verts[index].StartWeight = buffer.Ints[1];
                        mesh.Verts[index].CountWeight = buffer.Ints[2];
                    }
                }
                else if (ParseLine(ref buffer, line, "numtris %i"))
                {
                    mesh.NumTris = RequireCount(buffer.Ints[0], "numtris", filename);
                    mesh.Faces = new int[mesh.NumTris][];
                    for (int i = 0; i < mesh.NumTris; i++)
                    {
                        line = ReadRequiredLine(file, "triangle");
                        RequireParsed(ref buffer, line, "tri %i %i %i %i", filename);
                        int index = RequireIndex(buffer.Ints[0], mesh.NumTris, "tri", filename);

                        // Doom 3 stores triangles clockwise; reverse to counter-clockwise for OpenGL.
                        mesh.Faces[index] = new[] { buffer.Ints[3], buffer.Ints[2], buffer.Ints[1] };
                    }
                }
                else if (ParseLine(ref buffer, line, "numweights %i"))
                {
                    mesh.NumWeights = RequireCount(buffer.Ints[0], "numweights", filename);
                    mesh.Weights = new MD5Weight[mesh.NumWeights];
                    for (int i = 0; i < mesh.NumWeights; i++)
                    {
                        line = ReadRequiredLine(file, "weight");
                        RequireParsed(ref buffer, line, "weight %i %i %f ( %f %f %f )", filename);
                        int index = RequireIndex(buffer.Ints[0], mesh.NumWeights, "weight", filename);
                        mesh.Weights[index].Joint = buffer.Ints[1];
                        mesh.Weights[index].Bias = buffer.Floats[0];
                        mesh.Weights[index].Pos = new Vector3(buffer.Floats[1], buffer.Floats[2], buffer.Floats[3]);
                    }
                }
            }
        }

        private void ValidateMesh(MD5Mesh mesh, int meshIndex, string filename)
        {
            if (mesh.Verts == null || mesh.Faces == null || mesh.Weights == null)
            {
                throw new InvalidDataException($"Mesh {meshIndex} is incomplete in '{filename}'.");
            }

            for (int i = 0; i < mesh.NumVert; i++)
            {
                MD5Vertex v = mesh.Verts[i];
                if (v.CountWeight <= 0 || v.StartWeight < 0 || v.StartWeight + v.CountWeight > mesh.NumWeights)
                {
                    throw new InvalidDataException($"Mesh {meshIndex}, vertex {i} references invalid weights in '{filename}'.");
                }
            }

            for (int i = 0; i < mesh.NumTris; i++)
            {
                int[] face = mesh.Faces[i];
                if (face == null || face[0] < 0 || face[0] >= mesh.NumVert ||
                    face[1] < 0 || face[1] >= mesh.NumVert || face[2] < 0 || face[2] >= mesh.NumVert)
                {
                    throw new InvalidDataException($"Mesh {meshIndex}, triangle {i} is missing or references invalid vertices in '{filename}'.");
                }
            }

            for (int i = 0; i < mesh.NumWeights; i++)
            {
                if (mesh.Weights[i].Joint < 0 || mesh.Weights[i].Joint >= numJoints)
                {
                    throw new InvalidDataException($"Mesh {meshIndex}, weight {i} references invalid joint in '{filename}'.");
                }
            }
        }

        public void LoadAnim(string animName, string filename)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(animName);
            ArgumentException.ThrowIfNullOrWhiteSpace(filename);
            if (bPSkeleton == null)
            {
                throw new InvalidOperationException("Load a mesh before loading an animation.");
            }

            Animation anim = new Animation { AnimName = animName };
            TextBuffer buffer = new TextBuffer();

            MD5JointInfo[] jointInfos = null;
            LocalJoint[] baseFrame = null;
            float[] animFrameData = null;
            bool[] framesRead = null;
            int numAnimatedComponents = -1;
            int version = 0;

            using (StreamReader file = new StreamReader(filename))
            {
                string line;
                while ((line = file.ReadLine()) != null)
                {
                    CleanString(ref line);
                    if (line.Length == 0) continue;

                    if (ParseLine(ref buffer, line, "MD5Version %i"))
                    {
                        version = buffer.Ints[0];
                        continue;
                    }

                    if (ParseLine(ref buffer, line, "numFrames %i"))
                    {
                        anim.NumFrames = buffer.Ints[0];
                        if (anim.NumFrames <= 0) throw new InvalidDataException($"Invalid frame count in '{filename}'.");
                        anim.Boxes = new MD5BoundingBox[anim.NumFrames];
                        framesRead = new bool[anim.NumFrames];
                        continue;
                    }

                    if (ParseLine(ref buffer, line, "numJoints %i"))
                    {
                        anim.NumJoints = buffer.Ints[0];
                        if (anim.NumJoints != numJoints)
                        {
                            throw new InvalidDataException($"Animation '{filename}' has {anim.NumJoints} joints, the mesh has {numJoints}.");
                        }
                        jointInfos = new MD5JointInfo[anim.NumJoints];
                        baseFrame = new LocalJoint[anim.NumJoints];
                        anim.Parents = new int[anim.NumJoints];
                        continue;
                    }

                    if (ParseLine(ref buffer, line, "frameRate %i"))
                    {
                        anim.FrameRate = buffer.Ints[0];
                        continue;
                    }

                    // Number of animated float parameters per frame. Zero is valid (a static pose).
                    if (ParseLine(ref buffer, line, "numAnimatedComponents %i"))
                    {
                        numAnimatedComponents = buffer.Ints[0];
                        if (numAnimatedComponents < 0) throw new InvalidDataException($"Invalid component count in '{filename}'.");
                        animFrameData = new float[numAnimatedComponents];
                        continue;
                    }

                    if (line == "hierarchy {")
                    {
                        RequireHeader(jointInfos != null && numAnimatedComponents >= 0, "hierarchy", filename);
                        for (int i = 0; i < anim.NumJoints; ++i)
                        {
                            line = ReadRequiredLine(file, "animation hierarchy");
                            CleanString(ref line);
                            RequireParsed(ref buffer, line, "%s %i %i %i", filename);

                            MD5JointInfo info = new MD5JointInfo
                            {
                                Name = buffer.String,
                                Parent = buffer.Ints[0],
                                Flags = buffer.Ints[1],
                                StartIndex = buffer.Ints[2]
                            };

                            if (info.Parent != bPSkeleton[i].Parent)
                            {
                                throw new InvalidDataException($"Animation '{filename}' joint {i} ('{info.Name}') does not match the mesh hierarchy.");
                            }

                            int componentCount = System.Numerics.BitOperations.PopCount((uint)(info.Flags & 63));
                            if (componentCount > 0 && (info.StartIndex < 0 || info.StartIndex + componentCount > numAnimatedComponents))
                            {
                                throw new InvalidDataException($"Animation '{filename}' joint {i} references components outside the frame data.");
                            }

                            jointInfos[i] = info;
                            anim.Parents[i] = info.Parent;
                        }
                        continue;
                    }

                    if (line == "bounds {")
                    {
                        RequireHeader(anim.Boxes != null, "bounds", filename);
                        for (int i = 0; i < anim.NumFrames; ++i)
                        {
                            line = ReadRequiredLine(file, "animation bounds");
                            CleanString(ref line);
                            RequireParsed(ref buffer, line, "( %f %f %f ) ( %f %f %f )", filename);
                            anim.Boxes[i].Min = new Vector3(buffer.Floats[0], buffer.Floats[1], buffer.Floats[2]);
                            anim.Boxes[i].Max = new Vector3(buffer.Floats[3], buffer.Floats[4], buffer.Floats[5]);
                        }
                        continue;
                    }

                    // The baseframe holds the parent-relative joints that the per-frame
                    // components are applied on top of.
                    if (line == "baseframe {")
                    {
                        RequireHeader(baseFrame != null, "baseframe", filename);
                        for (int i = 0; i < anim.NumJoints; ++i)
                        {
                            line = ReadRequiredLine(file, "animation base frame");
                            CleanString(ref line);
                            RequireParsed(ref buffer, line, "( %f %f %f ) ( %f %f %f )", filename);

                            baseFrame[i].Pos = new Vector3(buffer.Floats[0], buffer.Floats[1], buffer.Floats[2]);
                            baseFrame[i].Orient = new Quaternion(buffer.Floats[3], buffer.Floats[4], buffer.Floats[5], 1);
                            MyMath.ComputeWComponent(ref baseFrame[i].Orient);
                        }
                        continue;
                    }

                    if (ParseLine(ref buffer, line, "frame %i {"))
                    {
                        RequireHeader(jointInfos != null && baseFrame != null && animFrameData != null && framesRead != null, "frame", filename);
                        anim.Frames ??= new LocalJoint[anim.NumFrames, anim.NumJoints];

                        int frameIndex = RequireIndex(buffer.Ints[0], anim.NumFrames, "frame", filename);

                        for (int i = 0; i < numAnimatedComponents;)
                        {
                            line = ReadRequiredLine(file, $"animation frame {frameIndex}");
                            CleanString(ref line);
                            if (line == "}")
                            {
                                throw new InvalidDataException($"Animation frame {frameIndex} contains too few components in '{filename}'.");
                            }

                            string[] splt = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            for (int ww = 0; ww < splt.Length; ww++)
                            {
                                if (i >= numAnimatedComponents ||
                                    !float.TryParse(splt[ww], NumberStyles.Float, CultureInfo.InvariantCulture, out animFrameData[i]))
                                {
                                    throw new InvalidDataException($"Invalid animation frame data in '{filename}'.");
                                }
                                i++;
                            }
                        }

                        BuildLocalFrame(jointInfos, baseFrame, animFrameData, frameIndex, anim);
                        framesRead[frameIndex] = true;
                    }
                }
            }

            if (version != 10 || anim.NumFrames <= 0 || anim.NumJoints != numJoints || anim.FrameRate <= 0 ||
                anim.Frames == null || anim.Parents == null || framesRead == null || Array.IndexOf(framesRead, false) >= 0)
            {
                throw new InvalidDataException($"Invalid or incompatible MD5 animation '{filename}'.");
            }

            if (LockRootMotion)
            {
                RemoveRootMotion(anim);
            }

            anim.Time = 0f;
            skeleton = new MD5Joint[numJoints];
            for (int i = 0; i < numJoints; i++)
            {
                skeleton[i].Name = bPSkeleton[i].Name;
                skeleton[i].Parent = bPSkeleton[i].Parent;
            }

            animations.RemoveAll(item => item.AnimName == animName);
            animations.Add(anim);
            animated = true;
            UseAnimation(animName);
        }

        /// <summary>
        /// Applies the animated components of one frame to the base frame, producing
        /// parent-relative joints. Hierarchy concatenation is deferred until after blending.
        /// </summary>
        private static void BuildLocalFrame(MD5JointInfo[] jointInfos, LocalJoint[] baseFrame, float[] frameData, int frameIndex, Animation anim)
        {
            for (int i = 0; i < anim.NumJoints; ++i)
            {
                MD5JointInfo info = jointInfos[i];
                Vector3 pos = baseFrame[i].Pos;
                Quaternion orient = baseFrame[i].Orient;
                int j = info.StartIndex;

                if ((info.Flags & 1) != 0) pos.X = frameData[j++];     // Tx
                if ((info.Flags & 2) != 0) pos.Y = frameData[j++];     // Ty
                if ((info.Flags & 4) != 0) pos.Z = frameData[j++];     // Tz
                if ((info.Flags & 8) != 0) orient.X = frameData[j++];  // Qx
                if ((info.Flags & 16) != 0) orient.Y = frameData[j++]; // Qy
                if ((info.Flags & 32) != 0) orient.Z = frameData[j++]; // Qz

                MyMath.ComputeWComponent(ref orient);

                if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z) ||
                    !float.IsFinite(orient.X) || !float.IsFinite(orient.Y) || !float.IsFinite(orient.Z) || !float.IsFinite(orient.W))
                {
                    throw new InvalidDataException($"Animation produced an invalid joint at frame {frameIndex}, joint {i}.");
                }

                anim.Frames[frameIndex, i] = new LocalJoint { Pos = pos, Orient = orient };
            }
        }

        private static void RemoveRootMotion(Animation anim)
        {
            for (int joint = 0; joint < anim.NumJoints; joint++)
            {
                if (anim.Parents[joint] >= 0) continue;

                Vector3 reference = anim.Frames[0, joint].Pos;
                for (int frame = 1; frame < anim.NumFrames; frame++)
                {
                    anim.Frames[frame, joint].Pos = reference;
                }
            }
        }

        /// <summary>
        /// Blends two frames in parent-relative space (lerp translation, slerp rotation) and
        /// then concatenates the hierarchy into model space, as Doom 3 does. Blending
        /// model-space joints instead would shorten limbs during fast rotations.
        /// </summary>
        private void BlendSkeleton(Animation anim, int frame1, int frame2, float blend)
        {
            for (int i = 0; i < anim.NumJoints; ++i)
            {
                LocalJoint a = anim.Frames[frame1, i];
                LocalJoint b = anim.Frames[frame2, i];

                Vector3 pos = Vector3.Lerp(a.Pos, b.Pos, blend);
                Quaternion orient = MyMath.Slerp(ref a.Orient, ref b.Orient, blend);

                int parent = anim.Parents[i];
                if (parent < 0)
                {
                    skeleton[i].Pos = pos;
                    skeleton[i].Orient = orient;
                }
                else
                {
                    MD5Joint p = skeleton[parent];
                    skeleton[i].Pos = p.Pos + MyMath.RotatePoint(ref p.Orient, ref pos);
                    Quaternion combined = MyMath.Mult(ref p.Orient, ref orient);
                    skeleton[i].Orient = MyMath.Normalize(ref combined);
                }
            }
        }

        private static void TimeToFrames(Animation anim, out int frame1, out int frame2, out float blend)
        {
            if (anim.NumFrames <= 1)
            {
                frame1 = frame2 = 0;
                blend = 0f;
                return;
            }

            double frameTime = (double)anim.Time * anim.FrameRate;
            int whole = (int)Math.Floor(frameTime);
            blend = (float)(frameTime - whole);
            frame1 = whole % anim.CycleFrames;
            if (frame1 < 0) frame1 += anim.CycleFrames;
            frame2 = frame1 + 1;
        }

        public void Update(float dt)
        {
            if (animated && curAnim != null)
            {
                if (float.IsFinite(dt) && dt > 0f)
                {
                    float duration = curAnim.Duration;
                    curAnim.Time += dt;
                    if (curAnim.Time >= duration)
                    {
                        curAnim.Time %= duration;
                    }
                }

                TimeToFrames(curAnim, out int frame1, out int frame2, out float blend);
                BlendSkeleton(curAnim, frame1, frame2, blend);
                ComputingVertexPositions(skeleton);
            }
            else if (bPSkeleton != null)
            {
                skeleton = bPSkeleton;
                ComputingVertexPositions(skeleton);
            }
        }

        public void UseAnimation(string animName)
        {
            Animation found = animations.Find(a => a.AnimName == animName);
            if (found == null) return;

            curAnim = found;
            Update(0f);
        }

        public int CurrentFrame
        {
            get
            {
                if (!animated || curAnim == null) return 0;
                TimeToFrames(curAnim, out int frame1, out _, out _);
                return frame1;
            }
        }

        /// <summary>Number of distinct frames in one playback cycle.</summary>
        public int TotalFrames => animated && curAnim != null ? curAnim.CycleFrames : 0;

        public void SeekToFrame(int frame)
        {
            if (!animated || curAnim == null) return;
            int clamped = Math.Clamp(frame, 0, curAnim.CycleFrames - 1);
            curAnim.Time = (float)clamped / curAnim.FrameRate;
            Update(0f);
        }

        private void BuildOutputBuffers()
        {
            meshVertices.Clear();
            meshIndices.Clear();
            for (int k = 0; k < numMeshes; k++)
            {
                MD5Mesh mesh = model[k];
                meshVertices.Add(new Vertex[mesh.NumVert]);

                int[] indices = new int[mesh.NumTris * 3];
                for (int i = 0; i < mesh.NumTris; i++)
                {
                    indices[i * 3 + 0] = mesh.Faces[i][0];
                    indices[i * 3 + 1] = mesh.Faces[i][1];
                    indices[i * 3 + 2] = mesh.Faces[i][2];
                }
                meshIndices.Add(indices);
            }
        }

        /// <summary>
        /// CPU linear-blend skinning: every weight's position and bind-pose tangent frame is
        /// transformed by its joint and accumulated with the weight's bias.
        /// </summary>
        private void ComputingVertexPositions(MD5Joint[] pose)
        {
            for (int k = 0; k < numMeshes; k++)
            {
                MD5Mesh mesh = model[k];
                Vertex[] output = meshVertices[k];

                for (int i = 0; i < mesh.NumVert; i++)
                {
                    Vector3 position = Vector3.Zero;
                    Vector3 normal = Vector3.Zero;
                    Vector3 tangent = Vector3.Zero;
                    Vector3 bitangent = Vector3.Zero;

                    MD5Vertex vert = mesh.Verts[i];
                    for (int j = 0; j < vert.CountWeight; j++)
                    {
                        MD5Weight weight = mesh.Weights[vert.StartWeight + j];
                        MD5Joint joint = pose[weight.Joint];

                        position += (joint.Pos + MyMath.RotatePoint(ref joint.Orient, ref weight.Pos)) * weight.Bias;
                        normal += MyMath.RotatePoint(ref joint.Orient, ref weight.Normal) * weight.Bias;
                        tangent += MyMath.RotatePoint(ref joint.Orient, ref weight.Tangent) * weight.Bias;
                        bitangent += MyMath.RotatePoint(ref joint.Orient, ref weight.Bitangent) * weight.Bias;
                    }

                    if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                    {
                        throw new InvalidDataException($"Skinning produced an invalid vertex in mesh {k}, vertex {i}.");
                    }

                    // Re-orthonormalise: blending rotated bases does not preserve unit length or orthogonality.
                    Vector3 n = SafeNormalize(normal, Vector3.UnitZ);
                    Vector3 t = SafeNormalize(tangent - n * Vector3.Dot(n, tangent), AnyPerpendicular(n));
                    Vector3 b = Vector3.Cross(n, t);
                    if (Vector3.Dot(b, bitangent) < 0f) b = -b;

                    output[i] = new Vertex(position, n, vert.UV, t, b);
                }
            }
        }

        /// <summary>
        /// Derives smooth per-vertex normals and UV tangents in the bind pose and stores them in
        /// each weight's joint space, so they can be skinned exactly like positions.
        /// Normals are shared between vertices that occupy the same position (UV seams), as
        /// Doom 3 does with its duplicated vertices, to avoid visible lighting seams.
        /// Tangents are kept per vertex because they legitimately differ across UV seams.
        /// </summary>
        private void InitializeBindPoseBasis()
        {
            for (int meshIndex = 0; meshIndex < numMeshes; meshIndex++)
            {
                MD5Mesh mesh = model[meshIndex];
                Vector3[] positions = BuildPositions(mesh, bPSkeleton);
                Vector3[] normalAccum = new Vector3[mesh.NumVert];
                Vector3[] tangentAccum = new Vector3[mesh.NumVert];
                Vector3[] bitangentAccum = new Vector3[mesh.NumVert];

                for (int triangle = 0; triangle < mesh.NumTris; triangle++)
                {
                    int i0 = mesh.Faces[triangle][0];
                    int i1 = mesh.Faces[triangle][1];
                    int i2 = mesh.Faces[triangle][2];
                    Vector3 edge1 = positions[i1] - positions[i0];
                    Vector3 edge2 = positions[i2] - positions[i0];

                    // Unnormalised cross product: area-weighted face normal.
                    Vector3 faceNormal = Vector3.Cross(edge1, edge2);
                    normalAccum[i0] += faceNormal;
                    normalAccum[i1] += faceNormal;
                    normalAccum[i2] += faceNormal;

                    Vector2 duv1 = mesh.Verts[i1].UV - mesh.Verts[i0].UV;
                    Vector2 duv2 = mesh.Verts[i2].UV - mesh.Verts[i0].UV;
                    float determinant = duv1.X * duv2.Y - duv2.X * duv1.Y;
                    if (MathF.Abs(determinant) < 1e-12f) continue;

                    float inverse = 1f / determinant;
                    Vector3 tangent = (edge1 * duv2.Y - edge2 * duv1.Y) * inverse;
                    Vector3 bitangent = (edge2 * duv1.X - edge1 * duv2.X) * inverse;

                    // Weight by triangle area so tiny or sliver triangles do not dominate.
                    float weight = faceNormal.Length * MathF.Abs(determinant);
                    float tangentScale = weight / MathF.Max(tangent.Length, 1e-12f);
                    float bitangentScale = weight / MathF.Max(bitangent.Length, 1e-12f);
                    tangentAccum[i0] += tangent * tangentScale;
                    tangentAccum[i1] += tangent * tangentScale;
                    tangentAccum[i2] += tangent * tangentScale;
                    bitangentAccum[i0] += bitangent * bitangentScale;
                    bitangentAccum[i1] += bitangent * bitangentScale;
                    bitangentAccum[i2] += bitangent * bitangentScale;
                }

                Vector3[] smoothNormals = ShareNormalsAcrossSeams(positions, normalAccum);

                for (int vertexIndex = 0; vertexIndex < mesh.NumVert; vertexIndex++)
                {
                    Vector3 n = SafeNormalize(smoothNormals[vertexIndex], Vector3.UnitZ);
                    Vector3 t = tangentAccum[vertexIndex] - n * Vector3.Dot(n, tangentAccum[vertexIndex]);
                    t = SafeNormalize(t, AnyPerpendicular(n));
                    Vector3 b = Vector3.Cross(n, t);
                    if (Vector3.Dot(b, bitangentAccum[vertexIndex]) < 0f) b = -b;

                    MD5Vertex vertex = mesh.Verts[vertexIndex];
                    for (int j = 0; j < vertex.CountWeight; j++)
                    {
                        int weightIndex = vertex.StartWeight + j;
                        MD5Weight weight = mesh.Weights[weightIndex];
                        Quaternion inverseJoint = Quaternion.Conjugate(bPSkeleton[weight.Joint].Orient);
                        weight.Normal = MyMath.RotatePoint(ref inverseJoint, ref n);
                        weight.Tangent = MyMath.RotatePoint(ref inverseJoint, ref t);
                        weight.Bitangent = MyMath.RotatePoint(ref inverseJoint, ref b);
                        mesh.Weights[weightIndex] = weight;
                    }
                }
                model[meshIndex] = mesh;
            }
        }

        private static Vector3[] ShareNormalsAcrossSeams(Vector3[] positions, Vector3[] normals)
        {
            // Only surfaces facing roughly the same way are merged, so back-to-back geometry that
            // happens to share positions (thin plates, closed slits) keeps its own normals.
            const float cell = 1e-3f;
            const float minCosine = 0.0f;

            var groups = new Dictionary<(int, int, int), List<int>>(positions.Length);
            var keys = new (int, int, int)[positions.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                var key = ((int)MathF.Round(p.X / cell), (int)MathF.Round(p.Y / cell), (int)MathF.Round(p.Z / cell));
                keys[i] = key;
                if (!groups.TryGetValue(key, out List<int> members))
                {
                    members = new List<int>(2);
                    groups[key] = members;
                }
                members.Add(i);
            }

            Vector3[] result = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 own = normals[i];
                Vector3 ownDirection = SafeNormalize(own, Vector3.Zero);
                Vector3 shared = own;

                foreach (int j in groups[keys[i]])
                {
                    if (j == i) continue;
                    if (Vector3.Dot(ownDirection, SafeNormalize(normals[j], Vector3.Zero)) > minCosine)
                    {
                        shared += normals[j];
                    }
                }

                result[i] = shared;
            }
            return result;
        }

        private static Vector3[] BuildPositions(MD5Mesh mesh, MD5Joint[] pose)
        {
            Vector3[] positions = new Vector3[mesh.NumVert];
            for (int i = 0; i < mesh.NumVert; i++)
            {
                Vector3 position = Vector3.Zero;
                MD5Vertex vertex = mesh.Verts[i];
                for (int j = 0; j < vertex.CountWeight; j++)
                {
                    MD5Weight weight = mesh.Weights[vertex.StartWeight + j];
                    MD5Joint joint = pose[weight.Joint];
                    position += (joint.Pos + MyMath.RotatePoint(ref joint.Orient, ref weight.Pos)) * weight.Bias;
                }
                positions[i] = position;
            }

            return positions;
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            float lengthSquared = value.LengthSquared;
            return lengthSquared > 1e-12f ? value / MathF.Sqrt(lengthSquared) : fallback;
        }

        private static Vector3 AnyPerpendicular(Vector3 n) =>
            Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));

        /// <summary>Axis-aligned bounds of the current skinned pose.</summary>
        public bool TryGetBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            bool any = false;
            foreach (Vertex[] vertices in meshVertices)
            {
                foreach (Vertex v in vertices)
                {
                    min = Vector3.ComponentMin(min, v.Position);
                    max = Vector3.ComponentMax(max, v.Position);
                    any = true;
                }
            }
            return any;
        }

        private static void CleanString(ref string str)
        {
            if (str == null)
            {
                throw new EndOfStreamException("Unexpected end of MD5 file.");
            }

            int commentStart = str.IndexOf("//", StringComparison.Ordinal);
            if (commentStart >= 0)
            {
                str = str[..commentStart];
            }

            str = str.Replace('\t', ' ').Replace('"', ' ').Trim();

            while (str.Contains("  ", StringComparison.Ordinal))
            {
                str = str.Replace("  ", " ");
            }
        }

        private static bool ParseLine(ref TextBuffer buffer, string txt, string str)
        {
            if (txt == null || str == null)
            {
                return false;
            }

            CleanString(ref txt);

            string[] splitTxt = txt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] splitStr = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (splitTxt.Length != splitStr.Length)
            {
                return false;
            }

            int countFloat = 0, countInt = 0;
            foreach (string token in splitStr)
            {
                if (token == "%i") countInt++;
                else if (token == "%f") countFloat++;
            }

            buffer.Ints = new int[countInt];
            buffer.Floats = new float[countFloat];
            countFloat = countInt = 0;

            for (int i = 0; i < splitStr.Length; i++)
            {
                switch (splitStr[i])
                {
                    case "%i":
                        if (!int.TryParse(splitTxt[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out buffer.Ints[countInt++]))
                            return false;
                        break;
                    case "%f":
                        if (!float.TryParse(splitTxt[i], NumberStyles.Float, CultureInfo.InvariantCulture, out buffer.Floats[countFloat++]))
                            return false;
                        break;
                    case "%s":
                        buffer.String = splitTxt[i];
                        break;
                    default:
                        // Keywords are case-insensitive; names (%s) keep their original case.
                        if (!string.Equals(splitStr[i], splitTxt[i], StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                }
            }
            return true;
        }

        private static string ReadRequiredLine(StreamReader reader, string section) =>
            reader.ReadLine() ?? throw new EndOfStreamException($"Unexpected end of MD5 file while reading {section}.");

        private static void RequireParsed(ref TextBuffer buffer, string line, string pattern, string filename)
        {
            if (!ParseLine(ref buffer, line, pattern))
            {
                throw new InvalidDataException($"Invalid MD5 data in '{filename}': '{line}'.");
            }
        }

        private static int RequireCount(int value, string what, string filename) =>
            value >= 0 ? value : throw new InvalidDataException($"Negative {what} in '{filename}'.");

        private static int RequireIndex(int value, int count, string what, string filename) =>
            value >= 0 && value < count ? value : throw new InvalidDataException($"{what} index {value} out of range in '{filename}'.");

        private static void RequireHeader(bool condition, string section, string filename)
        {
            if (!condition)
            {
                throw new InvalidDataException($"'{section}' block appears before the required header fields in '{filename}'.");
            }
        }
    }
}

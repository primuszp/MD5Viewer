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
            /// <summary>
            /// StartWeight (int) index
            /// </summary>
            public int StartWeight;
            
            /// <summary>
            /// <para>CountWeight (int) is the number of weights, starting from the StartWeight (int) index,</para>
            /// <para>which are used to calculate the final vertex position</para>
            /// </summary>
            public int CountWeight;
            
            /// <summary>
            /// The texture coordinates (also called UV coords)
            /// </summary>
            public Vector2 UV;
        }

        private struct MD5Weight
        {
            /// <summary>
            /// Joint is the joint it depends of
            /// </summary>
            public int Joint;

            /// <summary>
            /// <para>Bias is a factor in the [0, 1] range which defines</para>
            /// <para>the contribution of this weight when computing a vertex position</para>
            /// </summary>
            public float Bias;

            /// <summary>
            /// The weight's position in space
            /// </summary>
            public Vector3 Pos;
            public Vector3 Normal;
            public Vector3 Tangent;
            public Vector3 Bitangent;
        }

        private struct MD5Joint
        {
            /// <summary>
            /// The joint's name (string)
            /// </summary>
            public string Name;

            /// <summary>
            /// <para>The joint's parent index (int)</para>
            /// <para>If it is equal to -1, then the joint has no parent joint and is what we call a root  joint</para>
            /// </summary>
            public int Parent;

            /// <summary>
            /// The joint's position in space (float)
            /// </summary>
            public Vector3 Pos;

            /// <summary>
            /// The joint's orientation quaternion x, y and z components
            /// </summary>
            public Quaternion Orient;
        }

        private struct MD5Mesh
        {
            /// <summary>
            /// <para>The shader string is defined in the MTR files (/materials  directory) of Doom 3 </para>
            /// <para>and tell you what are the textures to apply to the mesh and how to combine them </para>
            /// </summary>
            public string Shader;

            /// <summary>
            /// The number of vertices of the mesh (int)
            /// </summary>
            public int NumVert;

            /// <summary>
            /// The number of triangles of the mesh (int)
            /// </summary>
            public int NumTris;


            /// <summary>
            /// The number of weights of the mesh (int)
            /// </summary>
            public int NumWeights;


            public int[][] Faces;
            public MD5Vertex[] Verts;
            public MD5Weight[] Weights;
        }

        private struct Animation
        {
            public string AnimName;

            //The number of frames of the animation
            public int NumFrames;

            //The number of joints of the frame skeletons
            public int NumJoints;

            //The number of frames per second to draw for the animation
            public int FrameRate;


            public MD5Joint[,] SkelFrames;
            public MD5BoundingBox[] Boxes;

            public int CurrentFrame;
            public int NextFrame;
            public float LastTime;
            public float MaxTime;

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

        private struct MD5BaseFrameJoint
        {
            public Vector3 Pos;
            public Quaternion Orient;
        }

        //Version of the format
        private int versionNr;
        //The number of meshes of the model contained in the md5mesh file
        private int numMeshes;
        //The number of joints of the model's skeleton
        private int numJoints;

        private int meshIndex;

        //The model's skeleton stored in the MD5 Mesh files is what we call the "bind-pose skeleton"
        private MD5Joint[] bPSkeleton;
        private MD5Joint[] skeleton;

        List<Animation> animations = new List<Animation>();
        Animation curAnim;


        private MD5Mesh[] model;
        private Vector3[] finalVertices;
        private List<Vertex[]> meshes = new List<Vertex[]>();
        private bool bindPoseBasisReady;

        private bool animated = false;

        public IReadOnlyList<Vertex[]> Meshes
        {
            get { return meshes; }
        }

        public bool HasAnimation
        {
            get { return animated; }
        }

        public int MeshCount => meshes.Count;

        public string GetMeshShader(int index) =>
            (index >= 0 && index < numMeshes && model != null) ? model[index].Shader ?? "" : "";


        public void Load(string filename)
        {
            meshIndex = 0;
            string line = "";
            TextBuffer buffer = new TextBuffer();

            using (StreamReader file = new StreamReader(filename))
            {
                while ((line = file.ReadLine()) != null)
                {
                    // The first line tell you the version of the format. This is an integer. Doom 3's MD5 version is 10.
                    if (ParseLine(ref buffer, line, "MD5Version %i"))
                    {
                        versionNr = buffer.Ints[0];
                    }

                    // Read number of joints
                    if (ParseLine(ref buffer, line, "numJoints %i"))
                    {
                        numJoints = buffer.Ints[0];
                        bPSkeleton = new MD5Joint[numJoints];
                    }

                    // Read number of meshes
                    if (ParseLine(ref buffer, line, "numMeshes %i"))
                    {
                        numMeshes = buffer.Ints[0];
                        model = new MD5Mesh[numMeshes];
                    }

                    // Parse model joints
                    if (line.Equals("joints {"))
                    {
                        for (int i = 0; i < numJoints; i++)
                        {
                            line = file.ReadLine();
                            CleanString(ref line);

                            ParseLine(ref buffer, line, "%s %i ( %f %f %f ) ( %f %f %f )");
                            bPSkeleton[i].Name = buffer.String;
                            bPSkeleton[i].Parent = buffer.Ints[0];
                            bPSkeleton[i].Pos.X = buffer.Floats[0];
                            bPSkeleton[i].Pos.Y = buffer.Floats[1];
                            bPSkeleton[i].Pos.Z = buffer.Floats[2];
                            bPSkeleton[i].Orient = new Quaternion(buffer.Floats[3], buffer.Floats[4], buffer.Floats[5], 1);

                            // After reading a joint, you must calculate the w component
                            MyMath.ComputeWComponent(ref bPSkeleton[i].Orient);
                        }
                    }

                    // Parse model meshes
                    if (line.Equals("mesh {"))
                    {
                        while (!line.Equals("}"))
                        {
                            line = file.ReadLine();
                            CleanString(ref line);

                            // Read texture name
                            if (line.StartsWith("shader"))
                            {
                                if (ParseLine(ref buffer, line, "shader %s"))
                                {
                                    model[meshIndex].Shader = buffer.String;
                                }
                            }

                            // Read mesh data
                            if (ParseLine(ref buffer, line, "numverts %i"))
                            {
                                model[meshIndex].NumVert = buffer.Ints[0];
                                model[meshIndex].Verts = new MD5Vertex[model[meshIndex].NumVert];

                                //model[meshCount].texture = Texture.Load(textureName, false);

                                for (int i = 0; i < model[meshIndex].NumVert; i++)
                                {
                                    line = file.ReadLine();
                                    ParseLine(ref buffer, line, "vert %i ( %f %f ) %i %i");
                                    model[meshIndex].Verts[buffer.Ints[0]].UV.X = buffer.Floats[0];
                                    model[meshIndex].Verts[buffer.Ints[0]].UV.Y = buffer.Floats[1];
                                    model[meshIndex].Verts[buffer.Ints[0]].StartWeight = buffer.Ints[1];
                                    model[meshIndex].Verts[buffer.Ints[0]].CountWeight = buffer.Ints[2];
                                }
                            }
                            if (ParseLine(ref buffer, line, "numtris %i"))
                            {
                                model[meshIndex].NumTris = buffer.Ints[0];
                                model[meshIndex].Faces = new int[model[meshIndex].NumTris][];
                                for (int i = 0; i < model[meshIndex].NumTris; i++)
                                {
                                    line = file.ReadLine();
                                    ParseLine(ref buffer, line, "tri %i %i %i %i");

                                    model[meshIndex].Faces[buffer.Ints[0]] = new int[3];
                                    model[meshIndex].Faces[buffer.Ints[0]][0] = buffer.Ints[3];
                                    model[meshIndex].Faces[buffer.Ints[0]][1] = buffer.Ints[2];
                                    model[meshIndex].Faces[buffer.Ints[0]][2] = buffer.Ints[1];
                                }
                            }
                            if (ParseLine(ref buffer, line, "numweights %i"))
                            {
                                model[meshIndex].NumWeights = buffer.Ints[0];
                                model[meshIndex].Weights = new MD5Weight[model[meshIndex].NumWeights];
                                for (int i = 0; i < model[meshIndex].NumWeights; i++)
                                {
                                    line = file.ReadLine();
                                    ParseLine(ref buffer, line, "weight %i %i %f ( %f %f %f )");
                                    model[meshIndex].Weights[buffer.Ints[0]].Joint = buffer.Ints[1];
                                    model[meshIndex].Weights[buffer.Ints[0]].Bias = buffer.Floats[0];
                                    model[meshIndex].Weights[buffer.Ints[0]].Pos.X = buffer.Floats[1];
                                    model[meshIndex].Weights[buffer.Ints[0]].Pos.Y = buffer.Floats[2];
                                    model[meshIndex].Weights[buffer.Ints[0]].Pos.Z = buffer.Floats[3];
                                }
                            }
                        }
                        meshIndex++;
                    }
                }
            }


            InitializeBindPoseBasis();
            ComputingVertexPositions(bPSkeleton);
        }

        public void LoadAnim(string animName, string filename)
        {
            Animation anim = new Animation();
            TextBuffer buffer = new TextBuffer();

            MD5JointInfo[] jointInfos = null;
            MD5BaseFrameJoint[] baseFrame = null;
            float[] animFrameData = null;

            anim.AnimName = animName;
            int numAnimatedComponents = 0;
            int frameIndex;

            using (StreamReader file = new StreamReader(filename))
            {
                string line = "";
                while ((line = file.ReadLine()) != null)
                {
                    if (line == "") continue;

                    //Read number of joints
                    if (ParseLine(ref buffer, line, "numFrames %i"))
                    {
                        //Allocate memory for skeleton frames and bounding boxes
                        anim.NumFrames = buffer.Ints[0];

                        if (anim.NumFrames > 0)
                        {
                            anim.Boxes = new MD5BoundingBox[anim.NumFrames];
                        }
                    }

                    if (ParseLine(ref buffer, line, "numJoints %i"))
                    {
                        //Allocate memory for joints of each frame
                        anim.NumJoints = buffer.Ints[0];

                        if (anim.NumJoints > 0)
                        {
                            //Allocate temporary memory for building skeleton frames
                            jointInfos = new MD5JointInfo[anim.NumJoints];
                            baseFrame = new MD5BaseFrameJoint[anim.NumJoints];
                        }
                        anim.SkelFrames = new MD5Joint[anim.NumFrames, anim.NumJoints];
                    }

                    if (ParseLine(ref buffer, line, "frameRate %i"))
                    {
                        anim.FrameRate = buffer.Ints[0];
                    }

                    //numAnimatedComponents (int) is the number of parameters per frame used to compute the frame skeletons.
                    //These parameters, combined with the baseframe skeleton given in the MD5 Anim file, permit to build a skeleton for each frame.

                  

                    if (ParseLine(ref buffer, line, "numAnimatedComponents %i"))
                    {
                        numAnimatedComponents = buffer.Ints[0];

                        if (numAnimatedComponents > 0)
                        {
                            //Allocate memory for animation frame data
                            animFrameData = new float[numAnimatedComponents];
                        }
                    }

                    //After reading the header, comes the skeleton hierarchy.
                    //It brings information about the joints for building the skeleton frames from the baseframe data.
                    if (line.Equals("hierarchy {"))
                    {
                        for (int i = 0; i < anim.NumJoints; ++i)
                        {
                            //Read whole line
                            line = file.ReadLine();
                            CleanString(ref line);

                            //Read joint info
                            ParseLine(ref buffer, line, "%s %i %i %i");
                            jointInfos[i].Name = buffer.String;
                            jointInfos[i].Parent = buffer.Ints[0];
                            jointInfos[i].Flags = buffer.Ints[1];
                            jointInfos[i].StartIndex = buffer.Ints[2];
                        }
                    }

                    //After the hierarchy comes the frame bounds. There is a bounding box for each frame.
                    if (line.Equals("bounds {"))
                    {
                        for (int i = 0; i < anim.NumFrames; ++i)
                        {
                            //Read whole line
                            line = file.ReadLine();
                            CleanString(ref line);

                            //Read bounding box
                            ParseLine(ref buffer, line, "( %f %f %f ) ( %f %f %f )");
                            anim.Boxes[i].Min.X = buffer.Floats[0];
                            anim.Boxes[i].Min.Y = buffer.Floats[1];
                            anim.Boxes[i].Min.Z = buffer.Floats[2];
                            anim.Boxes[i].Max.X = buffer.Floats[3];
                            anim.Boxes[i].Max.Y = buffer.Floats[4];
                            anim.Boxes[i].Max.Z = buffer.Floats[5];
                        }
                    }

                    //After bounds you'll find the baseframe data.
                    //It contains the position and orientation of each joint from which the frame skeletons will be built.
                    if (line.Equals("baseframe {"))
                    {
                        for (int i = 0; i < anim.NumJoints; ++i)
                        {
                            //Read whole line
                            line = file.ReadLine();
                            CleanString(ref line);

                            //Read base frame joint
                            ParseLine(ref buffer, line, "( %f %f %f ) ( %f %f %f )");

                            if (buffer.Floats.Length == 6)
                            {
                                baseFrame[i].Pos.X = buffer.Floats[0];
                                baseFrame[i].Pos.Y = buffer.Floats[1];
                                baseFrame[i].Pos.Z = buffer.Floats[2];
                                baseFrame[i].Orient.X = buffer.Floats[3];
                                baseFrame[i].Orient.Y = buffer.Floats[4];
                                baseFrame[i].Orient.Z = buffer.Floats[5];

                                //Compute the w component
                                MyMath.ComputeWComponent(ref baseFrame[i].Orient);
                            }
                        }
                    }

                    //After the baseframe data, the frame data. There is a chunk of data for each frame.
                    //This data are the parameters used to compute the frame's skeleton.
                  
                    //frameIndex (int) is the index of the frame

                    
                    if (ParseLine(ref buffer, line, "frame %i"))
                    {
                        frameIndex = buffer.Ints[0];

                        //Read frame data
                        for (int i = 0; i < numAnimatedComponents; )
                        {
                            line = file.ReadLine(); if (line[0] == '}') break;
                            CleanString(ref line);
                            string[] splt = line.Split(' ');

                            for (int ww = 0; ww < splt.Length; ww++)
                            {
                                animFrameData[i++] = float.Parse(splt[ww], CultureInfo.InvariantCulture);
                            }
                        }

                        //Build frame skeleton from the collected data
                        BuildFrameSkeleton(ref jointInfos, ref baseFrame, ref animFrameData, frameIndex, anim.NumJoints, ref anim);
                    }
                }


                anim.CurrentFrame = 0;
                anim.NextFrame = anim.NumFrames > 1 ? 1 : 0;

                anim.LastTime = 0;
                anim.MaxTime = 1.0f / anim.FrameRate;

                /* Allocate memory for animated skeleton */
                skeleton = new MD5Joint[anim.NumJoints];

                animated = true;

                animations.Add(anim);
                UseAnimation(animName);
                Update(0);
            }
        }


        private void BuildFrameSkeleton(ref MD5JointInfo[] jointInfos, ref MD5BaseFrameJoint[] baseFrame, ref float[] animFrameData, int frameIndex, int num_joints, ref Animation md5anim)
        {
            for (int i = 0; i < num_joints; ++i)
            {
                int j = 0;
                MD5BaseFrameJoint baseJoint = baseFrame[i];
                Vector3 animatedPos;
                Quaternion animatedOrient;

                animatedPos = baseJoint.Pos;
                animatedOrient = baseJoint.Orient;

                if ((jointInfos[i].Flags & 1) > 0) /* Tx */
                {
                    animatedPos.X = animFrameData[jointInfos[i].StartIndex + j];
                    ++j;
                }

                if ((jointInfos[i].Flags & 2) > 0) /* Ty */
                {
                    animatedPos.Y = animFrameData[jointInfos[i].StartIndex + j];
                    ++j;
                }

                if ((jointInfos[i].Flags & 4) > 0) /* Tz */
                {
                    animatedPos.Z = animFrameData[jointInfos[i].StartIndex + j];
                    ++j;
                }

                if ((jointInfos[i].Flags & 8) > 0) /* Qx */
                {
                    animatedOrient.X = animFrameData[jointInfos[i].StartIndex + j];
                    ++j;
                }

                if ((jointInfos[i].Flags & 16) > 0) /* Qy */
                {
                    animatedOrient.Y = animFrameData[jointInfos[i].StartIndex + j];
                    ++j;
                }

                if ((jointInfos[i].Flags & 32) > 0) /* Qz */
                {
                    animatedOrient.Z = animFrameData[jointInfos[i].StartIndex + j];
                    ++j;
                }

                //Compute orient quaternion's w value
                MyMath.ComputeWComponent(ref animatedOrient);

                int parent = jointInfos[i].Parent;
                md5anim.SkelFrames[frameIndex, i].Parent = parent;
                md5anim.SkelFrames[frameIndex, i].Name = jointInfos[i].Name;

                //Has parent?
                if (md5anim.SkelFrames[frameIndex, i].Parent < 0)
                {
                    md5anim.SkelFrames[frameIndex, i].Pos = animatedPos;
                    md5anim.SkelFrames[frameIndex, i].Orient = animatedOrient;
                }
                else
                {
                    MD5Joint parentJoint = md5anim.SkelFrames[frameIndex, parent];

                    //Rotated Position
                    Vector3 rpos;

                    //Add positions
                    rpos = MyMath.RotatePoint(ref parentJoint.Orient, ref animatedPos);

                    md5anim.SkelFrames[frameIndex, i].Pos.X = rpos.X + parentJoint.Pos.X;
                    md5anim.SkelFrames[frameIndex, i].Pos.Y = rpos.Y + parentJoint.Pos.Y;
                    md5anim.SkelFrames[frameIndex, i].Pos.Z = rpos.Z + parentJoint.Pos.Z;

                    //Concatenate rotations
                    md5anim.SkelFrames[frameIndex, i].Orient = MyMath.Mult(ref parentJoint.Orient, ref animatedOrient);
                    md5anim.SkelFrames[frameIndex, i].Orient = MyMath.Normalize(ref md5anim.SkelFrames[frameIndex, i].Orient);
                }
            }
        }

        private void InterpolateSkeletons(ref MD5Joint[,] skel, int curFrame, int nextFrame, int num_joints, float interp)
        {
            for (int i = 0; i < num_joints; ++i)
            {
                //Copy parent index
                skeleton[i].Parent = skel[curFrame, i].Parent;

                //Linear interpolation for position
                skeleton[i].Pos.X = skel[curFrame, i].Pos.X + interp * (skel[nextFrame, i].Pos.X - skel[curFrame, i].Pos.X);
                skeleton[i].Pos.Y = skel[curFrame, i].Pos.Y + interp * (skel[nextFrame, i].Pos.Y - skel[curFrame, i].Pos.Y);
                skeleton[i].Pos.Z = skel[curFrame, i].Pos.Z + interp * (skel[nextFrame, i].Pos.Z - skel[curFrame, i].Pos.Z);

                //Spherical linear interpolation for orientation
                skeleton[i].Orient = MyMath.Slerp(ref skel[curFrame, i].Orient, ref skel[nextFrame, i].Orient, interp);
            }
        }

        private void Animate(ref Animation anim, float dt)
        {
            anim.LastTime += dt;
            int maxFrames = anim.NumFrames - 1;

            //Move to next frame
            while (anim.LastTime >= anim.MaxTime)
            {
                anim.NextFrame++;
                anim.CurrentFrame++;
                anim.LastTime -= anim.MaxTime;

                if (anim.CurrentFrame > maxFrames) anim.CurrentFrame = 0;
                if (anim.NextFrame > maxFrames) anim.NextFrame = 0;
            }
        }

        public void Update(float time)
        {
            if (animated)
            {
                //Calculate current and next frames
                Animate(ref curAnim, time);
                // Interpolate skeletons between two frames.
                InterpolateSkeletons(ref curAnim.SkelFrames, curAnim.CurrentFrame, curAnim.NextFrame, curAnim.NumJoints, curAnim.LastTime * curAnim.FrameRate);
                ComputingVertexPositions(skeleton);
            }
            else
            {
                //No animation, use bind-pose skeleton */
                skeleton = bPSkeleton;
                ComputingVertexPositions(skeleton);
            }
        }

        public void UseAnimation(string animName)
        {
            if (curAnim.AnimName != animName)
            {
                for (int q = 0; q < animations.Count; q++)
                {
                    if (animations[q].AnimName == animName)
                    {
                        curAnim = animations[q];
                        break;
                    }
                }
            }
        }


        public int CurrentFrame => animated ? curAnim.CurrentFrame : 0;
        public int TotalFrames  => animated ? curAnim.NumFrames    : 0;

        public void SeekToFrame(int frame)
        {
            if (!animated) return;
            int clamped = Math.Max(0, Math.Min(frame, curAnim.NumFrames - 1));
            curAnim.CurrentFrame = clamped;
            curAnim.NextFrame    = (clamped + 1) % curAnim.NumFrames;
            curAnim.LastTime     = 0f;
            InterpolateSkeletons(ref curAnim.SkelFrames, curAnim.CurrentFrame, curAnim.NextFrame, curAnim.NumJoints, 0f);
            ComputingVertexPositions(skeleton);
        }

        private void ComputingVertexPositions(MD5Joint[] skeleton)
        {
            meshes.Clear();

            //Calculate the final Position ingame Position of all the model vertexes
            for (int k = 0; k < numMeshes; k++)
            {
                finalVertices = new Vector3[model[k].NumVert];
                Vector3[] skinnedNormals = new Vector3[model[k].NumVert];
                Vector3[] skinnedTangents = new Vector3[model[k].NumVert];
                Vector3[] skinnedBitangents = new Vector3[model[k].NumVert];
                Vertex[] vertices = new Vertex[model[k].NumTris * 3];

                //Setup vertices 
                for (int i = 0; i < model[k].NumVert; i++)
                {
                    //Calculate final vertex to draw with weights
                    Vector3 finalVertex = new Vector3(0, 0, 0);
                    Vector3 finalNormal = Vector3.Zero;
                    Vector3 finalTangent = Vector3.Zero;
                    Vector3 finalBitangent = Vector3.Zero;
                    for (int j = 0; j < model[k].Verts[i].CountWeight; j++)
                    {
                        MD5Weight weight = model[k].Weights[model[k].Verts[i].StartWeight + j];
                        MD5Joint joint = skeleton[weight.Joint];

                        //Calculate transformed vertex for this weight
                        Vector3 wv = MyMath.RotatePoint(ref joint.Orient, ref weight.Pos);

                        //The sum of all weight->bias should be 1.0
                        finalVertex.X += (joint.Pos.X + wv.X) * weight.Bias;
                        finalVertex.Y += (joint.Pos.Y + wv.Y) * weight.Bias;
                        finalVertex.Z += (joint.Pos.Z + wv.Z) * weight.Bias;

                        if (bindPoseBasisReady)
                        {
                            Vector3 localNormal = weight.Normal;
                            Vector3 localTangent = weight.Tangent;
                            Vector3 localBitangent = weight.Bitangent;
                            finalNormal += MyMath.RotatePoint(ref joint.Orient, ref localNormal) * weight.Bias;
                            finalTangent += MyMath.RotatePoint(ref joint.Orient, ref localTangent) * weight.Bias;
                            finalBitangent += MyMath.RotatePoint(ref joint.Orient, ref localBitangent) * weight.Bias;
                        }
                    }
                    finalVertices[i] = finalVertex;

                    if (bindPoseBasisReady)
                    {
                        Vector3 n = SafeNormalize(finalNormal);
                        Vector3 t = SafeNormalize(finalTangent - n * Vector3.Dot(n, finalTangent));
                        Vector3 b = SafeNormalize(Vector3.Cross(n, t));
                        if (Vector3.Dot(b, finalBitangent) < 0f) b = -b;
                        skinnedNormals[i] = n;
                        skinnedTangents[i] = t;
                        skinnedBitangents[i] = b;
                    }
                }

                int count = 0;
                // Organize the final vertices acording to the meshes triangles
                for (int i = 0; i < model[k].NumTris; i++)
                {
                    int i0 = model[k].Faces[i][0];
                    int i1 = model[k].Faces[i][1];
                    int i2 = model[k].Faces[i][2];
                    vertices[count + 0] = new Vertex(finalVertices[i0], skinnedNormals[i0], model[k].Verts[i0].UV, skinnedTangents[i0], skinnedBitangents[i0]);
                    vertices[count + 1] = new Vertex(finalVertices[i1], skinnedNormals[i1], model[k].Verts[i1].UV, skinnedTangents[i1], skinnedBitangents[i1]);
                    vertices[count + 2] = new Vertex(finalVertices[i2], skinnedNormals[i2], model[k].Verts[i2].UV, skinnedTangents[i2], skinnedBitangents[i2]);

                    count += 3;
                }
                meshes.Add(vertices);
            }
        }

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
                    Vector3 faceNormal = Vector3.Cross(edge1, edge2);
                    normalAccum[i0] += faceNormal;
                    normalAccum[i1] += faceNormal;
                    normalAccum[i2] += faceNormal;

                    Vector2 duv1 = mesh.Verts[i1].UV - mesh.Verts[i0].UV;
                    Vector2 duv2 = mesh.Verts[i2].UV - mesh.Verts[i0].UV;
                    float determinant = duv1.X * duv2.Y - duv2.X * duv1.Y;
                    if (MathF.Abs(determinant) < 0.000001f) continue;

                    float inverse = 1f / determinant;
                    Vector3 tangent = (edge1 * duv2.Y - edge2 * duv1.Y) * inverse;
                    Vector3 bitangent = (edge2 * duv1.X - edge1 * duv2.X) * inverse;
                    tangentAccum[i0] += tangent;
                    tangentAccum[i1] += tangent;
                    tangentAccum[i2] += tangent;
                    bitangentAccum[i0] += bitangent;
                    bitangentAccum[i1] += bitangent;
                    bitangentAccum[i2] += bitangent;
                }

                for (int vertexIndex = 0; vertexIndex < mesh.NumVert; vertexIndex++)
                {
                    Vector3 n = SafeNormalize(normalAccum[vertexIndex]);
                    Vector3 t = tangentAccum[vertexIndex] - n * Vector3.Dot(n, tangentAccum[vertexIndex]);
                    if (t.LengthSquared < 0.000001f)
                    {
                        t = Vector3.Cross(n, MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY);
                    }
                    t = SafeNormalize(t);
                    Vector3 b = SafeNormalize(Vector3.Cross(n, t));
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

            bindPoseBasisReady = true;
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
                    Vector3 weightPosition = weight.Pos;
                    position += (joint.Pos + MyMath.RotatePoint(ref joint.Orient, ref weightPosition)) * weight.Bias;
                }
                positions[i] = position;
            }

            return positions;
        }

        private static Vector3 SafeNormalize(Vector3 value)
        {
            if (value.LengthSquared < 0.000001f)
            {
                return Vector3.UnitX;
            }

            value.Normalize();
            return value;
        }

        public Vertex[] GetCombinedVertices()
        {
            int total = 0;
            for (int i = 0; i < meshes.Count; i++)
            {
                total += meshes[i].Length;
            }

            Vertex[] combined = new Vertex[total];
            int offset = 0;
            for (int i = 0; i < meshes.Count; i++)
            {
                Array.Copy(meshes[i], 0, combined, offset, meshes[i].Length);
                offset += meshes[i].Length;
            }

            return combined;
        }


        private void CleanString(ref string str)
        {
            str = str.Replace("\t", " ");
            str = str.Replace("\"", " ");
            str = str.Trim();

            str = RemoveExtraSpaces(str);
        }

        //Removes all but single spaces from a string so "some string" goes to "some string"
        private string RemoveExtraSpaces(string str)
        {
            if (!str.Contains("  ")) return (str);
            return (RemoveExtraSpaces(str.Replace("  ", " ")));
        }

        private bool ParseLine(ref TextBuffer buffer, string txt, string str)
        {
            int countFloat = 0, countInt = 0;

            CleanString(ref txt);

            txt = txt.ToLower();
            str = str.ToLower();

            string[] splitTxt = txt.Split(new Char[] { ' ' });
            string[] splitStr = str.Split(new Char[] { ' ' });

            for (int i = 0; i < str.Length - 1; i++)
            {
                if (str[i] == '%' && str[i + 1] == 'i') countInt++;
                if (str[i] == '%' && str[i + 1] == 'f') countFloat++;
            }

            buffer.Ints = new int[countInt];
            buffer.Floats = new float[countFloat];

            countFloat = countInt = 0;

            for (int i = 0; i < splitStr.Length; i++)
            {   //Integer
                if (string.Equals(splitStr[i], "%i"))
                {
                    buffer.Ints[countInt] = int.Parse(splitTxt[i]);
                    countInt++;
                }
                else //Float
                    if (string.Equals(splitStr[i], "%f"))
                    {
                        buffer.Floats[countFloat] = float.Parse(splitTxt[i], CultureInfo.InvariantCulture);
                        countFloat++;
                    } //String
                    else if (string.Equals(splitStr[i], "%s"))
                        buffer.String = splitTxt[i];
                    else if (!string.Equals(splitStr[i], splitTxt[i]))
                        return (false);
            }
            return (true);
        }
    }
}

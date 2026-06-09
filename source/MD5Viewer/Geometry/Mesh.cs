using System.Collections.Generic;
using System.Drawing;

namespace MD5Viewer
{
    class Mesh
    {
        #region Private Member Variables

        private List<Node> nodes = null;
        private List<Edge> edges = null;
        private List<Face> faces = null;

        private bool normalized = false;
        private Color color = Color.Green;

        #endregion

        #region Public Properties

        /// <summary>
        /// A hálót alkotó csomópontok listája
        /// </summary>
        public List<Node> Nodes
        {
            get { return nodes; }
            set { nodes = value; }
        }

        /// <summary>
        /// A hálót alkotó élek listája
        /// </summary>
        public List<Edge> Edges
        {
            get { return edges; }
            set { edges = value; }
        }

        /// <summary>
        /// A hálót alkotó háromszögek listája
        /// </summary>
        public List<Face> Faces
        {
            get { return faces; }
            set { faces = value; }
        }

        /// <summary>
        /// Indexelő a hálót alkotó háromszögekhez
        /// </summary>
        /// <param name="index">Index a háló i-edik háromszögére</param>
        /// <returns>Visszatér a háromszöggel</returns>
        public Face this[int index]
        {
            get { return faces[index]; }
        }

        /// <summary>
        /// Igaz (true), ha a normelvektorok léteznek, egyébként hamis (false)
        /// </summary>
        public bool Normalized
        {
            get { return normalized; }
            set { normalized = value; }
        }

        /// <summary>
        /// A háló színe
        /// </summary>
        public Color Color
        {
            get { return color; }
            set { color = value; }
        }

        #endregion

        #region Constructors

        public Mesh()
        {
            nodes = new List<Node>();
            edges = new List<Edge>();
            faces = new List<Face>();
        }

        #endregion

        #region Public Methods

        #region Topology Methods

        /// <summary>
        /// A csomópontokhoz kapcsolódó háromszögek beállítása
        /// </summary>
        public void BuildNodeConnections()
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                if (Faces[i].FaceType == Face.FType.Normal)
                {
                    Nodes[Faces[i].NodeIndices[0]].AddFace(i);
                    Nodes[Faces[i].NodeIndices[1]].AddFace(i);
                    Nodes[Faces[i].NodeIndices[2]].AddFace(i);
                }
            }
        }

        /// <summary>
        /// A topológiai kapcsolatot építi fel
        /// </summary>
        public void BuildTopology()
        {
            //A csomópontokhoz kapcsolódó háromszögek beállítása
            BuildNodeConnections();

            for (int Face1 = 0; Face1 < Faces.Count; Face1++)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (faces[Face1].FaceIndices[0] == -1)
                    {
                        for (int iB = 0; iB < nodes[faces[Face1].NodeIndices[1]].LinkedFaces.Count; iB++)
                        {
                            int Face2 = nodes[faces[Face1].NodeIndices[1]].LinkedFaces[iB];

                            if (Face2 != Face1)
                            {
                                int ii = 0;

                                while (faces[Face2].NodeIndices[2] != faces[Face1].NodeIndices[1] || ii > 3)
                                {
                                    faces[Face2].RotateLeft();
                                    ii++;
                                }

                                if (faces[Face2].NodeIndices[1] == faces[Face1].NodeIndices[2])
                                {
                                    faces[Face1].FaceIndices[0] = Face2;
                                    faces[Face2].FaceIndices[0] = Face1;
                                    break;
                                }
                            }
                        }
                    }
                    faces[Face1].RotateLeft();
                }
            }
            CalculateNormals();
        }

        /// <summary>
        /// Két szomszédos háromszög közös élét átbillenti, ha létezik
        /// </summary>
        /// <param name="AFace">Index az egyik szomszédos háromszögre</param>
        /// <param name="BFace">Index a másik szomszédos háromszögre</param>
        public void Flip(int AFace, int BFace)
        {
            if (Faces[AFace].IndexToFaceA == BFace)
            {
                Flip(AFace);
            }
            else if (Faces[AFace].IndexToFaceB == BFace || Faces[AFace].IndexToFaceC == BFace)
            {
                while (Faces[AFace].IndexToFaceA != BFace)
                {
                    Faces[AFace].RotateLeft();
                }
                ConnectTriangles(AFace, BFace);
                Flip(AFace);
            }
            else return;
        }

        /// <summary>
        /// A háromszög A csúcspontjával szembeni élét átbillenti, ha lehetséges
        /// </summary>
        /// <param name="Index">Index a háromszögre</param>
        public void Flip(int FaceIndex)
        {
            if (FaceIndex < 0 || FaceIndex >= Faces.Count) return;

            int NeighbourFaceIndex = Faces[FaceIndex].FaceIndices[0];

            if (NeighbourFaceIndex < 0 || NeighbourFaceIndex >= Faces.Count) return;

            ConnectTriangles(FaceIndex, NeighbourFaceIndex);

            int PA = Faces[FaceIndex].NodeIndices[1];
            int PC = Faces[FaceIndex].NodeIndices[2];
            int PD = Faces[FaceIndex].NodeIndices[0];
            int PB = Faces[NeighbourFaceIndex].NodeIndices[0];

            int TA = Faces[NeighbourFaceIndex].FaceIndices[1];
            int TC = Faces[FaceIndex].FaceIndices[1];
            int TD = Faces[FaceIndex].FaceIndices[2];
            int TB = Faces[NeighbourFaceIndex].FaceIndices[2];

            if (PB == -1 || PD == -1) return;

            Faces[FaceIndex].NodeIndices[0] = PA;
            Faces[FaceIndex].NodeIndices[1] = PB;
            Faces[FaceIndex].NodeIndices[2] = PD;

            Faces[NeighbourFaceIndex].NodeIndices[0] = PC;
            Faces[NeighbourFaceIndex].NodeIndices[1] = PD;
            Faces[NeighbourFaceIndex].NodeIndices[2] = PB;

            ConnectTriangles(FaceIndex, NeighbourFaceIndex);
            Faces[FaceIndex].RotateLeft();
            ConnectTriangles(FaceIndex, TD);
            Faces[FaceIndex].RotateLeft();
            ConnectTriangles(FaceIndex, TA);
            Faces[FaceIndex].RotateLeft();

            Faces[NeighbourFaceIndex].RotateLeft();
            ConnectTriangles(NeighbourFaceIndex, TB);
            Faces[NeighbourFaceIndex].RotateLeft();
            ConnectTriangles(NeighbourFaceIndex, TC);
            Faces[NeighbourFaceIndex].RotateLeft();
        }

        #endregion

        /// <summary>
        /// Kiszámoljuk a csúcspontbeli normálisokat
        /// </summary>
        public void CalculateNormals()
        {
            foreach (Face face in faces)
            {
                if (face.FaceType == Face.FType.Normal)
                {
                    Vector3d A = nodes[face[0]].Vertex;
                    Vector3d B = nodes[face[1]].Vertex;
                    Vector3d C = nodes[face[2]].Vertex;

                    face.FaceNormal = new Vector3d(((B - A) % (C - A)));
                    face.FaceNormal.Normalize();
                }
            }

            foreach (Node node in nodes)
            {
                Vector3d sumNormal = new Vector3d();

                foreach (int index in node.LinkedFaces)
                {
                    sumNormal += faces[index].FaceNormal;
                }
                node.Normal = (sumNormal / node.LinkedFaces.Count);
            }
            normalized = true;
        }

        /// <summary>
        /// AFace és BFace összefűzése a közös élük mentén, ha lehetséges
        /// </summary>
        /// <param name="AFace"></param>
        /// <param name="BFace"></param>
        public void ConnectTriangles(int AFace, int BFace)
        {
            if (AFace < 0 || AFace >= Faces.Count) return;

            Faces[AFace].FaceIndices[0] = BFace;

            if (BFace < 0 || BFace >= Faces.Count) return;

            for (int i = 0; i < 4; i++)
            {
                if (Faces[BFace].NodeIndices[1] == Faces[AFace].NodeIndices[2])
                {
                    Faces[BFace].FaceIndices[0] = AFace;
                    break;
                }
                Faces[BFace].RotateLeft();
            }
        }

        #endregion
    }
}

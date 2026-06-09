using System;

namespace MD5Viewer
{
    [Serializable()]
    class Edge : IEquatable<Edge>
    {
        #region Private Member Variables

        /// <summary>
        /// Index az él kezdő- és végpontjára
        /// </summary>
        private int[] nodeIndex = new int[] { -1, -1 };

        /// <summary>
        /// Index az él első- és második háromszögére
        /// </summary>
        private int[] faceIndex = new int[] { -1, -1 };

        #endregion

        #region Public properties

        /// <summary>
        /// Index az él kezdőpontjához (NodeIndex[0])
        /// </summary>
        public int StartNode
        {
            get { return nodeIndex[0]; }
            set { nodeIndex[0] = value; }
        }

        /// <summary>
        /// Index az él végpontjához (NodeIndex[1])
        /// </summary>
        public int EndNode
        {
            get { return nodeIndex[1]; }
            set { nodeIndex[1] = value; }
        }

        /// <summary>
        /// Index az él baloldali háromszögéhez (FaceIndex[0])
        /// </summary>
        public int LeftFace
        {
            get { return faceIndex[0]; }
            set { faceIndex[0] = value; }
        }

        /// <summary>
        /// Index az él joboldali háromszögéhez (FaceIndex[1])
        /// </summary>
        public int RightFace
        {
            get { return faceIndex[1]; }
            set { faceIndex[1] = value; }
        }

        /// <summary>
        /// Index az él kezdő- és végpontjához: NodeIndex[0] -> StartNode és NodeIndex[1] -> EndNode
        /// </summary>
        public int[] NodeIndex
        {
            get { return nodeIndex; }
            set { nodeIndex = value; }
        }

        /// <summary>
        /// Index az él bal- és jobboldali háromszögéhez: FaceIndex[0] -> LeftFace és FaceIndex[1] -> RightFace
        /// </summary>
        public int[] FaceIndex
        {
            get { return faceIndex; }
            set { faceIndex = value; }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Az él indexei -1 értéket vesznek fel, a végtelenbe mutatnak
        /// </summary>
        public Edge()
        {
            nodeIndex = new int[] { -1, -1 };
            faceIndex = new int[] { -1, -1 };
        }

        /// <summary>
        /// Az él indexei a megadot csomópontokra mutatnak
        /// </summary>
        /// <param name="StartNode">Az él kezdőpontjára mutató index</param>
        /// <param name="EndNode">Az él végpontjára mutató index</param>
        public Edge(int StartNode, int EndNode)
        {
            this.StartNode = StartNode;
            this.EndNode = EndNode;
        }

        /// <summary>
        /// Az él indexei a megadot él csomópontjaira és szomszédjaira mutatnak
        /// </summary>
        /// <param name="edge">A megadott él</param>
        public Edge(Edge edge)
        {
            this.NodeIndex = edge.NodeIndex;
            this.FaceIndex = edge.FaceIndex;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Az él másolata
        /// </summary>
        /// <returns>Edge</returns>
        public Edge Clone()
        {
            return (this);
        }

        /// <summary>
        /// Az él tulajdonságainak szöveges megjelenítése
        /// </summary>
        /// <returns>String</returns>
        public override string ToString()
        {
            return String.Format("({0},{1})({2},{3}) ", nodeIndex[0], nodeIndex[1], faceIndex[0], faceIndex[1]);
        }

        /// <summary>
        /// Két él megegyezik, ha csomópontjaik azonosak
        /// </summary>
        /// <param name="other">Összehasonlítandó él</param>
        /// <returns>Ha megegyeznek akkor (true) egyébként (false)</returns>
        public bool Equals(Edge other)
        {
            return ((this.StartNode == other.EndNode) && (this.EndNode == other.StartNode)) ||
                   ((this.StartNode == other.StartNode) && (this.EndNode == other.EndNode));
        }

        #endregion

        #region ISerializable Members

        #endregion
    }
}

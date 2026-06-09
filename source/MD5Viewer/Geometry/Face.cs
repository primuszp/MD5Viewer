using System;
using System.Runtime.Serialization;

namespace MD5Viewer
{
    [Serializable()]
    class Face : IEquatable<Face>
    {
        public enum FType : byte
        {
            Normal = 0, Shadow = 1, Unvisible = 2,
        }

        #region Private Member Variables

        /// <summary>
        /// Index a háromszög csomópontjaihoz
        /// </summary>
        private int[] inode = new int[] { -1, -1, -1 };

        /// <summary>
        /// Index a háromszög szomszédjaihoz
        /// </summary>
        private int[] iface = new int[] { -1, -1, -1 };

        /// <summary>
        /// A háromszög normálvektora
        /// </summary>
        private Vector3d faceNormal = new Vector3d();

        /// <summary>
        /// A háromszög típusa
        /// </summary>
        private FType faceType = FType.Normal;

        /// <summary>
        /// A háromszöghöz kapcsolt anyag neve
        /// </summary>
        private string materialName = "Standard";

        #endregion

        #region Public Properties

        /// <summary>
        /// Index a háromszög első csomópontjára (NodeIndices[0] -> A)
        /// </summary>
        public int IndexToNodeA
        {
            get { return inode[0]; }
            set { inode[0] = value; }
        }

        /// <summary>
        /// Index a háromszög második csomópontjára (NodeIndices[1] -> B)
        /// </summary>
        public int IndexToNodeB
        {
            get { return inode[1]; }
            set { inode[1] = value; }
        }

        /// <summary>
        /// Index a háromszög harmadik csomópontjára (NodeIndices[2] -> C)
        /// </summary>
        public int IndexToNodeC
        {
            get { return inode[2]; }
            set { inode[2] = value; }
        }

        /// <summary>
        /// Index a háromszög csomópontjaihoz(index [0] -> A, [1] -> B, [2] -> C)
        /// </summary>
        public int[] NodeIndices
        {
            get { return inode; }
            set { inode = value; }
        }

        /// <summary>
        /// A háromszög csomópontjainak tulajdonsága                      
        /// </summary>         
        /// <param name="index">Index a háromszög csomópontjaira: index [0] -> A, [1] -> B, [2] -> C</param>
        /// <returns>Visszatér a csomópont indexével</returns>
        public int this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: { return inode[0]; }
                    case 1: { return inode[1]; }
                    case 2: { return inode[2]; }
                    default: throw new ArgumentException(THREE_NODES, "index");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: { inode[0] = value; break; }
                    case 1: { inode[1] = value; break; }
                    case 2: { inode[2] = value; break; }
                    default: throw new ArgumentException(THREE_NODES, "index");
                }
            }
        }

        /// <summary>
        /// Index a háromszög első szomszédjához (FaceIndices[0] -> A)
        /// </summary>
        public int IndexToFaceA
        {
            get { return iface[0]; }
            set { iface[0] = value; }
        }

        /// <summary>
        /// Index a háromszög második szomszédjához(FaceIndices[1] -> B)
        /// </summary>
        public int IndexToFaceB
        {
            get { return iface[1]; }
            set { iface[1] = value; }
        }

        /// <summary>
        /// Index a háromszög harmadik szomszédjához (FaceIndices[2] -> C)
        /// </summary>
        public int IndexToFaceC
        {
            get { return iface[2]; }
            set { iface[2] = value; }
        }

        /// <summary>
        /// Index a háromszög szomszédjaihoz (index [0] -> A, [1] -> B, [2] -> C)
        /// </summary>
        public int[] FaceIndices
        {
            get { return iface; }
            set { iface = value; }
        }

        /// <summary>
        /// A háromszög normálvektora
        /// </summary>
        public Vector3d FaceNormal
        {
            get { return faceNormal; }
            set { faceNormal = value; }
        }

        /// A háromszög típusa
        /// </summary>
        public FType FaceType
        {
            get { return faceType; }
            set { faceType = value; }
        }

        /// <summary>
        /// A háromszöghöz kapcsolt anyag neve
        /// </summary>
        public string MaterialName
        {
            get { return materialName; }
            set { materialName = value; }
        }

        #endregion

        #region Constructors

        public Face()
        {

        }

        public Face(int indexToNodeA, int indexToNodeB, int indexToNodeC)
        {
            this.inode[0] = indexToNodeA;
            this.inode[1] = indexToNodeB;
            this.inode[2] = indexToNodeC;
        }

        public Face(int[] nodes)
        {
            this.inode[0] = nodes[0];
            this.inode[1] = nodes[1];
            this.inode[2] = nodes[2];
        }

        public Face(Face face)
        {
            this.inode = face.inode;
            this.iface = face.iface;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Balra forgatja a háromszög indexeit
        /// </summary>
        public void RotateLeft()
        {
            int nodetemp = inode[0];
            int facetemp = iface[0];

            for (int i = 0; i < 2; i++)
            {
                inode[i] = inode[i + 1];
                iface[i] = iface[i + 1];
            }

            inode[2] = nodetemp;
            iface[2] = facetemp;
        }

        /// <summary>
        /// Jobbra forgatja a háromszög indexeit
        /// </summary>
        public void RotateRight()
        {
            int nodetemp = inode[2];
            int facetemp = iface[2];

            for (int i = 2; i > 0; i--)
            {
                inode[i] = inode[i - 1];
                iface[i] = iface[i - 1];
            }

            inode[0] = nodetemp;
            iface[0] = facetemp;
        }

        public Face Clone()
        {
            return (this);
        }

        public override string ToString()
        {
            return ("[" + iface[0] + ", " + iface[1] + ", " + iface[2] + "]");
        }

        /// <summary>
        /// Két háromszög azonos, ha ugyanazok a csomópontok alkotják öket
        /// </summary>
        /// <param name="other">Összehasonlítandó háromszög</param>
        /// <returns>Ha megegyeznek akkor (true) egyébként (false)</returns>
        public bool Equals(Face other)
        {
            return ((this.NodeIndices[0] == other.NodeIndices[0]) && (this.NodeIndices[1] == other.NodeIndices[1]) && (this.NodeIndices[2] == other.NodeIndices[2]));
        }

        #endregion

        #region Messages

        /// <summary>
        /// Az üzenet megjelenítésre kerül, ha nem megfelelő nagyságú mutatót alkalmazunk
        /// </summary>
        private const string THREE_NODES = "A mutató nem lehet nagyobb mint a háromszög csúcspontjainak száma!";

        #endregion
    }
}

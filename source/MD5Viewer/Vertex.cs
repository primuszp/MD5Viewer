using OpenTK.Mathematics;

namespace MD5Viewer
{
    public struct Vertex
    {
        private Vector3 position;
        private Vector2 texture;
        private Vector3 normal;
        private Vector3 tangent;
        private Vector3 bitangent;

        public Vector3 Position
        {
            get { return position; }
            set { position = value; }
        }

        public Vector3 Normal
        {
            get { return normal; }
            set { normal = value; }
        }

        public Vector2 Texture
        {
            get { return texture; }
            set { texture = value; }
        }

        public Vector3 Tangent
        {
            get { return tangent; }
            set { tangent = value; }
        }

        public Vector3 Bitangent
        {
            get { return bitangent; }
            set { bitangent = value; }
        }

        public Vertex(Vector3 position)
        {
            this.position = position;
            this.texture = new Vector2(1, 1);
            this.normal = new Vector3(0, 0, 1);
            this.tangent = new Vector3(1, 0, 0);
            this.bitangent = new Vector3(0, 1, 0);
        }

        public Vertex(Vector3 position, Vector3 normal)
        {
            this.position = position;
            this.normal = normal;
            this.texture = new Vector2(1, 1);
            this.tangent = new Vector3(1, 0, 0);
            this.bitangent = new Vector3(0, 1, 0);
        }

        public Vertex(Vector3 position, Vector3 normal, Vector2 texture)
        {
            this.position = position;
            this.normal = normal;
            this.texture = texture;
            this.tangent = new Vector3(1, 0, 0);
            this.bitangent = new Vector3(0, 1, 0);
        }

        public Vertex(Vector3 position, Vector3 normal, Vector2 texture, Vector3 tangent, Vector3 bitangent)
        {
            this.position = position;
            this.normal = normal;
            this.texture = texture;
            this.tangent = tangent;
            this.bitangent = bitangent;
        }
    }
}

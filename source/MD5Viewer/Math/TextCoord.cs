namespace MD5Viewer
{
    public struct TexCoord
    {
        private float u;
        private float v;

        public float U { get { return u; } set { u = value; } }
        public float V { get { return v; } set { v = value; } }

        public TexCoord(float u, float v)
        {
            this.u = u;
            this.v = v;
        }
    }
}

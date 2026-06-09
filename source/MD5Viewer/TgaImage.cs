namespace MD5Viewer
{
    internal sealed class TgaImage
    {
        private TgaImage(int width, int height, byte[] rgba)
        {
            Width = width;
            Height = height;
            Rgba = rgba;
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Rgba { get; }

        public static TgaImage Load(string file)
        {
            using BinaryReader reader = new BinaryReader(File.OpenRead(file));

            byte idLength = reader.ReadByte();
            byte colorMapType = reader.ReadByte();
            byte imageType = reader.ReadByte();

            reader.BaseStream.Seek(5, SeekOrigin.Current);
            reader.BaseStream.Seek(4, SeekOrigin.Current);

            int width = reader.ReadUInt16();
            int height = reader.ReadUInt16();
            byte bitsPerPixel = reader.ReadByte();
            byte descriptor = reader.ReadByte();

            if (colorMapType != 0)
            {
                throw new NotSupportedException("Color mapped TGA files are not supported.");
            }

            if (imageType != 2 && imageType != 3 && imageType != 10 && imageType != 11)
            {
                throw new NotSupportedException("Only true-color and grayscale TGA files are supported.");
            }

            int bytesPerPixel = bitsPerPixel / 8;
            if (bytesPerPixel != 1 && bytesPerPixel != 3 && bytesPerPixel != 4)
            {
                throw new NotSupportedException("Only 8, 24 and 32 bit TGA files are supported.");
            }

            if (idLength > 0)
            {
                reader.BaseStream.Seek(idLength, SeekOrigin.Current);
            }

            byte[] rgba = new byte[width * height * 4];
            bool topOrigin = (descriptor & 0x20) != 0;
            bool rightOrigin = (descriptor & 0x10) != 0;
            bool rle = imageType == 10 || imageType == 11;

            if (rle)
            {
                ReadRle(reader, rgba, width, height, bytesPerPixel, topOrigin, rightOrigin);
            }
            else
            {
                for (int i = 0; i < width * height; i++)
                {
                    ReadPixel(reader, out byte r, out byte g, out byte b, out byte a, bytesPerPixel);
                    WritePixel(rgba, i, width, height, topOrigin, rightOrigin, r, g, b, a);
                }
            }

            return new TgaImage(width, height, rgba);
        }

        private static void ReadRle(BinaryReader reader, byte[] rgba, int width, int height, int bytesPerPixel, bool topOrigin, bool rightOrigin)
        {
            int pixelIndex = 0;
            int pixelCount = width * height;

            while (pixelIndex < pixelCount)
            {
                byte packet = reader.ReadByte();
                int count = (packet & 0x7F) + 1;
                bool runLengthPacket = (packet & 0x80) != 0;

                if (runLengthPacket)
                {
                    ReadPixel(reader, out byte r, out byte g, out byte b, out byte a, bytesPerPixel);
                    for (int i = 0; i < count && pixelIndex < pixelCount; i++)
                    {
                        WritePixel(rgba, pixelIndex++, width, height, topOrigin, rightOrigin, r, g, b, a);
                    }
                }
                else
                {
                    for (int i = 0; i < count && pixelIndex < pixelCount; i++)
                    {
                        ReadPixel(reader, out byte r, out byte g, out byte b, out byte a, bytesPerPixel);
                        WritePixel(rgba, pixelIndex++, width, height, topOrigin, rightOrigin, r, g, b, a);
                    }
                }
            }
        }

        private static void ReadPixel(BinaryReader reader, out byte r, out byte g, out byte b, out byte a, int bytesPerPixel)
        {
            if (bytesPerPixel == 1)
            {
                r = g = b = reader.ReadByte();
                a = 255;
                return;
            }

            b = reader.ReadByte();
            g = reader.ReadByte();
            r = reader.ReadByte();
            a = bytesPerPixel == 4 ? reader.ReadByte() : (byte)255;
        }

        private static void WritePixel(byte[] rgba, int sourceIndex, int width, int height, bool topOrigin, bool rightOrigin, byte r, byte g, byte b, byte a)
        {
            int x = sourceIndex % width;
            int y = sourceIndex / width;

            if (!topOrigin)
            {
                y = height - 1 - y;
            }

            if (rightOrigin)
            {
                x = width - 1 - x;
            }

            int target = ((y * width) + x) * 4;
            rgba[target + 0] = r;
            rgba[target + 1] = g;
            rgba[target + 2] = b;
            rgba[target + 3] = a;
        }
    }
}

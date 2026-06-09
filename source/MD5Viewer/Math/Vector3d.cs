using System;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace MD5Viewer
{
    [Serializable()]
    public sealed class Vector3d : IComparable, IComparable<Vector3d>, IEquatable<Vector3d>, ISerializable
    {
        #region Private Member Variables

        /// <summary>
        /// A vektor X komponense
        /// /// </summary>
        private double x;

        /// <summary>
        /// A vektor Y komponense
        /// </summary>
        private double y;

        /// <summary>
        /// A vektor Z komponense
        /// </summary>
        private double z;

       /// <summary>
       /// A vektor hossza
       /// </summary>
        private double length;

        #endregion

        #region Public Properties

        /// <summary>
        /// A vektor X komponensének tulajdonsága
        /// </summary>
        public double X
        {
            get { return x; }
            set 
            { 
                x = value;
                length = CalcLength();
            }
        }

        /// <summary>
        ///  A vektor Y komponensének tulajdonsága
        /// </summary>
        public double Y
        {
            get { return y; }
            set 
            { 
                y = value;
                length = CalcLength();
            }
        }

        /// <summary>
        ///  A vektor Z komponensének tulajdonsága
        /// </summary>
        public double Z
        {
            get { return z; }
            set 
            { 
                z = value;
                length = CalcLength();
            }
        }

        /// <summary>
        /// Indexelő a vektor komponenseire: vektor index [0] -> X, [1] -> Y és [2] -> Z
        /// </summary>
        /// <param name="index">Index a vektor komponenseire</param>
        /// <exception cref="System.ArgumentException">
        /// Kivétel történik, ha a mutató nagyobb mint a vektor komponenseinek száma (3)
        /// </exception>
        public double this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: { return x; }
                    case 1: { return y; }
                    case 2: { return z; }
                    default: throw new ArgumentException(THREE_COMPONENTS, "index");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: { x = value; break; }
                    case 1: { y = value; break; }
                    case 2: { z = value; break; }
                    default: throw new ArgumentException(THREE_COMPONENTS, "index");
                }
                length = CalcLength();
            }
        }

        /// <summary>
        /// Tulajdonság a vektor háromelemű tömbként való értelmezéséhez
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// Kivétel történik, ha nem háromelemű (x,y,z) tömböt adunk meg
        /// </exception> 
        [XmlIgnore]
        public double[] Array
        {
            get
            {
                return (new double[] { x, y, z });
            }
            set
            {
                if (value.Length == 3)
                {
                    x = value[0];
                    y = value[1];
                    z = value[2];
                    length = CalcLength();
                }
                else
                {
                    throw new ArgumentException(THREE_COMPONENTS);
                }
            }
        }

        /// <summary>
        /// A vektor hossza, csak olvasható tulajdonság
        /// </summary>
        public double Length
        {
            get { return (length); }
        }

        /// <summary>
        /// A vektor hosszának négyzete, csak olvasható tulajdonság
        /// </summary>
        public double LengthSquared
        {
            get { return (CalcLengthSquared()); }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A vektor összes komponense nulla lesz
        /// </summary>
        public Vector3d()
            : this(0, 0, 0)
        {
        }

        /// <summary>
        /// A vektor z komponense mindig nulla lesz
        /// </summary>
        /// <param name="x">A vektor x komponense</param>
        /// <param name="y">A vektor y komponense</param>
        public Vector3d(double X, double Y)
        {
            this.X = X;
            this.Y = Y;
            this.Z = 0;
            this.length = this.CalcLength();
        }

        /// <summary>
        /// A vektor összes komponense a megadot értéket veszi fel
        /// </summary>
        /// <param name="x">A vektor x komponense</param>
        /// <param name="y">A vektor y komponense</param>
        /// <param name="z">A vektor z komponense</param>
        public Vector3d(double X, double Y, double Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
            this.length = this.CalcLength();
        }

        /// <summary>
        /// A vektort háromelemű tömbként tároljuk: [x,y,z]
        /// </summary>
        /// <param name="xyz">A vektor komponenseit tartalmazó tömb</param>
        public Vector3d(double[] XYZ)
        {
            if (XYZ.Length == 3)
            {
                this.X = XYZ[0];
                this.Y = XYZ[1];
                this.Z = XYZ[2];
                this.length = this.CalcLength();
            }
            else
            {
                throw new ArgumentException(THREE_COMPONENTS);
            }
        }

        /// <summary>
        /// A vektor komponensei a megadott vektor komponenseivel lesz egyenlő
        /// </summary>
        /// <param name="vector">A megadott vektor</param>
        public Vector3d(Vector3d vector)
        {
            this.X = vector.X;
            this.Y = vector.Y;
            this.Z = vector.Z;
            this.length = this.CalcLength();
        }

        #endregion

        #region Public Methods

        #region Static Methods

        private static double DegreeToRadian(double angle)
        {
            return (Math.PI * angle / 180.0);
        }

        private static double RadianToDegree(double angle)
        {
            return (angle * (180.0 / Math.PI));
        }

        /// <summary>
        /// Két vektor összege
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Összeg vektor</returns>
        public static Vector3d Add(Vector3d vector, Vector3d other)
        {
            return (new Vector3d(vector.X + other.X, vector.Y + other.Y, vector.Z + other.Z));
        }

        /// <summary>
        /// Két vektor különbsége
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Különbségvektor</returns>
        public static Vector3d Sub(Vector3d vector, Vector3d other)
        {
            return (new Vector3d(vector.X - other.X, vector.Y - other.Y, vector.Z - other.Z));
        }

        /// <summary>
        /// Vektor szorzása skalárral
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="scalar">Második összetevő</param>
        /// <returns>Eredmény vektor</returns>
        public static Vector3d Mult(Vector3d vector, double scalar)
        {
            return (new Vector3d(vector.X * scalar, vector.Y * scalar, vector.Z * scalar));
        }

        /// <summary>
        /// Vektor osztása skalárral
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="scalar">Második összetevő</param>
        /// <returns>Eredmény vektor</returns>
        public static Vector3d Div(Vector3d vector, double scalar)
        {
            return (Mult(vector, 1.0 / scalar));
        }

        /// <summary>
        /// Két vektor vektoriális szorzata (crossproduct)
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Vektor</returns>
        /// <implementation>
        /// A vektoriális szorzás nem kommutatív
        /// </implementation>
        public static Vector3d CrossProduct(Vector3d vector, Vector3d other)
        {
            return (new Vector3d(vector.Y * other.Z - vector.Z * other.Y, vector.Z * other.X - vector.X * other.Z, vector.X * other.Y - vector.Y * other.X));
        }

        /// <summary>
        /// Két vektor skaláris szorzata (dot product)
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő)</param>
        /// <returns>Skalár</returns>
        public static double DotProduct(Vector3d vector, Vector3d other)
        {
            return (vector.X * other.X + vector.Y * other.Y + vector.Z * other.Z);
        }

        /// <summary>
        /// Három vektor vegyes szorzata (mixed product)
        /// </summary>
        /// <param name="vA">Első összetevő</param>
        /// <param name="vB">Második összetevő</param>
        /// <param name="vC">Harmadik összetevő</param>
        /// <returns>Skalár</returns>
        /// <implementation>
        /// A vegyes szorzat nem kommutatív
        /// </implementation>
        public static double MixedProduct(Vector3d vA, Vector3d vB, Vector3d vC)
        {
            return DotProduct(CrossProduct(vA, vB), vC);
        }

        /// <summary>
        /// Két vektor között értelmezett szög fokban
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Szög</returns>
        public static double Angle(Vector3d vector, Vector3d other)
        {
            return ((180.0 / Math.PI) * Math.Acos(Normalize(vector) * Normalize(other)));
        }

        /// <summary>
        /// Két vektor között értelmezett távolság
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Skalár</returns>
        public static double Distance(Vector3d vector, Vector3d other)
        {
            return Math.Sqrt((vector.X - other.X) * (vector.X - other.X) + (vector.Y - other.Y) * (vector.Y - other.Y) + (vector.Z - other.Z) * (vector.Z - other.Z));
        }

        /// <summary>
        /// A vektor normáltja
        /// </summary>
        /// <param name="vector">Vektor</param>
        /// <returns>Normált vektor</returns>
        public static Vector3d Normalize(Vector3d vector)
        {
            if (vector.length == 0.0)
            {
                return (new Vector3d(0, 0, 0));
            }
            else
            {
                return (new Vector3d(vector.X / vector.length, vector.Y / vector.length, vector.Z / vector.length));
            }
        }

        /// <summary>
        /// Y-tengely körüli forgatás (Yaw)
        /// </summary>
        /// <param name="vector">Az elforgatandó vektor</param>
        /// <param name="degree">A forgatás szöge fokban</param>
        /// <returns>Az Y-tengely körül elforgatott vektor</returns>
        public static Vector3d Yaw(Vector3d vector, double degree)
        {
            double x = (vector.Z * Math.Sin(degree)) + (vector.X * Math.Cos(degree));
            double y = (vector.Y);
            double z = (vector.Z * Math.Cos(degree)) - (vector.X * Math.Sin(degree));
            return new Vector3d(x, y, z);
        }

        /// <summary>
        /// X-tengely körüli forgatás (Pitch)
        /// </summary>
        /// <param name="vector">Az elforgatandó vektor</param>
        /// <param name="degree">A forgatás szöge fokban</param>
        /// <returns>Az X-tengely körül elforgatott vektor</returns>
        public static Vector3d Pitch(Vector3d vector, double degree)
        {
            double x = (vector.X);
            double y = (vector.Y * Math.Cos(degree)) - (vector.Z * Math.Sin(degree));
            double z = (vector.Y * Math.Sin(degree)) + (vector.Z * Math.Cos(degree));
            return new Vector3d(x, y, z);
        }

        /// <summary>
        /// Z-tengely körüli forgatás (Roll)
        /// </summary>
        /// <param name="Node">Az elforgatandó vektor</param>
        /// <param name="degree">A forgatás szöge fokban</param>
        /// <returns>A Z-tengely körül elforgatott vektor</returns>
        public static Vector3d Roll(Vector3d vector, double degree)
        {
            double x = (vector.X * Math.Cos(degree)) - (vector.Y * Math.Sin(degree));
            double y = (vector.X * Math.Sin(degree)) + (vector.Y * Math.Cos(degree));
            double z = (vector.Z);
            return new Vector3d(x, y, z);
        }

        /// <summary>
        /// Interpoláció 3 vektor között baricentrikus koordináták segítségével
        /// </summary>
        /// <param name="vA">A vektor</param>
        /// <param name="vB">B vektor</param>
        /// <param name="vC">C vektor</param>
        /// <param name="u">Első baricentrikus koordináta</param>
        /// <param name="v">Második baricentrikus koordináta</param>
        /// <returns>Ha u=0 és v=0 akkor vA, Ha u=1 és v=0 akkor vB, Ha u=0 és v=1 akkor vC, egyébként a három vektor lineáris kombinációja</returns>
        public static Vector3d BaryCentric(Vector3d vA, Vector3d vB, Vector3d vC, double u, double v)
        {
            return (vA + u * (vB - vA) + v * (vC - vA));
        }

        /// <summary>
        /// Interpoláció két vektor között a keverési tényező függvényében
        /// </summary>
        /// <param name="vA">A vektor</param>
        /// <param name="vB">B vektor</param>
        /// <param name="blend">Keverési tényező, blend[0..1] között értelmezett</param>
        /// <returns>A vektor ha blend=0.0 és B vektor ha blend=1.0, egyébként a két vektor lineáris kombinációja</returns>
        public static Vector3d Lerp(Vector3d vA, Vector3d vB, double blend)
        {
            vA.X = blend * (vB.X - vA.X) + vA.X;
            vA.Y = blend * (vB.Y - vA.Y) + vA.Y;
            vA.Z = blend * (vB.Z - vA.Z) + vA.Z;
            return (vA);
        }

        public static Vector3d ComponentMin(Vector3d vector, Vector3d other)
        {
            vector.X = vector.X < other.X ? vector.X : other.X;
            vector.Y = vector.Y < other.Y ? vector.Y : other.Y;
            vector.Z = vector.Z < other.Z ? vector.Z : other.Z;
            return (vector);
        }

        public static Vector3d ComponentMax(Vector3d vector, Vector3d other)
        {
            vector.X = vector.X > other.X ? vector.X : other.X;
            vector.Y = vector.Y > other.Y ? vector.Y : other.Y;
            vector.Z = vector.Z > other.Z ? vector.Z : other.Z;
            return (vector);
        }

        public static Vector3d Min(Vector3d left, Vector3d right)
        {
            return (left.LengthSquared < right.LengthSquared ? left : right);
        }

        public static Vector3d Max(Vector3d left, Vector3d right)
        {
            return (left.LengthSquared >= right.LengthSquared ? left : right);
        }

        /// <summary>
        /// Összekapcsolja a vektort a megadott minimális és maximális vektorokkal
        /// </summary>
        /// <param name="vec">Input vektor</param>
        /// <param name="min">Minimum vektor</param>
        /// <param name="max">Maximum vektor</param>
        /// <returns>Az összekapcsolt vektor</returns>
        public static Vector3d Clamp(Vector3d vec, Vector3d min, Vector3d max)
        {
            vec.X = vec.X < min.X ? min.X : vec.X > max.X ? max.X : vec.X;
            vec.Y = vec.Y < min.Y ? min.Y : vec.Y > max.Y ? max.Y : vec.Y;
            vec.Z = vec.Z < min.Z ? min.Z : vec.Z > max.Z ? max.Z : vec.Z;
            return (vec);
        }

        #endregion

        #region Operators

        /// <summary>
        /// Két vektor összege
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Összegvektor vagy eredő</returns>
        public static Vector3d operator +(Vector3d vector, Vector3d other)
        {
            return new Vector3d(vector.X + other.X, vector.Y + other.Y, vector.Z + other.Z);
        }

        /// <summary>
        /// Két vektor különbsége
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Különbségvektor</returns>
        public static Vector3d operator -(Vector3d vector, Vector3d other)
        {
            return new Vector3d(vector.X - other.X, vector.Y - other.Y, vector.Z - other.Z);
        }

        /// <summary>
        /// Vektor szorzása skalárral
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="scalar">Második összetevő</param>
        /// <returns>Egy vektor skalárszorosa</returns>
        public static Vector3d operator *(Vector3d vector, double scalar)
        {
            return new Vector3d(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
        }

        /// <summary>
        /// Vektor szorzása skalárral
        /// </summary>
        /// <param name="scalar">Első összetevő</param>
        /// <param name="vector">Második összetevő</param>
        /// <returns>Egy vektor skalárszorosa</returns>
        public static Vector3d operator *(double scalar, Vector3d vector)
        {
            return (vector * scalar);
        }

        /// <summary>
        /// Vektor osztása skalárral
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="scalar">Második összetevő</param>
        /// <returns>Egy vektor és skalár hányadosa</returns>
        public static Vector3d operator /(Vector3d vector, double scalar)
        {
            return new Vector3d(vector.X / scalar, vector.Y / scalar, vector.Z / scalar);
        }

        /// <summary>
        /// Vektor tükrözése
        /// </summary>
        /// <param name="vector">A vektor amit tükrözünk</param>
        /// <returns>A tükrözött vektor</returns>
        public static Vector3d operator -(Vector3d vector)
        {
            return new Vector3d(-vector.X, -vector.Y, -vector.Z);
        }

        /// <summary>
        /// Vektor megerősítése
        /// </summary>
        /// <param name="vector">A vektor amit megerősítünk</param>
        /// <returns>A megerősített vektor</returns>
        public static Vector3d operator +(Vector3d vector)
        {
            return new Vector3d(+vector.X, +vector.Y, +vector.Z);
        }

        /// <summary>
        /// Két vektor skaláris szorzata (dot product)
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Skalár</returns>
        public static double operator *(Vector3d vector, Vector3d other)
        {
            return (vector.X * other.X + vector.Y * other.Y + vector.Z * other.Z);
        }

        /// <summary>
        /// Két vektor vektoriális szorzata (crossproduct)
        /// </summary>
        /// <param name="vector">Első összetevő</param>
        /// <param name="other">Második összetevő</param>
        /// <returns>Vektor</returns>
        /// <implementation>
        /// A vektoriális szorzás nem kommutatív
        /// </implementation>
        public static Vector3d operator %(Vector3d vector, Vector3d other)
        {
            return new Vector3d(vector.Y * other.Z - vector.Z * other.Y, vector.Z * other.X - vector.X * other.Z, vector.X * other.Y - vector.Y * other.X);
        }

        #endregion
        
        #region Functions

        /// <summary>
        /// A meglévő vektornak egy eltérő vektorral alkotott összege
        /// </summary>
        /// <param name="other">Vektor</param>
        /// <returns>Összegvektor vagy eredő</returns>
        public Vector3d Add(Vector3d other)
        {
            return (Add(this, other));
        }

        /// <summary>
        /// A meglévő vektornak egy eltérő vektorral alkotott különbsége
        /// </summary>
        /// <param name="other">Vektor</param>
        /// <returns>Különbségvektor</returns>
        public Vector3d Sub(Vector3d other)
        {
            return (Sub(this, other));
        }

        /// <summary>
        /// A meglévő vektornak egy skalárral alkotott szorzata
        /// </summary>
        /// <param name="scalar">Skalár</param>
        /// <returns>Eredményvektor</returns>
        public Vector3d Mult(double scalar)
        {
            return (Mult(this, scalar));
        }

        /// <summary>
        /// A meglévő vektornak egy skalárral alkotott hányadosa
        /// </summary>
        /// <param name="scalar">Skalár</param>
        /// <returns>Eredményvektor</returns>
        public Vector3d Div(double scalar)
        {
            return (Div(this, scalar));
        }

        /// <summary>
        /// A meglévő vektornak egy eltérő vektorral képzett vektoriális szorzata (crossproduct)
        /// </summary>
        /// <param name="other">Vektor</param>
        /// <returns>Eredményvektor</returns>
        public Vector3d CrossProduct(Vector3d other)
        {
            return (CrossProduct(this, other));
        }

        /// <summary>
        /// A meglévő vektornak egy eltérő vektorral képzett skaláris szorzata (dot product)
        /// </summary>
        /// <param name="other">Vektor</param>
        /// <returns>Skalár</returns>
        public double DotProduct(Vector3d other)
        {
            return (DotProduct(this, other));
        }

        /// <summary>
        /// A meglévő vektor és egy másik vektor között értelmezett szög fokban
        /// </summary>
        /// <param name="other">Vektor</param>
        /// <returns>Szög</returns>
        public double Angle(Vector3d other)
        {
            return (Angle(this, other));
        }

        /// <summary>
        /// A meglévő vektor és egy másik vektor között értelmezett távolság
        /// </summary>
        /// <param name="other">A meglévő vektortól eltérő másik vektor</param>
        /// <returns>Skalár</returns>
        public double Distance(Vector3d other)
        {
            return Distance(this, other);
        }

        /// <summary>
        /// Egységvektor készítése
        /// </summary>
        public void Normalize()
        {
            if (length == 0.0)
            {
                return;
            }
            else
            {
                double scale = 1.0 / length;
                Mult(scale);
            }
        }

        /// <summary>
        /// A vektor minden komponense negatív lesz
        /// </summary>
        public void Negate()
        {
            this.X = -this.X;
            this.Y = -this.Y;
            this.Z = -this.Z;
        }

        /// <summary>
        /// A vektor komponenseinek skálázása
        /// </summary>
        /// <param name="sx">Az X tengely menti skálázás factora</param>
        /// <param name="sy">Az Y tengely menti skálázás factora</param>
        /// <param name="sz"> A Z tengely menti skálázás factora</param>
        public void Scale(double sX, double sY, double sZ)
        {
            this.X = X * sX;
            this.Y = Y * sY;
            this.Z = Z * sZ;
        }

        /// <summary>
        /// Y-tengely körüli forgatás (Yaw)
        /// </summary>
        /// <param name="degree">A forgatás szöge fokban</param>
        /// <returns>Az Y-tengely körül elforgatott vektor</returns>
        public void Yaw(double degree)
        {
            X = Yaw(this, degree).X;
            Y = Yaw(this, degree).Y;
            Z = Yaw(this, degree).Z;
        }

         /// <summary>
        /// X-tengely körüli forgatás (Pitch)
        /// </summary>
        /// <param name="degree">A forgatás szöge fokban</param>
        /// <returns>Az X-tengely körül elforgatott vektor</returns>
        public void Pitch(double degree)
        {
            X = Pitch(this, degree).X;
            Y = Pitch(this, degree).Y;
            Z = Pitch(this, degree).Z;
        }

        /// <summary>
        /// Z-tengely körüli forgatás (Roll)
        /// </summary>
        /// <param name="degree">A forgatás szöge fokban</param>
        /// <returns>A Z-tengely körül elforgatott vektor</returns>
        public void Roll(double degree)
        {
            X = Roll(this, degree).X;
            Y = Roll(this, degree).Y;
            Z = Roll(this, degree).Z;
        }

        #endregion

        #endregion

        #region Private Methods

        /// <summary>
        /// A vektor hosszát adja meg
        /// </summary>
        private double CalcLength()
        {
            return (Math.Sqrt(CalcLengthSquared()));
        }

        /// <summary>
        /// A vektor hosszának négyzetét adja meg
        /// </summary>
        /// <returns></returns>
        private double CalcLengthSquared()
        {
            return (X * X + Y * Y + Z * Z);
        }

        #endregion

        #region Private Static Fields

        /// <summary>
        /// Az üzenet megjelenítésre kerül, ha nem megfelelő elemszámú tömböt alkalmazunk
        /// </summary>
        private static string THREE_COMPONENTS = "A tömbnek pontosan három komponenst kell tartalmaznia (x,y,z)";

        #endregion

        #region Public Static Fields

        /// <summary>
        /// Egységvektor az X-tengely mentén (1,0,0)
        /// </summary>
        public static readonly Vector3d UnitX = new Vector3d(1, 0, 0);

        /// <summary>
        /// Egységvektor az Y-tengely mentén (0,1,0)
        /// </summary>
        public static readonly Vector3d UnitY = new Vector3d(0, 1, 0);

        /// <summary>
        /// Egységvektor a Z-tengely mentén (0,0,1)
        /// </summary>
        public static readonly Vector3d UnitZ = new Vector3d(0, 0, 1);

        /// <summary>
        /// Zéró vektor definiálása (0,0,0)
        /// </summary>
        public static readonly Vector3d Zero = new Vector3d(0, 0, 0);

        /// <summary>
        /// A vektor minden komponense egy (1,1,1)
        /// </summary>
        public static readonly Vector3d One = new Vector3d(1, 1, 1);

        #endregion

        #region Interface Implementations

        #region CompareTo

        /// <summary>
        /// A vektorok hossza alapján történik az összehasonlítás rendezéskor
        /// </summary>
        /// <param name="other">A vektor amivel összehasonlítjuk a példányt</param>
        /// <returns>
        /// +1, ha a példány vektor hossza nagyobb  mint amivel összehasonlítjuk
        /// -1, ha a példány vektor hossza kisebb  mint amivel összehasonlítjuk
        ///  0, ha a példány vektor hossza egyenlő mint amivel összehasonlítjuk
        /// </returns>
        public int CompareTo(Vector3d other)
        {
            if (this < other)
            {
                return (-1);
            }
            
            if (this > other)
            {
                return (+1);
            }

            return (0);
        }

        public int CompareTo(Object obj)
        {
            if (obj is Vector3d)
            {
                return CompareTo((Vector3d)obj);
            }
            else
            {
                //Az obj nem Vector típus, kivétel dobással jelezzük
                throw new ArgumentException("Csak Vektor típus hasonlítható össze!");
            }
        }

        #endregion

        #region Equals

        public bool Equals(Vector3d other)
        {
            return (Equals(this, other));
        }

        public override bool Equals(object obj)
        {
            if ((obj == null) || !(obj is Vector3d))
            {
                return false;
            }

            return (Equals(this, (Vector3d)obj));
        }

        public static bool Equals(Vector3d vector, Vector3d other)
        {
            return ((vector.X.Equals(other.X) && vector.Y.Equals(other.Y)) && vector.Z.Equals(other.Z));
        }

        #endregion

        #region GetHashCode

        public override int GetHashCode()
        {
            return (this.X.GetHashCode() ^ this.Y.GetHashCode() ^ this.Z.GetHashCode());
        }

        #endregion

        #region Logic Operators

        /// <summary>
        /// Összehasonlítja két vektor hosszát (kisebb mint)
        /// </summary>
        /// <param name="vector">Vektor amit hasonlítunk</param>
        /// <param name="other">Vektor amihez hasonlítunk</param>
        /// <returns>Az érték (true), ha vector kisebb mint other, egyébként (false)</returns>
        public static bool operator <(Vector3d vector, Vector3d other)
        {
            return (vector.Length < other.Length);
        }

        /// <summary>
        /// Összehasonlítja két vektor hosszát (nagyobb mint)
        /// </summary>
        /// <param name="vector">Vektor amit hasonlítunk</param>
        /// <param name="other">Vektor amihez hasonlítunk</param>
        /// <returns>Az érték (true), ha vector nagyobb mint other, egyébként (false)</returns>
        public static bool operator >(Vector3d vector, Vector3d other)
        {
            return (vector.Length > other.Length);
        }

        /// <summary>
        /// Összehasonlítja két vektor hosszát (kisebb vagy egyenlő mint)
        /// </summary>
        /// <param name="vector">Vektor amit hasonlítunk</param>
        /// <param name="other">Vektor amihez hasonlítunk</param>
        /// <returns>Az érték (true), ha vector kisebb vagy egyenlő mint other, egyébként (false)</returns>
        public static bool operator <=(Vector3d vector, Vector3d other)
        {
            return (vector.Length <= other.Length);
        }

        /// <summary>
        /// Összehasonlítja két vektor hosszát (nagyobb vagy egyenlő mint)
        /// </summary>
        /// <param name="vector">Vektor amit hasonlítunk</param>
        /// <param name="other">Vektor amihez hasonlítunk</param>
        /// <returns>Az érték (true), ha vector nagyobb vagy egyenlő mint other, egyébként (false)</returns>
        public static bool operator >=(Vector3d vector, Vector3d other)
        {
            return (vector.Length >= other.Length);
        }

        public static bool operator ==(Vector3d me, Vector3d other)
        {
            return ((me.X == other.X) && (me.Y == other.Y) && (me.Z == other.Z));
        }

        public static bool operator !=(Vector3d me, Vector3d other)
        {
            return !(me == other);
        }

        #endregion

        #region ToString

        /// <summary>
        /// A vektor belső állapotának szöveges megjelenítése
        /// </summary>
        /// <returns>A vektort reprezentáló szöveg (string)</returns>
        public override string ToString()
        {
            return (String.Format("X={0} Y={1} Z={2}", X, Y, Z));
        }

        #endregion

        #endregion

        #region ISerializable Members

        public void GetObjectData(SerializationInfo info, StreamingContext context) 
        {
            info.AddValue("X", x);
            info.AddValue("Y", y);
            info.AddValue("Z", z);
        }
        public Vector3d(SerializationInfo info, StreamingContext context) 
        {
            x = info.GetDouble("X");
            y = info.GetDouble("Y");
            z = info.GetDouble("Z");
        }

        #endregion
    }
}

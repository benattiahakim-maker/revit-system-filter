using System;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>Vecteur ou point (unités de l'appelant : pieds Revit ou mm).</summary>
    public readonly record struct Vec3(double X, double Y, double Z);

    /// <summary>
    /// Transformation rigide comme Revit <c>Transform</c> : <c>P' = Origin + x·BasisX + y·BasisY + z·BasisZ</c>.
    /// </summary>
    public readonly record struct Affine(Vec3 Origin, Vec3 BasisX, Vec3 BasisY, Vec3 BasisZ)
    {
        public static Affine Identity { get; } =
            new Affine(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1));

        public Vec3 OfPoint(Vec3 p) => new Vec3(
            Origin.X + p.X * BasisX.X + p.Y * BasisY.X + p.Z * BasisZ.X,
            Origin.Y + p.X * BasisX.Y + p.Y * BasisY.Y + p.Z * BasisZ.Y,
            Origin.Z + p.X * BasisX.Z + p.Y * BasisY.Z + p.Z * BasisZ.Z);
    }

    /// <summary>Maths de boîtes sans dépendance Revit (testées sous Linux).</summary>
    public static class BoxMath
    {
        /// <summary>Pieds internes de Revit → millimètres.</summary>
        public const double FeetToMm = 304.8;

        /// <summary>
        /// Transforme les 8 coins de la boîte [min, max] par chaque transformation de <paramref name="chain"/>
        /// (dans l'ordre : la première s'applique en premier) puis renvoie la boîte alignée sur les axes qui les contient.
        /// </summary>
        public static (Vec3 Min, Vec3 Max) TransformBox(Vec3 min, Vec3 max, params Affine[] chain)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
            for (var i = 0; i < 8; i++)
            {
                var p = new Vec3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
                foreach (var t in chain) p = t.OfPoint(p);
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            return (new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
        }

        /// <summary>
        /// Boîte (en pieds, dans le repère local de la maquette d'origine) → <see cref="BBoxRecord"/> en mm dans le repère
        /// de l'hôte. <paramref name="boxToModel"/> : transformation propre de la boîte (BoundingBoxXYZ.Transform) ;
        /// <paramref name="modelToHost"/> : GetTotalTransform() du lien (identité pour l'hôte), dont l'origine est en pieds.
        /// </summary>
        public static BBoxRecord ToHostMm(Vec3 min, Vec3 max, Affine boxToModel, Affine modelToHost)
        {
            var (lo, hi) = TransformBox(min, max, boxToModel, modelToHost);
            return new BBoxRecord
            {
                Min = new[] { Mm(lo.X), Mm(lo.Y), Mm(lo.Z) },
                Max = new[] { Mm(hi.X), Mm(hi.Y), Mm(hi.Z) },
            };
        }

        /// <summary>Pieds → mm, arrondi au dixième de millimètre.</summary>
        public static double Mm(double feet) => Math.Round(feet * FeetToMm, 1);
    }
}

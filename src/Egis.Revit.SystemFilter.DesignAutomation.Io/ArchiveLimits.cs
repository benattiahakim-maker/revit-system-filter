namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>Plafonds appliqués au ZIP d'entrée (taille réellement décompressée, pas celle annoncée).</summary>
    public sealed class ArchiveLimits
    {
        /// <summary>Nombre maximal d'entrées (fichiers + dossiers).</summary>
        public int MaxEntries { get; set; } = 1000;

        /// <summary>Taille décompressée totale maximale (4 Gio par défaut).</summary>
        public long MaxTotalBytes { get; set; } = 4L * 1024 * 1024 * 1024;

        /// <summary>Longueur maximale d'un chemin d'entrée (caractères).</summary>
        public int MaxPathLength { get; set; } = 259;

        public static ArchiveLimits Default => new ArchiveLimits();
    }
}

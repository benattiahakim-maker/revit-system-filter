using Egis.Revit.SystemFilter.WorkItemRunner.Mvvm;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>
/// Ligne du tableau des résultats, présenté en arbre : une pièce, et sous elle ses portes, équipements et terminaux. Les cellules
/// suivent les colonnes de <see cref="ResultView.RoomColumns"/> ; l'indexeur permet la liaison <c>[i]</c> des colonnes générées.
/// </summary>
public sealed class TableRow : ObservableObject
{
    /// <summary>Retrait d'un niveau de l'arbre, en pixels.</summary>
    public const double IndentPerLevel = 16;

    private bool _isExpanded;

    public TableRow(IReadOnlyList<string?> cells, IReadOnlyList<TableRow>? children = null, int depth = 0)
    {
        Cells = cells;
        Children = children ?? [];
        Depth = depth;
    }

    public IReadOnlyList<string?> Cells { get; }

    public IReadOnlyList<TableRow> Children { get; }

    public int Depth { get; }

    public bool HasChildren => Children.Count > 0;

    public double Indent => Depth * IndentPerLevel;

    /// <summary>Repliée par défaut : le tableau s'ouvre sur la liste des pièces.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(Glyph));
            }
        }
    }

    /// <summary>Flèche de dépliage ; vide pour une ligne sans enfant.</summary>
    public string Glyph => HasChildren ? (IsExpanded ? "▼" : "▶") : string.Empty;

    public string? this[int index] => index >= 0 && index < Cells.Count ? Cells[index] : null;

    /// <summary>Lignes visibles, dans l'ordre de l'arbre : chaque ligne, puis ses enfants si elle est dépliée.</summary>
    public static IReadOnlyList<TableRow> Visible(IEnumerable<TableRow> roots)
    {
        var rows = new List<TableRow>();
        void Add(TableRow row)
        {
            rows.Add(row);
            if (row.IsExpanded)
            {
                foreach (var child in row.Children)
                {
                    Add(child);
                }
            }
        }

        foreach (var root in roots)
        {
            Add(root);
        }

        return rows;
    }
}

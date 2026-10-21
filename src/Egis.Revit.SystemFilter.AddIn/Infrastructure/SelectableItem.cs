namespace Egis.Revit.SystemFilter.AddIn.Infrastructure
{
    /// <summary>
    /// Élément sélectionnable (case à cocher) d'une liste de l'interface.
    /// </summary>
    public sealed class SelectableItem : ObservableObject
    {
        private bool _isSelected;

        public SelectableItem(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}

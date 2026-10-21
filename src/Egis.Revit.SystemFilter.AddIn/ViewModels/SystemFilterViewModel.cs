using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Egis.Revit.SystemFilter.AddIn.Application;
using Egis.Revit.SystemFilter.AddIn.Infrastructure;
using Egis.Revit.SystemFilter.Core.Abstractions;
using Egis.Revit.SystemFilter.Core.Models;

namespace Egis.Revit.SystemFilter.AddIn.ViewModels
{
    /// <summary>
    /// ViewModel de la fenêtre de vérification : expose les listes sélectionnables
    /// et les commandes « Filtrer » / « Enregistrer la vue ». Ne contient aucune
    /// logique Revit directe : tout passe par les services Core, via le dispatcher.
    /// </summary>
    public sealed class SystemFilterViewModel : ObservableObject
    {
        private readonly RevitSessionContext _context;
        private readonly RevitActionDispatcher _dispatcher;
        private readonly IViewFilterService _filterService;
        private readonly IVerificationViewService _viewService;

        private string _viewName = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _includePsyWorksets;

        public SystemFilterViewModel(
            RevitSessionContext context,
            SystemCatalog catalog,
            RevitActionDispatcher dispatcher,
            IViewFilterService filterService,
            IVerificationViewService viewService)
        {
            _context = context;
            _dispatcher = dispatcher;
            _filterService = filterService;
            _viewService = viewService;

            SystemTypes = ToSelectable(catalog.SystemTypeNames);
            Worksets = ToSelectable(catalog.WorksetNames);
            Materials = ToSelectable(catalog.MaterialNames);

            FilterCommand = new RelayCommand(ApplyFilters);
            SaveViewCommand = new RelayCommand(SaveView, () => !string.IsNullOrWhiteSpace(ViewName));
        }

        public ObservableCollection<SelectableItem> SystemTypes { get; }

        public ObservableCollection<SelectableItem> Worksets { get; }

        public ObservableCollection<SelectableItem> Materials { get; }

        public bool HasWorksets => Worksets.Count > 0;

        public bool IncludePsyWorksets
        {
            get => _includePsyWorksets;
            set => SetProperty(ref _includePsyWorksets, value);
        }

        public string ViewName
        {
            get => _viewName;
            set
            {
                if (SetProperty(ref _viewName, value))
                {
                    SaveViewCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public RelayCommand FilterCommand { get; }

        public RelayCommand SaveViewCommand { get; }

        private void ApplyFilters()
        {
            var request = BuildRequest();
            if (request.IsEmpty)
            {
                StatusMessage = "Sélectionnez au moins un type de système ou un matériel.";
                return;
            }

            _dispatcher.Post(application =>
            {
                var view = _context.UiDocument.ActiveView;
                var result = _filterService.ApplyFilters(
                    _context.HostDocument,
                    _context.LinkedDocument,
                    view,
                    _context.UserPrefix,
                    request);

                StatusMessage = result.Success
                    ? $"Filtres appliqués : {result.ReseauRuleCount} réseau(x), {result.EquipmentRuleCount} équipement(s)."
                    : $"Échec de l'application des filtres : {result.Message}";
            });
        }

        private void SaveView()
        {
            var viewName = ViewName;
            _dispatcher.Post(application =>
            {
                try
                {
                    var source = _context.UiDocument.ActiveView;
                    var saved = _viewService.DuplicateAndSaveView(
                        _context.HostDocument, source, _context.UserPrefix, viewName);

                    _context.UiDocument.ActiveView = saved;
                    StatusMessage = $"Vue enregistrée : {saved.Name}";
                }
                catch (Exception exception)
                {
                    StatusMessage = $"Échec de l'enregistrement : {exception.Message}";
                }
            });
        }

        private SystemFilterRequest BuildRequest() => new SystemFilterRequest(
            SystemTypes.Where(item => item.IsSelected).Select(item => item.Name),
            Materials.Where(item => item.IsSelected).Select(item => item.Name),
            Worksets.Where(item => item.IsSelected).Select(item => item.Name),
            IncludePsyWorksets);

        private static ObservableCollection<SelectableItem> ToSelectable(
            System.Collections.Generic.IEnumerable<string> names) =>
            new ObservableCollection<SelectableItem>(names.Select(name => new SelectableItem(name)));
    }
}

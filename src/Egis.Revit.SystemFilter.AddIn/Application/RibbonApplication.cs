using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace Egis.Revit.SystemFilter.AddIn.Application
{
    /// <summary>
    /// Point d'entrée de l'add-in : crée l'onglet et le bouton du ruban au démarrage.
    /// </summary>
    public sealed class RibbonApplication : IExternalApplication
    {
        private const string TabName = "Egis BIM";
        private const string PanelName = "Vérification CVC";
        private const string IconResourceName = "EgisIcon.jpg";

        public Result OnStartup(UIControlledApplication application)
        {
            CreateTab(application, TabName);
            var panel = application.CreateRibbonPanel(TabName, PanelName);

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var buttonData = new PushButtonData(
                "EgisSystemFilterButton",
                "Filtrer\nsystèmes",
                assemblyPath,
                typeof(ShowFilterWindowCommand).FullName)
            {
                ToolTip = "Filtrer dynamiquement les systèmes CVCD dans la vue 3D active.",
                LongDescription =
                    "Affiche uniquement les réseaux et équipements rattachés aux systèmes " +
                    "sélectionnés dans la maquette CVC liée, puis permet d'enregistrer la vue filtrée.",
            };

            if (panel.AddItem(buttonData) is PushButton button)
            {
                var icon = LoadEmbeddedImage(IconResourceName);
                if (icon is not null)
                {
                    button.LargeImage = icon;
                    button.Image = icon;
                }
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static void CreateTab(UIControlledApplication application, string tabName)
        {
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // L'onglet existe déjà (autre add-in Egis) : on réutilise l'existant.
            }
        }

        private static ImageSource? LoadEmbeddedImage(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
        }
    }
}

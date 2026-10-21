namespace Egis.Revit.SystemFilter.Logging;

/// <summary>Codes des événements minimaux (section 5).</summary>
public static class LogEventCodes
{
    public const string ExtractionStarted = "EXTRACTION_STARTED";
    public const string ConfigurationValidated = "CONFIGURATION_VALIDATED";
    public const string DocumentAccessible = "DOCUMENT_ACCESSIBLE";
    public const string CollectionCompleted = "COLLECTION_COMPLETED";
    public const string AnomaliesDetected = "ANOMALIES_DETECTED";
    public const string ExportCompleted = "EXPORT_COMPLETED";
    public const string ExtractionFailed = "EXTRACTION_FAILED";
    public const string ExtractionSummary = "EXTRACTION_SUMMARY";
    public const string RoomExtractionFailed = "ROOM_EXTRACTION_FAILED";
    public const string DocumentCloseFailed = "DOCUMENT_CLOSE_FAILED";
    public const string RoomCollectionDetails = "ROOM_COLLECTION_DETAILS";

    /// <summary>DUPLICATE_NUMBER signalé entre variantes différentes dont le jeu d'options n'a pas pu être lu (étape <c>quality</c>).</summary>
    public const string DuplicateOptionSetUnknown = "DUPLICATE_OPTION_SET_UNKNOWN";

    /// <summary>Préparation des volumes (EGR-16) : mode, réglage du document, activation temporaire, annulation.</summary>
    public const string VolumeComputation = "VOLUME_COMPUTATION";

    /// <summary>volumeMode compute : activation temporaire du calcul impossible ; volumes non disponibles.</summary>
    public const string VolumeComputeFailed = "VOLUME_COMPUTE_FAILED";

    /// <summary>RVT d'une version antérieure mis à niveau en mémoire à l'ouverture, sans sauvegarde (étape <c>document</c>).</summary>
    public const string DocumentUpgradeInMemory = "DOCUMENT_UPGRADE_IN_MEMORY";

    /// <summary>Messages d'échec émis par des liens à l'ouverture, traités par défaut par Revit (étape <c>document</c>).</summary>
    public const string LinkedDocumentFailures = "LINKED_DOCUMENT_FAILURES";

    /// <summary>Portes, équipements ou terminaux collectés et rattachés aux pièces (étape <c>collection</c>).</summary>
    public const string ElementsCollected = "ELEMENTS_COLLECTED";

    /// <summary>Collecte des portes, équipements ou terminaux en échec ; l'extraction des pièces continue.</summary>
    public const string ElementsCollectionFailed = "ELEMENTS_COLLECTION_FAILED";

    /// <summary>Lecture de tous les paramètres d'une pièce en échec ; les champs du contrat restent exportés.</summary>
    public const string RoomParametersFailed = "ROOM_PARAMETERS_FAILED";

    /// <summary>Vues 3D des pièces exportées vers <c>Images/</c> (étape <c>collection</c>).</summary>
    public const string RoomImagesExported = "ROOM_IMAGES_EXPORTED";

    /// <summary>Export des vues 3D en échec, en tout ou partie ; l'extraction des pièces continue.</summary>
    public const string RoomImagesFailed = "ROOM_IMAGES_FAILED";

    /// <summary>Hauteurs des pièces mesurées par lancer de rayons (étape <c>collection</c>).</summary>
    public const string RoomHeightsMeasured = "ROOM_HEIGHTS_MEASURED";

    /// <summary>Mesure des hauteurs en échec pour une ou plusieurs pièces ; l'extraction continue.</summary>
    public const string RoomHeightsFailed = "ROOM_HEIGHTS_FAILED";
}

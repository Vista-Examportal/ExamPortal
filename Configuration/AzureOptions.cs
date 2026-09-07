namespace ExamPortal.Configuration
{
    /// <summary>
    /// RESERVED FOR FUTURE USE — this codebase does not currently use Azure Blob
    /// Storage anywhere (candidate resumes/offer letters are stored on local disk
    /// via Services/FileStorageService.cs and served through
    /// Controllers/SecureFilesController.cs). No functionality was changed to
    /// introduce this class.
    ///
    /// It's provided so that if/when Azure Blob Storage is adopted, the connection
    /// string has a strongly typed home ("Azure" configuration section) and never
    /// needs to be hardcoded — same override chain as the other options classes:
    /// appsettings -> User Secrets -> environment variables
    /// (Azure__StorageConnectionString) -> Azure Key Vault.
    /// </summary>
    public class AzureOptions
    {
        public const string SectionName = "Azure";

        public string StorageConnectionString { get; set; } = "";
        public string StorageContainerName { get; set; } = "";
    }
}

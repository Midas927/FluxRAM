using FluxRAM.App.Configuration;

namespace FluxRAM.App.Licensing;

public sealed record LicenseStatus(
    string MachineId,
    AppEditionFeatures Features,
    bool IsActivated,
    string Message,
    LicenseVerificationFailure Failure);

public sealed class LicenseManager
{
    private readonly IHardwareIdentifierProvider _hardwareIdentifierProvider;
    private readonly LicenseKeyVerifier _licenseKeyVerifier;
    private readonly LicenseActivationStore _activationStore;
    private readonly AppEdition _builtInEdition;

    public LicenseManager()
        : this(
            new HardwareIdentifierService(),
            new LicenseKeyVerifier(),
            new LicenseActivationStore(),
            AppEditionCatalog.CurrentEdition)
    {
    }

    public LicenseManager(
        IHardwareIdentifierProvider hardwareIdentifierProvider,
        LicenseKeyVerifier licenseKeyVerifier,
        LicenseActivationStore activationStore,
        AppEdition builtInEdition)
    {
        _hardwareIdentifierProvider = hardwareIdentifierProvider;
        _licenseKeyVerifier = licenseKeyVerifier;
        _activationStore = activationStore;
        _builtInEdition = builtInEdition;
    }

    public LicenseStatus GetStatus()
    {
        var machineId = _hardwareIdentifierProvider.GetCurrentMachineId();
        if (_builtInEdition == AppEdition.Pro)
        {
            return CreateProStatus(machineId, false, "Built-in Pro edition.");
        }

        var storedLicenseKey = _activationStore.Load();
        if (string.IsNullOrWhiteSpace(storedLicenseKey))
        {
            return CreateFreeStatus(machineId, "FluxRAM. Enter a Pro key to activate FluxRAM Pro.");
        }

        var verification = _licenseKeyVerifier.Verify(storedLicenseKey, machineId);
        return verification.IsValid
            ? CreateProStatus(machineId, true, "Pro edition activated on this computer.")
            : CreateFreeStatus(machineId, "Stored Pro key is invalid for this computer.", verification.Failure);
    }

    public LicenseStatus Activate(string licenseKey)
    {
        var machineId = _hardwareIdentifierProvider.GetCurrentMachineId();
        if (_builtInEdition == AppEdition.Pro)
        {
            return CreateProStatus(machineId, false, "Built-in Pro edition.");
        }

        var verification = _licenseKeyVerifier.Verify(licenseKey, machineId);
        if (!verification.IsValid)
        {
            return CreateFreeStatus(machineId, "Invalid Pro key.", verification.Failure);
        }

        _activationStore.Save(licenseKey);
        return CreateProStatus(machineId, true, "Pro edition activated on this computer.");
    }

    private static LicenseStatus CreateProStatus(string machineId, bool isActivated, string message)
    {
        return new LicenseStatus(
            machineId,
            AppEditionCatalog.For(AppEdition.Pro),
            isActivated,
            message,
            LicenseVerificationFailure.None);
    }

    private static LicenseStatus CreateFreeStatus(
        string machineId,
        string message,
        LicenseVerificationFailure failure = LicenseVerificationFailure.None)
    {
        return new LicenseStatus(
            machineId,
            AppEditionCatalog.For(AppEdition.Free),
            false,
            message,
            failure);
    }
}

using FluxRAM.App.Configuration;
using FluxRAM.App.Diagnostics;

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

    public LicenseManager()
        : this(
            new HardwareIdentifierService(),
            new LicenseKeyVerifier(),
            new LicenseActivationStore())
    {
    }

    public LicenseManager(
        IHardwareIdentifierProvider hardwareIdentifierProvider,
        LicenseKeyVerifier licenseKeyVerifier,
        LicenseActivationStore activationStore)
    {
        _hardwareIdentifierProvider = hardwareIdentifierProvider;
        _licenseKeyVerifier = licenseKeyVerifier;
        _activationStore = activationStore;
    }

    public LicenseStatus GetStatus()
    {
        if (!TryGetStableMachineId(out var machineId))
        {
            return CreateFreeStatus(
                string.Empty,
                "A stable machine identifier is unavailable.",
                LicenseVerificationFailure.HardwareUnavailable);
        }

        var stored = _activationStore.Read();
        if (stored.State == LicenseActivationReadState.Error)
        {
            return CreateFreeStatus(
                machineId,
                "The stored Pro key could not be read.",
                LicenseVerificationFailure.StorageError);
        }

        if (stored.State == LicenseActivationReadState.Missing || string.IsNullOrWhiteSpace(stored.LicenseKey))
        {
            return CreateFreeStatus(machineId, "FluxRAM. Enter a Pro key to activate FluxRAM Pro.");
        }

        return VerifyForCurrentMachine(stored.LicenseKey, machineId, allowLegacySession: false);
    }

    public LicenseStatus Activate(string licenseKey)
    {
        if (!TryGetStableMachineId(out var machineId))
        {
            return CreateFreeStatus(
                string.Empty,
                "A stable machine identifier is unavailable.",
                LicenseVerificationFailure.HardwareUnavailable);
        }

        var status = VerifyForCurrentMachine(licenseKey, machineId, allowLegacySession: true);
        if (status.Features.Edition != AppEdition.Pro ||
            status.Failure == LicenseVerificationFailure.LegacyKeyRequiresReplacement)
        {
            return status;
        }

        return _activationStore.Save(licenseKey)
            ? CreateProStatus(machineId, true, "Pro edition activated on this computer.")
            : CreateFreeStatus(
                machineId,
                "The Pro key is valid but could not be saved.",
                LicenseVerificationFailure.StorageError);
    }

    private LicenseStatus VerifyForCurrentMachine(
        string licenseKey,
        string machineId,
        bool allowLegacySession)
    {
        var claims = _licenseKeyVerifier.VerifyClaims(licenseKey);
        if (!claims.IsValid || claims.Payload is null)
        {
            return CreateFreeStatus(machineId, "Invalid Pro key.", claims.Failure);
        }

        if (MachineIdsEqual(claims.Payload.MachineId, machineId))
        {
            return CreateProStatus(machineId, true, "Pro edition activated on this computer.");
        }

        if (!TryGetLegacyMachineId(out var legacyMachineId))
        {
            return CreateFreeStatus(
                machineId,
                "The previous machine identifier cannot be verified. Request a replacement key for the current Machine ID.",
                LicenseVerificationFailure.LegacyIdentityUnavailable);
        }

        if (MachineIdsEqual(claims.Payload.MachineId, legacyMachineId))
        {
            const string message =
                "This legacy Pro key requires replacement for the current Machine ID.";
            return allowLegacySession
                ? CreateProStatus(
                    machineId,
                    false,
                    message,
                    LicenseVerificationFailure.LegacyKeyRequiresReplacement)
                : CreateFreeStatus(
                    machineId,
                    message,
                    LicenseVerificationFailure.LegacyKeyRequiresReplacement);
        }

        return CreateFreeStatus(
            machineId,
            "This Pro key does not belong to the current computer and must be replaced.",
            LicenseVerificationFailure.MachineMismatch);
    }

    private bool TryGetStableMachineId(out string machineId)
    {
        try
        {
            machineId = _hardwareIdentifierProvider.GetCurrentMachineId();
            return !string.IsNullOrWhiteSpace(machineId);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to read the stable machine identifier.", ex);
            machineId = string.Empty;
            return false;
        }
    }

    private bool TryGetLegacyMachineId(out string machineId)
    {
        try
        {
            machineId = _hardwareIdentifierProvider.GetLegacyMachineId();
            return !string.IsNullOrWhiteSpace(machineId);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to read the previous machine identifier.", ex);
            machineId = string.Empty;
            return false;
        }
    }

    private static bool MachineIdsEqual(string left, string right)
    {
        return string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static LicenseStatus CreateProStatus(
        string machineId,
        bool isActivated,
        string message,
        LicenseVerificationFailure failure = LicenseVerificationFailure.None)
    {
        return new LicenseStatus(
            machineId,
            AppEditionCatalog.For(AppEdition.Pro),
            isActivated,
            message,
            failure);
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
